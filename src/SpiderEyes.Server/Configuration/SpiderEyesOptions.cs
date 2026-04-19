using System.ComponentModel.DataAnnotations;

namespace SpiderEyes.Server.Configuration;

public sealed class SpiderEyesOptions
{
    public const string SectionName = "SpiderEyes";

    public ServerOptions Server { get; set; } = new();

    public SecurityOptions Security { get; set; } = new();

    public BrowserOptions Browser { get; set; } = new();

    public SessionOptions Session { get; set; } = new();

    public FeatureOptions Features { get; set; } = new();
}

public sealed class ServerOptions
{
    [Required]
    public string Host { get; set; } = "127.0.0.1";

    [Range(1, 65535)]
    public int Port { get; set; } = 8931;

    [Required]
    public string Route { get; set; } = "/mcp";

    public List<string> AllowedHosts { get; set; } = ["127.0.0.1", "localhost"];

    public List<string> AllowedOrigins { get; set; } = [];
}

public sealed class SecurityOptions
{
    public SpiderEyesSecurityMode Mode { get; set; } = SpiderEyesSecurityMode.LocalOnly;

    public string? BearerToken { get; set; }

    public bool DangerousAllowRemoteNoAuth { get; set; }
}

public enum SpiderEyesSecurityMode
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

    public string Locale { get; set; } = "en-GB";

    public string TimezoneId { get; set; } = "Europe/London";

    public string? DeviceName { get; set; }

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
}
