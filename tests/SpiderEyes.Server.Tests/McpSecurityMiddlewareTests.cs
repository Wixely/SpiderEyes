using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpiderEyes.Server.Configuration;
using SpiderEyes.Server.Hosting;

namespace SpiderEyes.Server.Tests;

public sealed class McpSecurityMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_Rejects_NonLoopback_InLocalOnlyMode()
    {
        var calledNext = false;
        var middleware = CreateMiddleware(
            new SpiderEyesOptions(),
            _ =>
            {
                calledNext = true;
                return Task.CompletedTask;
            });

        var context = new DefaultHttpContext();
        context.Request.Path = "/mcp";
        context.Request.Host = new HostString("127.0.0.1");
        context.Connection.RemoteIpAddress = IPAddress.Parse("10.1.2.3");

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(calledNext);
    }

    [Fact]
    public async Task InvokeAsync_Requires_BearerToken_InRemoteBearerMode()
    {
        var options = new SpiderEyesOptions
        {
            Security = new SecurityOptions
            {
                Mode = SpiderEyesSecurityMode.RemoteBearer,
                BearerToken = "secret-token",
            },
            Server = new ServerOptions
            {
                Host = "127.0.0.1",
                Port = 8931,
                Route = "/mcp",
                AllowedHosts = ["127.0.0.1"],
            },
        };

        var middleware = CreateMiddleware(options, _ => Task.CompletedTask);
        var context = new DefaultHttpContext();
        context.Request.Path = "/mcp";
        context.Request.Host = new HostString("127.0.0.1");
        context.Connection.RemoteIpAddress = IPAddress.Loopback;

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_Allows_ValidBearerToken()
    {
        var calledNext = false;
        var options = new SpiderEyesOptions
        {
            Security = new SecurityOptions
            {
                Mode = SpiderEyesSecurityMode.RemoteBearer,
                BearerToken = "secret-token",
            },
            Server = new ServerOptions
            {
                Host = "127.0.0.1",
                Port = 8931,
                Route = "/mcp",
                AllowedHosts = ["127.0.0.1"],
            },
        };

        var middleware = CreateMiddleware(
            options,
            _ =>
            {
                calledNext = true;
                return Task.CompletedTask;
            });

        var context = new DefaultHttpContext();
        context.Request.Path = "/mcp";
        context.Request.Host = new HostString("127.0.0.1");
        context.Request.Headers.Authorization = "Bearer secret-token";
        context.Connection.RemoteIpAddress = IPAddress.Loopback;

        await middleware.InvokeAsync(context);

        Assert.True(calledNext);
    }

    private static McpSecurityMiddleware CreateMiddleware(SpiderEyesOptions options, RequestDelegate next)
        => new(
            next,
            new OptionsMonitorStub<SpiderEyesOptions>(options),
            NullLogger<McpSecurityMiddleware>.Instance);

    private sealed class OptionsMonitorStub<T> : IOptionsMonitor<T>
    {
        public OptionsMonitorStub(T currentValue) => CurrentValue = currentValue;

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
