using Microsoft.Extensions.Options;
using SpiderEyes.Server.Configuration;

namespace SpiderEyes.Server.Services;

public sealed class PlaywrightRuntimeService
{
    private static readonly string[] KnownBrowserPrefixes = ["chromium-", "firefox-", "webkit-", "msedge-", "chrome-"];

    private readonly IOptionsMonitor<SpiderEyesOptions> _optionsMonitor;
    private readonly ILogger<PlaywrightRuntimeService> _logger;
    private readonly SemaphoreSlim _installLock = new(1, 1);

    public PlaywrightRuntimeService(
        IOptionsMonitor<SpiderEyesOptions> optionsMonitor,
        ILogger<PlaywrightRuntimeService> logger)
    {
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public object GetStatus(string? browser = null)
    {
        var configuredBrowser = NormalizeBrowser(browser);
        var browserRoot = GetBrowserRoot();
        var installedBrowsers = GetInstalledBrowsers(browserRoot);
        var hasChannel = !string.IsNullOrWhiteSpace(_optionsMonitor.CurrentValue.Browser.Channel);
        var isInstalled = hasChannel || IsBrowserInstalled(configuredBrowser, installedBrowsers);

        return new
        {
            configuredBrowser,
            configuredChannel = _optionsMonitor.CurrentValue.Browser.Channel,
            browserRoot,
            installedBrowsers,
            isInstalled,
            needsInstall = !isInstalled,
            installCommand = GetSuggestedInstallCommand(configuredBrowser),
            mcpTool = "browser_install_runtime",
        };
    }

    public async Task<object> InstallAsync(string? browser, bool withDependencies, CancellationToken cancellationToken)
    {
        var normalizedBrowser = NormalizeBrowser(browser);
        var args = BuildInstallArgs(normalizedBrowser, withDependencies);

        await _installLock.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Installing Playwright runtime with args: {Args}", string.Join(' ', args));
            var exitCode = await Task.Run(() => Microsoft.Playwright.Program.Main(args), cancellationToken);
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"Playwright install exited with code {exitCode}.");
            }

            var status = GetStatus(normalizedBrowser);
            return new
            {
                installedBrowser = normalizedBrowser,
                withDependencies,
                exitCode,
                status,
            };
        }
        finally
        {
            _installLock.Release();
        }
    }

    public bool IsBrowserInstalled(string browser)
        => IsBrowserInstalled(browser, GetInstalledBrowsers(GetBrowserRoot()));

    public string GetSuggestedInstallCommand(string? browser = null)
    {
        var normalizedBrowser = NormalizeBrowser(browser);
        var scriptName = OperatingSystem.IsWindows() ? "playwright.ps1" : "playwright.sh";
        return $"{scriptName} install {normalizedBrowser}";
    }

    private static bool IsBrowserInstalled(string browser, IReadOnlyList<string> installedBrowsers)
        => installedBrowsers.Any(entry => entry.StartsWith(browser + '-', StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> GetInstalledBrowsers(string browserRoot)
    {
        if (!Directory.Exists(browserRoot))
        {
            return [];
        }

        return Directory
            .EnumerateDirectories(browserRoot)
            .Select(Path.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Where(name => KnownBrowserPrefixes.Any(prefix => name!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Cast<string>()
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string NormalizeBrowser(string? browser)
    {
        var value = string.IsNullOrWhiteSpace(browser)
            ? _optionsMonitor.CurrentValue.Browser.BrowserType
            : browser.Trim();

        return value.ToLowerInvariant() switch
        {
            "configured" => _optionsMonitor.CurrentValue.Browser.BrowserType.ToLowerInvariant(),
            "chromium" => "chromium",
            "firefox" => "firefox",
            "webkit" => "webkit",
            _ => throw new InvalidOperationException($"Unsupported Playwright browser '{value}'. Use chromium, firefox, webkit, or configured."),
        };
    }

    private static string[] BuildInstallArgs(string normalizedBrowser, bool withDependencies)
    {
        var args = new List<string> { "install" };
        if (withDependencies)
        {
            args.Add("--with-deps");
        }

        args.Add(normalizedBrowser);
        return [.. args];
    }

    private static string GetBrowserRoot()
    {
        var overridePath = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath) && !string.Equals(overridePath, "0", StringComparison.Ordinal))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(overridePath));
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ms-playwright");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches", "ms-playwright");
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "ms-playwright");
    }
}
