namespace PlaywrightMCPSharp.Server.Services;

/// <summary>
/// A per-session device emulation override requested by an MCP client. Any null
/// field falls back to the matching Playwright device descriptor (when a
/// <see cref="DeviceName"/> is set) and then to the server's configured defaults.
/// </summary>
public sealed record EmulationSettings
{
    /// <summary>Playwright device descriptor name, e.g. "iPhone 13" or "Pixel 7".</summary>
    public string? DeviceName { get; init; }

    /// <summary>Explicit viewport width (natural orientation) overriding any device/preset value.</summary>
    public int? ViewportWidth { get; init; }

    /// <summary>Explicit viewport height (natural orientation) overriding any device/preset value.</summary>
    public int? ViewportHeight { get; init; }

    /// <summary>"landscape" or "portrait"; re-orders the resolved viewport.</summary>
    public string? Orientation { get; init; }

    public string? UserAgent { get; init; }

    public float? DeviceScaleFactor { get; init; }

    public bool? IsMobile { get; init; }

    public bool? HasTouch { get; init; }
}
