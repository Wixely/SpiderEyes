using System.Text.RegularExpressions;

namespace PlaywrightMCPSharp.Server.Services;

/// <summary>
/// Validates and normalizes named browser instance identifiers. Names are routing
/// identifiers scoped to an MCP session, not authorization credentials.
/// </summary>
public static class BrowserInstanceName
{
    public const string Default = "default";

    private static readonly Regex Pattern = new("^[a-z0-9][a-z0-9._-]{0,63}$", RegexOptions.Compiled);

    /// <summary>Normalizes a required instance name, throwing when it is missing or invalid.</summary>
    public static string Normalize(string? instanceName)
    {
        var trimmed = (instanceName ?? string.Empty).Trim().ToLowerInvariant();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Instance name is required.", nameof(instanceName));
        }

        if (!Pattern.IsMatch(trimmed))
        {
            throw new ArgumentException(
                $"Invalid instance name '{trimmed}'. Names must start with a letter or digit, use only lowercase letters, digits, '.', '_', or '-', and be at most 64 characters.",
                nameof(instanceName));
        }

        return trimmed;
    }

    /// <summary>Normalizes an optional instance name, mapping null/blank to the default instance.</summary>
    public static string NormalizeOrDefault(string? instanceName)
        => string.IsNullOrWhiteSpace(instanceName) ? Default : Normalize(instanceName);
}
