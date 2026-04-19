using System.Net;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using SpiderEyes.Server.Configuration;

namespace SpiderEyes.Server.Hosting;

public sealed class McpSecurityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IOptionsMonitor<SpiderEyesOptions> _optionsMonitor;
    private readonly ILogger<McpSecurityMiddleware> _logger;

    public McpSecurityMiddleware(
        RequestDelegate next,
        IOptionsMonitor<SpiderEyesOptions> optionsMonitor,
        ILogger<McpSecurityMiddleware> logger)
    {
        _next = next;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var options = _optionsMonitor.CurrentValue;
        if (!context.Request.Path.StartsWithSegments(options.Server.Route, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (!IsAllowedHost(context, options))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Host header rejected.");
            return;
        }

        if (!IsAllowedOrigin(context, options))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Origin rejected.");
            return;
        }

        switch (options.Security.Mode)
        {
            case SpiderEyesSecurityMode.LocalOnly:
                if (!IsLoopback(context.Connection.RemoteIpAddress))
                {
                    _logger.LogWarning("Rejected non-local MCP request from {RemoteIpAddress}.", context.Connection.RemoteIpAddress);
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsync("Only loopback access is allowed.");
                    return;
                }

                break;

            case SpiderEyesSecurityMode.RemoteBearer:
                var header = context.Request.Headers.Authorization.ToString();
                if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(header["Bearer ".Length..], options.Security.BearerToken, StringComparison.Ordinal))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.Headers.WWWAuthenticate = "Bearer";
                    await context.Response.WriteAsync("Bearer token required.");
                    return;
                }

                break;
        }

        await _next(context);
    }

    private static bool IsAllowedHost(HttpContext context, SpiderEyesOptions options)
    {
        var host = context.Request.Host.Host;
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        if (options.Server.AllowedHosts.Count == 0 || options.Server.AllowedHosts.Contains("*"))
        {
            return true;
        }

        return options.Server.AllowedHosts.Any(allowed => string.Equals(allowed, host, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAllowedOrigin(HttpContext context, SpiderEyesOptions options)
    {
        if (!context.Request.Headers.TryGetValue("Origin", out var originHeader) || StringValues.IsNullOrEmpty(originHeader))
        {
            return true;
        }

        if (options.Server.AllowedOrigins.Count == 0)
        {
            return options.Security.Mode != SpiderEyesSecurityMode.LocalOnly || IsLocalOrigin(originHeader.ToString());
        }

        return options.Server.AllowedOrigins.Any(allowed => string.Equals(allowed, originHeader.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLocalOrigin(string origin)
    {
        return origin.StartsWith("http://127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || origin.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase)
            || origin.StartsWith("https://127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || origin.StartsWith("https://localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLoopback(IPAddress? address)
    {
        return address is not null && IPAddress.IsLoopback(address);
    }
}
