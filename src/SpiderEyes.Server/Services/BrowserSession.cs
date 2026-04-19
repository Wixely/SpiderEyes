using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using SpiderEyes.Server.Configuration;
using SpiderEyes.Server.Models;

namespace SpiderEyes.Server.Services;

public sealed class BrowserSession : IAsyncDisposable
{
    private static readonly Regex RefPattern = new("^(?:ref=)?(?:e\\d+|f\\d+e\\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly SpiderEyesOptions _options;
    private readonly ILogger<BrowserSession> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly List<ConsoleEntry> _consoleEntries = [];
    private readonly List<NetworkEntry> _networkEntries = [];
    private readonly List<DialogRecord> _dialogs = [];
    private readonly Dictionary<string, RouteState> _routes = new(StringComparer.Ordinal);
    private readonly Dictionary<IPage, string> _pageIds = new();
    private readonly Dictionary<string, IPage> _pages = new(StringComparer.Ordinal);
    private readonly object _stateLock = new();

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private string? _currentTabId;
    private int _tabCounter;
    private PendingDialogAction? _nextDialogAction;
    private bool _tracingStarted;

    public BrowserSession(string sessionId, SpiderEyesOptions options, ILogger<BrowserSession> logger)
    {
        SessionId = sessionId;
        _options = options;
        _logger = logger;

        ArtifactDirectory = Path.GetFullPath(Path.Combine(_options.Session.ArtifactRoot, sessionId));
        Directory.CreateDirectory(ArtifactDirectory);
    }

    public string SessionId { get; }

    public string ArtifactDirectory { get; }

    public DateTimeOffset LastAccessUtc { get; private set; } = DateTimeOffset.UtcNow;

    public void Touch() => LastAccessUtc = DateTimeOffset.UtcNow;

    public async Task<T> RunExclusiveAsync<T>(Func<BrowserSession, CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            Touch();
            return await action(this, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_browser is not null && _context is not null)
        {
            return;
        }

        _playwright ??= await Playwright.CreateAsync();
        _browser ??= await LaunchBrowserAsync(cancellationToken);
        await RecreateContextAsync(null, cancellationToken);
    }

    public async Task<IPage> GetPageAsync(string? tabId, bool createIfMissing, CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(tabId) && _pages.TryGetValue(tabId, out var page))
        {
            _currentTabId = tabId;
            return page;
        }

        if (_currentTabId is not null && _pages.TryGetValue(_currentTabId, out page))
        {
            return page;
        }

        if (_pages.Count > 0)
        {
            var existing = _pages.OrderBy(pair => pair.Key, StringComparer.Ordinal).First();
            _currentTabId = existing.Key;
            return existing.Value;
        }

        if (!createIfMissing)
        {
            throw new InvalidOperationException("No active browser tab is available.");
        }

        return await NewPageAsync(cancellationToken);
    }

    public async Task<IPage> NewPageAsync(CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken);

        if (_context is null)
        {
            throw new InvalidOperationException("Browser context was not initialized.");
        }

        if (_pages.Count >= _options.Session.MaxTabs)
        {
            throw new InvalidOperationException($"Session already has the maximum {_options.Session.MaxTabs} tabs.");
        }

        var page = await _context.NewPageAsync();
        RegisterPage(page);
        return page;
    }

    public bool HasPages => _pages.Count > 0;

    public string GetTabId(IPage page)
    {
        if (_pageIds.TryGetValue(page, out var tabId))
        {
            return tabId;
        }

        throw new InvalidOperationException("The supplied page is not tracked by this session.");
    }

    public async Task<PageState?> BuildPageStateAsync(string? tabId, bool includeSnapshot, CancellationToken cancellationToken)
    {
        if (!HasPages)
        {
            return null;
        }

        var page = await GetPageAsync(tabId, createIfMissing: false, cancellationToken);
        var pageId = _pageIds[page];
        var title = await page.TitleAsync();
        var snapshot = includeSnapshot
            ? await page.Locator("body").AriaSnapshotAsync(new() { Mode = AriaSnapshotMode.Ai, Depth = 30 })
            : null;

        var tabs = _pages
            .Select(pair => new TabInfo
            {
                TabId = pair.Key,
                Url = pair.Value.Url,
                Title = SafeGetTitle(pair.Value),
                IsCurrent = string.Equals(pair.Key, pageId, StringComparison.Ordinal),
            })
            .OrderBy(static tab => tab.TabId, StringComparer.Ordinal)
            .ToArray();

        return new PageState
        {
            TabId = pageId,
            Url = page.Url,
            Title = title,
            Snapshot = snapshot,
            Summary = $"Page '{title ?? "(untitled)"}' at {page.Url}. Tabs: {_pages.Count}. Console entries: {_consoleEntries.Count}. Network entries: {_networkEntries.Count}.",
            Tabs = tabs,
        };
    }

    public ILocator ResolveTarget(IPage page, string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            throw new ArgumentException("Target is required.", nameof(target));
        }

        var trimmed = target.Trim();
        if (RefPattern.IsMatch(trimmed))
        {
            var normalized = trimmed.StartsWith("ref=", StringComparison.OrdinalIgnoreCase)
                ? trimmed[4..]
                : trimmed;
            return page.Locator($"aria-ref={normalized}");
        }

        return page.Locator(trimmed);
    }

    public IReadOnlyList<ConsoleEntry> GetConsoleEntries(int limit)
        => _consoleEntries.TakeLast(Math.Max(limit, 1)).ToArray();

    public IReadOnlyList<NetworkEntry> GetNetworkEntries(int limit)
        => _networkEntries.TakeLast(Math.Max(limit, 1)).ToArray();

    public IReadOnlyList<DialogRecord> GetDialogs()
        => _dialogs.ToArray();

    public IReadOnlyCollection<RouteRuleInfo> GetRoutes()
        => _routes.Values
            .OrderBy(static route => route.Info.RuleId, StringComparer.Ordinal)
            .Select(static route => route.Info)
            .ToArray();

    public async Task SetOfflineAsync(bool offline)
    {
        if (_context is null)
        {
            throw new InvalidOperationException("Browser context was not initialized.");
        }

        await _context.SetOfflineAsync(offline);
    }

    public async Task SetExtraHttpHeadersAsync(IDictionary<string, string>? headers)
    {
        if (_context is null)
        {
            throw new InvalidOperationException("Browser context was not initialized.");
        }

        await _context.SetExtraHTTPHeadersAsync(headers ?? new Dictionary<string, string>());
    }

    public string AddRoute(RouteMutation mutation)
    {
        var routeId = $"route-{_routes.Count + 1}";
        _routes[routeId] = new RouteState
        {
            Info = new RouteRuleInfo
            {
                RuleId = routeId,
                Pattern = mutation.Pattern,
                Action = mutation.Action,
                Status = mutation.Status,
                Body = mutation.Body,
                ContentType = mutation.ContentType,
            },
            Headers = mutation.Headers,
            AbortErrorCode = mutation.AbortErrorCode,
        };

        return routeId;
    }

    public bool RemoveRoute(string routeId) => _routes.Remove(routeId);

    public void ArmNextDialog(string action, string? promptText)
    {
        _nextDialogAction = new PendingDialogAction(action, promptText);
    }

    public async Task RecreateContextAsync(string? storageStatePath, CancellationToken cancellationToken)
    {
        if (_browser is null)
        {
            throw new InvalidOperationException("Browser was not initialized.");
        }

        if (_context is not null)
        {
            await _context.CloseAsync();
        }

        _pages.Clear();
        _pageIds.Clear();
        _currentTabId = null;
        _tracingStarted = false;

        var downloadsPath = Path.GetFullPath(Path.Combine(_options.Browser.DownloadsPath, SessionId));
        Directory.CreateDirectory(downloadsPath);

        var contextOptions = new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = _options.Browser.IgnoreHttpsErrors,
            Locale = _options.Browser.Locale,
            TimezoneId = _options.Browser.TimezoneId,
            AcceptDownloads = true,
            RecordVideoDir = null,
            StorageStatePath = storageStatePath,
            ViewportSize = new ViewportSize
            {
                Width = _options.Browser.ViewportWidth,
                Height = _options.Browser.ViewportHeight,
            },
        };

        _context = await _browser.NewContextAsync(contextOptions);
        HookContextEvents(_context);
        await _context.RouteAsync("**/*", HandleRouteAsync);
        await NewPageAsync(cancellationToken);
    }

    public async Task StartTracingAsync(string? title)
    {
        if (_context is null)
        {
            throw new InvalidOperationException("Browser context was not initialized.");
        }

        if (_tracingStarted)
        {
            return;
        }

        await _context.Tracing.StartAsync(new()
        {
            Title = title ?? $"SpiderEyes {SessionId}",
            Screenshots = true,
            Snapshots = true,
            Sources = true,
        });
        _tracingStarted = true;
    }

    public async Task<string> StopTracingAsync(string fileName)
    {
        if (_context is null)
        {
            throw new InvalidOperationException("Browser context was not initialized.");
        }

        var path = CreateArtifactPath(fileName, ".zip");
        if (_tracingStarted)
        {
            await _context.Tracing.StopAsync(new() { Path = path });
            _tracingStarted = false;
        }

        return path;
    }

    public string CreateArtifactPath(string? fileName, string extension)
    {
        var safeName = string.IsNullOrWhiteSpace(fileName)
            ? $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}{extension}"
            : fileName!;

        if (!Path.HasExtension(safeName))
        {
            safeName += extension;
        }

        safeName = Path.GetFileName(safeName);
        return Path.GetFullPath(Path.Combine(ArtifactDirectory, safeName));
    }

    public async ValueTask DisposeAsync()
    {
        if (_context is not null)
        {
            await _context.CloseAsync();
        }

        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();
        _lock.Dispose();
    }

    private async Task<IBrowser> LaunchBrowserAsync(CancellationToken cancellationToken)
    {
        BrowserTypeLaunchOptions launchOptions = new()
        {
            Channel = _options.Browser.Channel,
            Headless = _options.Browser.Headless,
            SlowMo = _options.Browser.SlowMoMs,
        };

        return _options.Browser.BrowserType.ToLowerInvariant() switch
        {
            "firefox" => await _playwright!.Firefox.LaunchAsync(launchOptions),
            "webkit" => await _playwright!.Webkit.LaunchAsync(launchOptions),
            _ => await _playwright!.Chromium.LaunchAsync(launchOptions),
        };
    }

    private void HookContextEvents(IBrowserContext context)
    {
        context.Page += (_, page) =>
        {
            RegisterPage(page);
        };

        context.Request += (_, request) =>
        {
            AddNetworkEntry(new NetworkEntry
            {
                Method = request.Method,
                Url = request.Url,
                ResourceType = request.ResourceType,
                FromRoute = false,
            });
        };

        context.Response += (_, response) =>
        {
            AddNetworkEntry(new NetworkEntry
            {
                Method = response.Request.Method,
                Url = response.Url,
                Status = response.Status,
                ResourceType = response.Request.ResourceType,
                FromRoute = false,
            });
        };
    }

    private void RegisterPage(IPage page)
    {
        if (_pageIds.ContainsKey(page))
        {
            return;
        }

        var tabId = $"tab-{Interlocked.Increment(ref _tabCounter)}";
        _pageIds[page] = tabId;
        _pages[tabId] = page;
        _currentTabId = tabId;

        page.Console += (_, message) =>
        {
            AddConsoleEntry(new ConsoleEntry
            {
                Type = message.Type,
                Text = message.Text,
                Location = message.Location,
            });
        };

        page.PageError += (_, error) =>
        {
            AddConsoleEntry(new ConsoleEntry
            {
                Type = "pageerror",
                Text = error,
            });
        };

        page.Dialog += async (_, dialog) =>
        {
            _dialogs.Add(new DialogRecord
            {
                Type = dialog.Type,
                Message = dialog.Message,
                DefaultValue = dialog.DefaultValue,
            });

            var action = _nextDialogAction;
            _nextDialogAction = null;
            if (action is null || string.Equals(action.Action, "dismiss", StringComparison.OrdinalIgnoreCase))
            {
                await dialog.DismissAsync();
                return;
            }

            if (string.Equals(action.Action, "accept", StringComparison.OrdinalIgnoreCase))
            {
                await dialog.AcceptAsync(action.PromptText);
                return;
            }

            await dialog.DismissAsync();
        };

        page.Close += (_, _) =>
        {
            if (!_pageIds.TryGetValue(page, out var id))
            {
                return;
            }

            _pageIds.Remove(page);
            _pages.Remove(id);
            if (_currentTabId == id)
            {
                _currentTabId = _pages.Keys.OrderBy(static key => key, StringComparer.Ordinal).FirstOrDefault();
            }
        };
    }

    private async Task HandleRouteAsync(IRoute route)
    {
        foreach (var state in _routes.Values)
        {
            if (!WildcardMatcher.IsMatch(state.Info.Pattern, route.Request.Url))
            {
                continue;
            }

            AddNetworkEntry(new NetworkEntry
            {
                Method = route.Request.Method,
                Url = route.Request.Url,
                ResourceType = route.Request.ResourceType,
                Status = state.Info.Status,
                FromRoute = true,
            });

            switch (state.Info.Action.ToLowerInvariant())
            {
                case "fulfill":
                    await route.FulfillAsync(new()
                    {
                        Status = state.Info.Status ?? StatusCodes.Status200OK,
                        Body = state.Info.Body ?? string.Empty,
                        ContentType = state.Info.ContentType,
                        Headers = state.Headers,
                    });
                    return;

                case "abort":
                    await route.AbortAsync(state.AbortErrorCode);
                    return;

                default:
                    await route.ContinueAsync();
                    return;
            }
        }

        await route.ContinueAsync();
    }

    private void AddConsoleEntry(ConsoleEntry entry)
    {
        _consoleEntries.Add(entry);
        Trim(_consoleEntries, _options.Session.MaxConsoleEntries);
    }

    private void AddNetworkEntry(NetworkEntry entry)
    {
        _networkEntries.Add(entry);
        Trim(_networkEntries, _options.Session.MaxNetworkEntries);
    }

    private static void Trim<T>(List<T> entries, int maxEntries)
    {
        if (entries.Count <= maxEntries)
        {
            return;
        }

        entries.RemoveRange(0, entries.Count - maxEntries);
    }

    private static string? SafeGetTitle(IPage page)
    {
        try
        {
            return page.TitleAsync().GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }

    private sealed class PendingDialogAction
    {
        public PendingDialogAction(string action, string? promptText)
        {
            Action = action;
            PromptText = promptText;
        }

        public string Action { get; }

        public string? PromptText { get; }
    }

    private sealed class RouteState
    {
        public required RouteRuleInfo Info { get; init; }

        public Dictionary<string, string>? Headers { get; init; }

        public string? AbortErrorCode { get; init; }
    }
}

internal static class WildcardMatcher
{
    public static bool IsMatch(string pattern, string value)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        var regex = "^" + Regex.Escape(pattern)
            .Replace("\\*\\*", ".*", StringComparison.Ordinal)
            .Replace("\\*", "[^/]*", StringComparison.Ordinal) + "$";

        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase);
    }
}
