using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using PlaywrightMCPSharp.Server.Configuration;
using PlaywrightMCPSharp.Server.Models;

namespace PlaywrightMCPSharp.Server.Services;

public sealed class BrowserSession : IAsyncDisposable
{
    private static readonly Regex RefPattern = new("^(?:ref=)?(?:e\\d+|f\\d+e\\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>How long disposal waits for an in-flight command before tearing down regardless.</summary>
    private static readonly TimeSpan DisposeLockTimeout = TimeSpan.FromSeconds(10);

    /// <summary>How long each individual teardown step may run before it is abandoned.</summary>
    private static readonly TimeSpan DisposeStepTimeout = TimeSpan.FromSeconds(10);

    private readonly PlaywrightMCPSharpOptions _options;
    private readonly PlaywrightRuntimeService _playwrightRuntimeService;
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
    private EmulationSettings? _emulationOverride;
    private string? _pendingInitialStorageStatePath;
    private int _activeCommands;
    private volatile bool _disposed;

    public BrowserSession(
        string sessionId,
        string instanceName,
        BrowserInstanceOverrides? overrides,
        PlaywrightMCPSharpOptions options,
        PlaywrightRuntimeService playwrightRuntimeService,
        ILogger<BrowserSession> logger)
    {
        SessionId = sessionId;
        InstanceName = instanceName;
        Overrides = overrides ?? new BrowserInstanceOverrides();
        _options = options;
        _playwrightRuntimeService = playwrightRuntimeService;
        _logger = logger;

        ArtifactDirectory = Path.GetFullPath(Path.Combine(_options.Session.ArtifactRoot, sessionId, instanceName));
        Directory.CreateDirectory(ArtifactDirectory);
        DownloadsDirectory = Path.GetFullPath(Path.Combine(_options.Browser.DownloadsPath, sessionId, instanceName));
        Directory.CreateDirectory(DownloadsDirectory);
        _pendingInitialStorageStatePath = Overrides.InitialStorageStatePath;
    }

    public string SessionId { get; }

    public string InstanceName { get; }

    public BrowserInstanceOverrides Overrides { get; }

    public string ArtifactDirectory { get; }

    /// <summary>Directory that Playwright writes this instance's accepted downloads into.</summary>
    public string DownloadsDirectory { get; }

    public DateTimeOffset CreatedUtc { get; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastAccessUtc { get; private set; } = DateTimeOffset.UtcNow;

    public string EffectiveBrowserType => Overrides.BrowserType ?? _options.Browser.BrowserType;

    public bool EffectiveHeadless => Overrides.Headless ?? _options.Browser.Headless;

    public bool IsStarted => _browser is not null;

    public bool HasActiveCommand => Volatile.Read(ref _activeCommands) > 0;

    public bool IsDisposed => _disposed;

    public void Touch() => LastAccessUtc = DateTimeOffset.UtcNow;

    public async Task<T> RunExclusiveAsync<T>(Func<BrowserSession, CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _activeCommands);
        try
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                ThrowIfDisposed();
                Touch();
                return await action(this, cancellationToken);
            }
            finally
            {
                _lock.Release();
            }
        }
        finally
        {
            Interlocked.Decrement(ref _activeCommands);
        }
    }

    public object GetInstanceStatus() => new
    {
        instanceName = InstanceName,
        sessionId = SessionId,
        browserType = EffectiveBrowserType,
        headless = EffectiveHeadless,
        started = IsStarted,
        connected = _browser?.IsConnected ?? false,
        tabCount = _pages.Count,
        busy = HasActiveCommand,
        createdUtc = CreatedUtc,
        lastAccessUtc = LastAccessUtc,
        artifactDirectory = ArtifactDirectory,
    };

    public async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_browser is not null && _context is not null)
        {
            return;
        }

        var browserType = EffectiveBrowserType;
        if (string.IsNullOrWhiteSpace(GetEffectiveChannel()) &&
            !_playwrightRuntimeService.IsBrowserInstalled(browserType))
        {
            throw new InvalidOperationException(
                $"Playwright browser runtime '{browserType}' is not installed on this machine. " +
                $"Call the MCP tool 'browser_install_runtime' or run '{_playwrightRuntimeService.GetSuggestedInstallCommand(browserType)}' on the host.");
        }

        _playwright ??= await Playwright.CreateAsync();
        _browser ??= await LaunchBrowserAsync(cancellationToken);
        var initialStorageStatePath = _pendingInitialStorageStatePath;
        _pendingInitialStorageStatePath = null;
        await RecreateContextAsync(initialStorageStatePath, cancellationToken);
    }

    public async Task<IPage> GetPageAsync(string? tabId, bool createIfMissing, CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(tabId))
        {
            // An unknown tab id must fail rather than silently retargeting this instance's current
            // tab. Two different mistakes land here - a handle from another instance, and a handle
            // that has since been closed or invalidated by a context recreation - so the message
            // states both possibilities instead of asserting the cross-instance one.
            if (!_pages.TryGetValue(tabId, out var requested))
            {
                var knownTabs = _pages.Count == 0
                    ? "none"
                    : string.Join(", ", _pages.Keys.OrderBy(static key => key, StringComparer.Ordinal));

                throw new InvalidOperationException(
                    $"Tab '{tabId}' is not open in browser instance '{InstanceName}' of session '{SessionId}'. " +
                    $"Tabs open in this instance: {knownTabs}. Either the tab was closed (or invalidated by a " +
                    "context reset), or the identifier belongs to a different instance - tab identifiers are " +
                    "scoped to one instance. Call browser_tabs for the current list.");
            }

            _currentTabId = tabId;
            return requested;
        }

        if (_currentTabId is not null && _pages.TryGetValue(_currentTabId, out var page))
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

        var contextOptions = BuildContextOptions(storageStatePath);

        _context = await _browser.NewContextAsync(contextOptions);
        HookContextEvents(_context);
        await _context.RouteAsync("**/*", HandleRouteAsync);
        await NewPageAsync(cancellationToken);
    }

    /// <summary>
    /// Applies a device emulation override (or clears it when <paramref name="settings"/>
    /// is null) and recreates the browser context. Open tabs and in-memory state are reset.
    /// </summary>
    public async Task ApplyEmulationAsync(EmulationSettings? settings, CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken);
        _emulationOverride = settings;
        await RecreateContextAsync(null, cancellationToken);
    }

    /// <summary>Returns the emulation values that will be used for the current context.</summary>
    public object GetEffectiveEmulation()
    {
        var browser = _options.Browser;
        var emu = _emulationOverride;
        var device = ResolveDevice(emu?.DeviceName ?? browser.DeviceName);
        var (width, height) = ResolveViewport(emu, device);

        return new
        {
            source = emu is null ? "config" : "override",
            deviceName = emu?.DeviceName ?? browser.DeviceName,
            viewport = new { width, height },
            userAgent = emu?.UserAgent ?? device?.UserAgent ?? browser.UserAgent,
            deviceScaleFactor = emu?.DeviceScaleFactor ?? device?.DeviceScaleFactor ?? browser.DeviceScaleFactor,
            isMobile = emu?.IsMobile ?? device?.IsMobile ?? browser.IsMobile,
            hasTouch = emu?.HasTouch ?? device?.HasTouch ?? browser.HasTouch,
        };
    }

    /// <summary>Lists the Playwright device descriptor names available for emulation.</summary>
    public async Task<IReadOnlyList<string>> GetDeviceNamesAsync(CancellationToken cancellationToken)
    {
        _playwright ??= await Playwright.CreateAsync();
        return _playwright.Devices.Keys.OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToArray();
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
            Title = title ?? $"PlaywrightMCPSharp {SessionId}/{InstanceName}",
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        var lockAcquired = false;
        try
        {
            // Wait for any in-flight command before tearing the browser down; later waiters
            // observe _disposed after acquiring the lock and fail deterministically. The wait
            // is bounded because callers dispose instances one after another, so a hung
            // command here would otherwise stall cleanup for every other instance.
            lockAcquired = await _lock.WaitAsync(DisposeLockTimeout);
            if (!lockAcquired)
            {
                _logger.LogWarning(
                    "Browser instance '{Instance}' in session '{Session}' was still busy after {TimeoutSeconds}s; closing it anyway.",
                    InstanceName,
                    SessionId,
                    DisposeLockTimeout.TotalSeconds);
            }

            // Each step is isolated so a crashed browser cannot skip the steps that follow.
            await RunTeardownStepAsync("close the browser context", () => _context?.CloseAsync() ?? Task.CompletedTask);
            await RunTeardownStepAsync("close the browser", () => _browser?.CloseAsync() ?? Task.CompletedTask);
            await RunTeardownStepAsync("dispose the Playwright driver", () => Task.Run(() => _playwright?.Dispose()));
        }
        catch (Exception ex)
        {
            // Disposal is best effort and must never throw at the caller.
            _logger.LogError(
                ex,
                "Unexpected failure closing browser instance '{Instance}' in session '{Session}'.",
                InstanceName,
                SessionId);
        }
        finally
        {
            if (lockAcquired)
            {
                try
                {
                    _lock.Release();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to release the command lock for browser instance '{Instance}'.", InstanceName);
                }
            }
        }
    }

    /// <summary>
    /// Runs one teardown step, bounding how long it may block and swallowing any failure so
    /// the remaining steps still run. Never throws.
    /// </summary>
    private async Task RunTeardownStepAsync(string description, Func<Task> step)
    {
        try
        {
            var task = step();
            if (await Task.WhenAny(task, Task.Delay(DisposeStepTimeout)) != task)
            {
                // Observe any later fault so it does not resurface as an unobserved exception.
                _ = task.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                _logger.LogWarning(
                    "Timed out after {TimeoutSeconds}s trying to {Step} for browser instance '{Instance}' in session '{Session}'.",
                    DisposeStepTimeout.TotalSeconds,
                    description,
                    InstanceName,
                    SessionId);
                return;
            }

            await task;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to {Step} for browser instance '{Instance}' in session '{Session}'.",
                description,
                InstanceName,
                SessionId);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new InvalidOperationException($"Browser instance '{InstanceName}' has been closed.");
        }
    }

    private string? GetEffectiveChannel()
        => Overrides.BrowserType is not null &&
           !string.Equals(Overrides.BrowserType, _options.Browser.BrowserType, StringComparison.OrdinalIgnoreCase)
            ? null
            : _options.Browser.Channel;

    private BrowserNewContextOptions BuildContextOptions(string? storageStatePath)
    {
        var browser = _options.Browser;
        var emu = _emulationOverride;
        var device = ResolveDevice(emu?.DeviceName ?? browser.DeviceName);
        var (width, height) = ResolveViewport(emu, device);

        return new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = browser.IgnoreHttpsErrors,
            Locale = browser.Locale,
            TimezoneId = browser.TimezoneId,
            AcceptDownloads = true,
            RecordVideoDir = null,
            StorageStatePath = storageStatePath,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            UserAgent = emu?.UserAgent ?? device?.UserAgent ?? browser.UserAgent,
            DeviceScaleFactor = emu?.DeviceScaleFactor ?? device?.DeviceScaleFactor ?? browser.DeviceScaleFactor,
            IsMobile = emu?.IsMobile ?? device?.IsMobile ?? browser.IsMobile,
            HasTouch = emu?.HasTouch ?? device?.HasTouch ?? browser.HasTouch,
        };
    }

    private BrowserNewContextOptions? ResolveDevice(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return null;
        }

        if (_playwright is not null && _playwright.Devices.TryGetValue(deviceName, out var device))
        {
            return device;
        }

        _logger.LogWarning("Unknown Playwright device '{Device}'. Use browser_list_devices to see valid names.", deviceName);
        return null;
    }

    private (int Width, int Height) ResolveViewport(EmulationSettings? emu, BrowserNewContextOptions? device)
    {
        var orientation = emu?.Orientation ?? _options.Browser.ViewportOrientation;

        int baseWidth;
        int baseHeight;
        if (emu?.ViewportWidth is { } explicitWidth && emu?.ViewportHeight is { } explicitHeight)
        {
            baseWidth = explicitWidth;
            baseHeight = explicitHeight;
        }
        else if (device?.ViewportSize is { } deviceViewport)
        {
            baseWidth = deviceViewport.Width;
            baseHeight = deviceViewport.Height;
        }
        else
        {
            (baseWidth, baseHeight) = ResolveConfigViewport();
        }

        return ViewportPresets.TryApplyOrientation(baseWidth, baseHeight, orientation, out var w, out var h, out _)
            ? (w, h)
            : (baseWidth, baseHeight);
    }

    private (int Width, int Height) ResolveConfigViewport()
    {
        var browser = _options.Browser;
        if (!string.IsNullOrWhiteSpace(browser.ViewportPreset))
        {
            if (ViewportPresets.TryResolve(browser.ViewportPreset, null, out var width, out var height, out var error))
            {
                return (width, height);
            }

            _logger.LogWarning("Ignoring invalid default viewport preset configuration: {Error}", error);
        }

        return (browser.ViewportWidth, browser.ViewportHeight);
    }

    private async Task<IBrowser> LaunchBrowserAsync(CancellationToken cancellationToken)
    {
        BrowserTypeLaunchOptions launchOptions = new()
        {
            Channel = GetEffectiveChannel(),
            Headless = EffectiveHeadless,
            SlowMo = _options.Browser.SlowMoMs,
            // Keeps accepted downloads in this instance's own folder rather than the
            // shared Playwright temp directory. Downloads are a browser-level setting,
            // so this must be applied here and not on the context options.
            DownloadsPath = DownloadsDirectory,
        };

        return EffectiveBrowserType.ToLowerInvariant() switch
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

/// <summary>
/// Per-instance launch overrides supplied at browser_instance_create time. Null values
/// fall back to the server's configured browser defaults.
/// </summary>
public sealed record BrowserInstanceOverrides(
    string? BrowserType = null,
    bool? Headless = null,
    string? InitialStorageStatePath = null);

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
