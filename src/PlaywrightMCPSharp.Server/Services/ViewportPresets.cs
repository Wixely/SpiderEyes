using System.Diagnostics.CodeAnalysis;

namespace PlaywrightMCPSharp.Server.Services;

/// <summary>
/// A named viewport resolution that can be selected by an MCP client instead of
/// supplying raw width/height. Stored dimensions are expressed in the preset's
/// natural orientation (landscape for desktop, portrait for tablet/mobile).
/// </summary>
public sealed record ViewportPreset(
    string Name,
    string Category,
    int Width,
    int Height,
    string Description,
    IReadOnlyList<string> Aliases);

/// <summary>
/// Curated catalog of popular desktop, tablet, and mobile viewport sizes.
/// </summary>
public static class ViewportPresets
{
    public const string Desktop = "Desktop";
    public const string Tablet = "Tablet";
    public const string Mobile = "Mobile";

    public const string Landscape = "landscape";
    public const string Portrait = "portrait";

    // Dimensions are stored in each device's natural orientation:
    // desktop = landscape (width >= height), tablet/mobile = portrait (height >= width).
    public static readonly IReadOnlyList<ViewportPreset> All =
    [
        // Desktop / laptop
        new("hd", Desktop, 1280, 720, "720p / HD", ["720p", "hd-ready"]),
        new("wxga", Desktop, 1366, 768, "Most common laptop screen", ["laptop"]),
        new("hd-plus", Desktop, 1600, 900, "HD+", ["900p"]),
        new("fhd", Desktop, 1920, 1080, "1080p / Full HD", ["1080p", "full-hd", "fullhd"]),
        new("wuxga", Desktop, 1920, 1200, "16:10 Full HD+", []),
        new("qhd", Desktop, 2560, 1440, "1440p / 2K QHD", ["1440p", "2k", "wqhd"]),
        new("uhd-4k", Desktop, 3840, 2160, "4K UHD", ["4k", "uhd", "2160p"]),
        new("macbook-air-13", Desktop, 1440, 900, "MacBook Air 13\" (effective)", ["macbook-air"]),
        new("macbook-pro-16", Desktop, 1728, 1117, "MacBook Pro 16\" (effective)", ["macbook-pro"]),

        // Tablet
        new("ipad-mini", Tablet, 768, 1024, "iPad mini", []),
        new("ipad", Tablet, 810, 1080, "iPad 10.2\"", []),
        new("ipad-air", Tablet, 820, 1180, "iPad Air", []),
        new("ipad-pro-11", Tablet, 834, 1194, "iPad Pro 11\"", []),
        new("ipad-pro-12.9", Tablet, 1024, 1366, "iPad Pro 12.9\"", ["ipad-pro"]),
        new("surface-pro", Tablet, 912, 1368, "Surface Pro", []),
        new("galaxy-tab", Tablet, 800, 1280, "Samsung Galaxy Tab", []),

        // Mobile
        new("android-small", Mobile, 360, 640, "Small Android phone", ["small"]),
        new("galaxy-s22", Mobile, 360, 780, "Samsung Galaxy S22", ["galaxy-s"]),
        new("pixel-7", Mobile, 412, 915, "Google Pixel 7", ["pixel"]),
        new("iphone-se", Mobile, 375, 667, "iPhone SE", []),
        new("iphone-12", Mobile, 390, 844, "iPhone 12/13/14", ["iphone-13", "iphone-14", "iphone"]),
        new("iphone-14-plus", Mobile, 428, 926, "iPhone 14 Plus / Pro Max", []),
        new("iphone-15-pro-max", Mobile, 430, 932, "iPhone 15 Pro Max", ["iphone-pro-max"]),
    ];

    /// <summary>
    /// Resolves a preset by name (or alias, case-insensitive) and applies the
    /// requested orientation. Returns false with a helpful error when the name or
    /// orientation is not recognized.
    /// </summary>
    public static bool TryResolve(
        string name,
        string? orientation,
        out int width,
        out int height,
        [NotNullWhen(false)] out string? error)
    {
        width = 0;
        height = 0;

        if (string.IsNullOrWhiteSpace(name))
        {
            error = "Preset name is required.";
            return false;
        }

        var trimmed = name.Trim();
        var preset = All.FirstOrDefault(p =>
            string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase) ||
            p.Aliases.Any(alias => string.Equals(alias, trimmed, StringComparison.OrdinalIgnoreCase)));

        if (preset is null)
        {
            error = $"Unknown viewport preset '{name}'. Call browser_list_viewport_presets to see available names.";
            return false;
        }

        if (!TryApplyOrientation(preset.Width, preset.Height, orientation, out width, out height, out error))
        {
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Re-orders the given dimensions to match the requested orientation. A null or
    /// empty orientation keeps the dimensions as supplied.
    /// </summary>
    public static bool TryApplyOrientation(
        int width,
        int height,
        string? orientation,
        out int resolvedWidth,
        out int resolvedHeight,
        [NotNullWhen(false)] out string? error)
    {
        var longSide = Math.Max(width, height);
        var shortSide = Math.Min(width, height);

        if (string.IsNullOrWhiteSpace(orientation))
        {
            resolvedWidth = width;
            resolvedHeight = height;
            error = null;
            return true;
        }

        switch (orientation.Trim().ToLowerInvariant())
        {
            case Landscape:
                resolvedWidth = longSide;
                resolvedHeight = shortSide;
                error = null;
                return true;

            case Portrait:
                resolvedWidth = shortSide;
                resolvedHeight = longSide;
                error = null;
                return true;

            default:
                resolvedWidth = 0;
                resolvedHeight = 0;
                error = $"Unknown orientation '{orientation}'. Use '{Landscape}' or '{Portrait}'.";
                return false;
        }
    }
}
