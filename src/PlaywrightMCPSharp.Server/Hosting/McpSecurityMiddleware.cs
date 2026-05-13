using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using PlaywrightMCPSharp.Server.Configuration;

namespace PlaywrightMCPSharp.Server.Hosting;

public sealed class McpSecurityMiddleware
{
    private const string PasswordHeaderName = "X-MCP-Password";

    private readonly RequestDelegate _next;
    private readonly IOptionsMonitor<PlaywrightMCPSharpOptions> _optionsMonitor;
    private readonly ILogger<McpSecurityMiddleware> _logger;

    public McpSecurityMiddleware(
        RequestDelegate next,
        IOptionsMonitor<PlaywrightMCPSharpOptions> optionsMonitor,
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

        if (!string.IsNullOrWhiteSpace(options.Server.Password) && !PasswordMatches(context.Request, options.Server.Password))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer, Basic";
            await context.Response.WriteAsync("MCP password required.");
            return;
        }

        switch (options.Security.Mode)
        {
            case PlaywrightMCPSharpSecurityMode.LocalOnly:
                if (!IsLoopback(context.Connection.RemoteIpAddress))
                {
                    _logger.LogWarning("Rejected non-local MCP request from {RemoteIpAddress}.", context.Connection.RemoteIpAddress);
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsync("Only loopback access is allowed.");
                    return;
                }

                break;

            case PlaywrightMCPSharpSecurityMode.RemoteBearer:
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

    private static bool IsAllowedHost(HttpContext context, PlaywrightMCPSharpOptions options)
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

    private static bool IsAllowedOrigin(HttpContext context, PlaywrightMCPSharpOptions options)
    {
        if (!context.Request.Headers.TryGetValue("Origin", out var originHeader) || StringValues.IsNullOrEmpty(originHeader))
        {
            return true;
        }

        if (options.Server.AllowedOrigins.Count == 0)
        {
            return options.Security.Mode != PlaywrightMCPSharpSecurityMode.LocalOnly || IsLocalOrigin(originHeader.ToString());
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

    private static bool PasswordMatches(HttpRequest request, string expected)
    {
        if (request.Headers.TryGetValue(PasswordHeaderName, out var passwordHeader)
            && string.Equals(passwordHeader.ToString(), expected, StringComparison.Ordinal))
        {
            return true;
        }

        if (!AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out var auth))
        {
            return false;
        }

        if (string.Equals(auth.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(auth.Parameter, expected, StringComparison.Ordinal);
        }

        if (string.Equals(auth.Scheme, "Basic", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(auth.Parameter))
        {
            try
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(auth.Parameter));
                var separator = decoded.IndexOf(':');
                return separator >= 0 && string.Equals(decoded[(separator + 1)..], expected, StringComparison.Ordinal);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        return false;
    }
}
