using System.ComponentModel.DataAnnotations;

namespace PlaywrightMCPSharp.Server.Configuration;

public sealed class PlaywrightMCPSharpOptions
{
    public const string SectionName = "PlaywrightMCPSharp";

    public ServerOptions Server { get; set; } = new();

    public SecurityOptions Security { get; set; } = new();

    public BrowserOptions Browser { get; set; } = new();

    public SessionOptions Session { get; set; } = new();

    public FeatureOptions Features { get; set; } = new();
}

public sealed class ServerOptions
{
    public PlaywrightMCPSharpTransportMode Transport { get; set; } = PlaywrightMCPSharpTransportMode.Http;

    [Required]
    public string Host { get; set; } = "127.0.0.1";

    [Range(1, 65535)]
    public int Port { get; set; } = 5704;

    [Required]
    public string Route { get; set; } = "/mcp";

    public string Password { get; set; } = string.Empty;

    public List<string> AllowedHosts { get; set; } = ["127.0.0.1", "localhost"];

    public List<string> AllowedOrigins { get; set; } = [];
}

public enum PlaywrightMCPSharpTransportMode
{
    Http = 0,
    Stdio = 1,
}

public sealed class SecurityOptions
{
    public PlaywrightMCPSharpSecurityMode Mode { get; set; } = PlaywrightMCPSharpSecurityMode.LocalOnly;

    public string? BearerToken { get; set; }

    public bool DangerousAllowRemoteNoAuth { get; set; }
}

public enum PlaywrightMCPSharpSecurityMode
{
    LocalOnly = 0,
    RemoteNoAuth = 1,
    RemoteBearer = 2,
}

public sealed class BrowserOptions
{
    public string BrowserType { get; set; } = "chromium";

    public string? Channel { get; set; }

    public bool Headless { get; set; }

    [Range(0, 10_000)]
    public int SlowMoMs { get; set; }

    [Range(100, 10_000)]
    public int ViewportWidth { get; set; } = 1440;

    [Range(100, 10_000)]
    public int ViewportHeight { get; set; } = 900;

    /// <summary>
    /// Optional default viewport preset name (e.g. "fhd", "ipad-pro-11"). When set it
    /// takes precedence over <see cref="ViewportWidth"/>/<see cref="ViewportHeight"/>.
    /// An MCP client can still override the live viewport with browser_resize.
    /// </summary>
    public string? ViewportPreset { get; set; }

    /// <summary>
    /// Optional orientation ("landscape" or "portrait") applied to the default viewport.
    /// </summary>
    public string? ViewportOrientation { get; set; }

    public string Locale { get; set; } = "en-GB";

    public string TimezoneId { get; set; } = "Europe/London";

    /// <summary>
    /// Default Playwright device descriptor name (e.g. "iPhone 13"). When set, the
    /// device's viewport, user agent, scale factor, touch, and mobile flags are used
    /// as defaults. An MCP client can override these live with browser_emulate_device.
    /// </summary>
    public string? DeviceName { get; set; }

    /// <summary>Default user agent override. Null uses the browser/device default.</summary>
    public string? UserAgent { get; set; }

    /// <summary>Default device scale factor (DPR). Null uses the browser/device default.</summary>
    public float? DeviceScaleFactor { get; set; }

    /// <summary>Default mobile emulation flag. Null uses the browser/device default.</summary>
    public bool? IsMobile { get; set; }

    /// <summary>Default touch support flag. Null uses the browser/device default.</summary>
    public bool? HasTouch { get; set; }

    public string DownloadsPath { get; set; } = "artifacts/downloads";

    public bool IgnoreHttpsErrors { get; set; }
}

public sealed class SessionOptions
{
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(30);

    [Range(1, 100)]
    public int MaxTabs { get; set; } = 8;

    public string ArtifactRoot { get; set; } = "artifacts";

    public int MaxConsoleEntries { get; set; } = 200;

    public int MaxNetworkEntries { get; set; } = 500;
}

public sealed class FeatureOptions
{
    public bool Core { get; set; } = true;

    public bool Network { get; set; } = true;

    public bool Storage { get; set; } = true;

    public bool Devtools { get; set; } = true;

    public bool Testing { get; set; } = true;

    public bool Vision { get; set; } = true;

    public bool EnableRunCode { get; set; } = true;

    public bool AllowUnrestrictedFileAccess { get; set; }

    public bool ClaudeCompatibleToolCatalog { get; set; }
}
