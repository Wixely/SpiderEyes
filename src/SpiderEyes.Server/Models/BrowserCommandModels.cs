namespace SpiderEyes.Server.Models;

public sealed class BrowserCommandResult
{
    public required string Tool { get; init; }

    public required string SessionId { get; init; }

    public bool Success { get; init; } = true;

    public string? Message { get; init; }

    public PageState? Page { get; init; }

    public object? Data { get; init; }

    public IReadOnlyList<ArtifactInfo> Artifacts { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class PageState
{
    public required string TabId { get; init; }

    public required string Url { get; init; }

    public string? Title { get; init; }

    public string? Snapshot { get; init; }

    public string Summary { get; init; } = string.Empty;

    public IReadOnlyList<TabInfo> Tabs { get; init; } = [];
}

public sealed class TabInfo
{
    public required string TabId { get; init; }

    public required string Url { get; init; }

    public string? Title { get; init; }

    public bool IsCurrent { get; init; }
}

public sealed class ArtifactInfo
{
    public required string Name { get; init; }

    public required string Path { get; init; }

    public string? MimeType { get; init; }
}

public sealed class ConsoleEntry
{
    public required string Type { get; init; }

    public required string Text { get; init; }

    public string? Location { get; init; }

    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class NetworkEntry
{
    public required string Method { get; init; }

    public required string Url { get; init; }

    public int? Status { get; init; }

    public string? ResourceType { get; init; }

    public bool FromRoute { get; init; }

    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class DialogRecord
{
    public required string Type { get; init; }

    public required string Message { get; init; }

    public string? DefaultValue { get; init; }

    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class RouteRuleInfo
{
    public required string RuleId { get; init; }

    public required string Pattern { get; init; }

    public required string Action { get; init; }

    public int? Status { get; init; }

    public string? Body { get; init; }

    public string? ContentType { get; init; }
}

public sealed class FormFieldInput
{
    public required string Target { get; init; }

    public string? Value { get; init; }

    public string? Kind { get; init; }

    public IReadOnlyList<string>? Values { get; init; }
}

public sealed class CookieMutation
{
    public required string Name { get; init; }

    public required string Value { get; init; }

    public string? Url { get; init; }

    public string? Domain { get; init; }

    public string? Path { get; init; }

    public float? ExpiresUnixSeconds { get; init; }

    public bool? HttpOnly { get; init; }

    public bool? Secure { get; init; }

    public string? SameSite { get; init; }
}

public sealed class RouteMutation
{
    public required string Pattern { get; init; }

    public required string Action { get; init; }

    public int? Status { get; init; }

    public string? Body { get; init; }

    public string? ContentType { get; init; }

    public Dictionary<string, string>? Headers { get; init; }

    public string? AbortErrorCode { get; init; }
}

public sealed record BrowserActionPayload(
    object? Data = null,
    string? Message = null,
    string? TabId = null,
    IReadOnlyList<ArtifactInfo>? Artifacts = null,
    IReadOnlyList<string>? Warnings = null);

public enum PageCaptureMode
{
    None = 0,
    Summary = 1,
    Snapshot = 2,
}
