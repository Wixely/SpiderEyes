using Microsoft.Extensions.Options;
using SpiderEyes.Server.Configuration;
using SpiderEyes.Server.Hosting;
using SpiderEyes.Server.Services;
using SpiderEyes.Server.Tools;

var transportOverride = GetTransportOverride(args);
var filteredArgs = FilterTransportAliases(args);
var bootstrapConfiguration = BuildBootstrapConfiguration(filteredArgs);
var bootstrapOptions = BindOptions(bootstrapConfiguration);
var transport = transportOverride ?? bootstrapOptions.Server.Transport;

if (transport == SpiderEyesTransportMode.Stdio)
{
    await RunStdioAsync(filteredArgs, transportOverride);
}
else
{
    await RunHttpAsync(filteredArgs, transportOverride);
}

return;

static async Task RunHttpAsync(string[] args, SpiderEyesTransportMode? transportOverride)
{
    var builder = WebApplication.CreateBuilder(args);
    ApplyTransportOverride(builder.Configuration, transportOverride);
    ConfigureOptions(builder.Services, builder.Configuration);

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddProblemDetails();
    ConfigureCoreServices(builder.Services);

    var bootstrapOptions = BindOptions(builder.Configuration);
    var bindHost = string.IsNullOrWhiteSpace(bootstrapOptions.Server.Host) ? "127.0.0.1" : bootstrapOptions.Server.Host;
    var bindPort = bootstrapOptions.Server.Port <= 0 ? 8931 : bootstrapOptions.Server.Port;
    builder.WebHost.UseUrls($"http://{bindHost}:{bindPort}");

    ConfigureToolCatalog(
        builder.Services
            .AddMcpServer()
            .WithHttpTransport(options =>
            {
                options.Stateless = false;
                options.IdleTimeout = bootstrapOptions.Session.IdleTimeout;
                options.PerSessionExecutionContext = false;
            }),
        bootstrapOptions);

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
        transport = options.Server.Transport.ToString(),
        route = options.Server.Route,
        securityMode = options.Security.Mode.ToString(),
        timeUtc = DateTimeOffset.UtcNow,
    }));

    app.MapMcp(options.Server.Route);

    await app.RunAsync();
}

static async Task RunStdioAsync(string[] args, SpiderEyesTransportMode? transportOverride)
{
    var builder = Host.CreateApplicationBuilder(args);
    ApplyTransportOverride(builder.Configuration, transportOverride);
    ConfigureOptions(builder.Services, builder.Configuration);

    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(consoleLogOptions =>
    {
        consoleLogOptions.LogToStandardErrorThreshold = LogLevel.Trace;
    });

    ConfigureCoreServices(builder.Services);
    ConfigureToolCatalog(
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport(),
        BindOptions(builder.Configuration));

    var host = builder.Build();
    var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("SpiderEyes.Startup");
    logger.LogInformation("SpiderEyes is running over stdio transport.");

    await host.RunAsync();
}

static void ConfigureOptions(IServiceCollection services, IConfiguration configuration)
{
    services
        .AddOptions<SpiderEyesOptions>()
        .Bind(configuration.GetSection(SpiderEyesOptions.SectionName))
        .ValidateOnStart();
    services.AddSingleton<IValidateOptions<SpiderEyesOptions>, SpiderEyesOptionsValidator>();
}

static void ConfigureCoreServices(IServiceCollection services)
{
    services.AddSingleton<BrowserSessionManager>();
    services.AddHostedService(static serviceProvider => serviceProvider.GetRequiredService<BrowserSessionManager>());
    services.AddSingleton<FileAccessService>();
    services.AddSingleton<PlaywrightRuntimeService>();
    services.AddSingleton<RunCodeService>();
    services.AddSingleton<BrowserToolExecutor>();
}

static void ConfigureToolCatalog(IMcpServerBuilder builder, SpiderEyesOptions options)
{
    if (options.Features.ClaudeCompatibleToolCatalog)
    {
        builder.WithTools<ClaudeCompatibleBrowserTools>();
        return;
    }

    builder
        .WithTools<CoreBrowserTools>()
        .WithTools<NetworkStorageBrowserTools>()
        .WithTools<DevtoolsBrowserTools>();
}

static IConfigurationRoot BuildBootstrapConfiguration(string[] args)
{
    var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
        ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

    var builder = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

    if (!string.IsNullOrWhiteSpace(environmentName))
    {
        builder.AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: false);
    }

    builder.AddEnvironmentVariables();
    if (args.Length > 0)
    {
        builder.AddCommandLine(args);
    }

    return builder.Build();
}

static SpiderEyesOptions BindOptions(IConfiguration configuration)
{
    var options = new SpiderEyesOptions();
    configuration.GetSection(SpiderEyesOptions.SectionName).Bind(options);
    return options;
}

static void ApplyTransportOverride(ConfigurationManager configuration, SpiderEyesTransportMode? transportOverride)
{
    if (transportOverride is null)
    {
        return;
    }

    configuration.AddInMemoryCollection(
    [
        new KeyValuePair<string, string?>($"{SpiderEyesOptions.SectionName}:Server:Transport", transportOverride.ToString()),
    ]);
}

static SpiderEyesTransportMode? GetTransportOverride(IEnumerable<string> args)
{
    SpiderEyesTransportMode? transport = null;
    foreach (var arg in args)
    {
        if (string.Equals(arg, "--stdio", StringComparison.OrdinalIgnoreCase))
        {
            transport = SpiderEyesTransportMode.Stdio;
        }
        else if (string.Equals(arg, "--http", StringComparison.OrdinalIgnoreCase))
        {
            transport = SpiderEyesTransportMode.Http;
        }
    }

    return transport;
}

static string[] FilterTransportAliases(IEnumerable<string> args)
    => args.Where(arg =>
        !string.Equals(arg, "--stdio", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(arg, "--http", StringComparison.OrdinalIgnoreCase))
        .ToArray();

public partial class Program;
