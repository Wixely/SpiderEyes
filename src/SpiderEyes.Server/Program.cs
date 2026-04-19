using Microsoft.Extensions.Options;
using SpiderEyes.Server.Configuration;
using SpiderEyes.Server.Hosting;
using SpiderEyes.Server.Services;
using SpiderEyes.Server.Tools;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpContextAccessor();
builder.Services.AddProblemDetails();

builder.Services
    .AddOptions<SpiderEyesOptions>()
    .Bind(builder.Configuration.GetSection(SpiderEyesOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<SpiderEyesOptions>, SpiderEyesOptionsValidator>();

var bootstrapOptions = new SpiderEyesOptions();
builder.Configuration.GetSection(SpiderEyesOptions.SectionName).Bind(bootstrapOptions);
var bindHost = string.IsNullOrWhiteSpace(bootstrapOptions.Server.Host) ? "127.0.0.1" : bootstrapOptions.Server.Host;
var bindPort = bootstrapOptions.Server.Port <= 0 ? 8931 : bootstrapOptions.Server.Port;
builder.WebHost.UseUrls($"http://{bindHost}:{bindPort}");

builder.Services.AddSingleton<BrowserSessionManager>();
builder.Services.AddHostedService(static serviceProvider => serviceProvider.GetRequiredService<BrowserSessionManager>());
builder.Services.AddSingleton<FileAccessService>();
builder.Services.AddSingleton<PlaywrightRuntimeService>();
builder.Services.AddSingleton<RunCodeService>();
builder.Services.AddSingleton<BrowserToolExecutor>();

builder.Services
    .AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.Stateless = false;
        options.IdleTimeout = bootstrapOptions.Session.IdleTimeout;
        options.PerSessionExecutionContext = false;
    })
    .WithTools<CoreBrowserTools>()
    .WithTools<NetworkStorageBrowserTools>()
    .WithTools<DevtoolsBrowserTools>();

var app = builder.Build();

var options = app.Services.GetRequiredService<IOptions<SpiderEyesOptions>>().Value;
if (options.Security.Mode == SpiderEyesSecurityMode.RemoteNoAuth)
{
    app.Logger.LogWarning("SpiderEyes is running in RemoteNoAuth mode. The MCP endpoint is exposed without authentication.");
}

app.UseMiddleware<McpSecurityMiddleware>();

app.MapGet("/healthz", () => Results.Ok(new
{
    status = "ok",
    route = options.Server.Route,
    securityMode = options.Security.Mode.ToString(),
    timeUtc = DateTimeOffset.UtcNow,
}));

app.MapMcp(options.Server.Route);

app.Run();

public partial class Program;
