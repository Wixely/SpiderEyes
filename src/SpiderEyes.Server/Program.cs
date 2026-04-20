using ModelContextProtocol.Server;
using Microsoft.Extensions.Options;
using SpiderEyes.Server.Configuration;
using SpiderEyes.Server.Hosting;
using SpiderEyes.Server.Services;
using SpiderEyes.Server.Tools;

const string ServiceName = "SpiderEyes";

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
    builder.Host.UseWindowsService(options =>
    {
        options.ServiceName = ServiceName;
    });
    ApplyTransportOverride(builder.Configuration, transportOverride);
    ConfigureOptions(builder.Services, builder.Configuration);

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddProblemDetails();
    ConfigureCoreServices(builder.Services);

    var bootstrapOptions = BindOptions(builder.Configuration);
    var bindHost = string.IsNullOrWhiteSpace(bootstrapOptions.Server.Host) ? "127.0.0.1" : bootstrapOptions.Server.Host;
    var bindPort = bootstrapOptions.Server.Port <= 0 ? 8931 : bootstrapOptions.Server.Port;
    builder.WebHost.UseUrls($"http://{bindHost}:{bindPort}");
    SetInteractiveConsoleTitle(bindPort);

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
    WriteHttpStartupDiagnostics(options, bindHost, bindPort);
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

static void SetInteractiveConsoleTitle(int port)
{
    if (!Environment.UserInteractive)
    {
        return;
    }

    Console.Title = $"{ServiceName} : {port}";
}

static void WriteHttpStartupDiagnostics(SpiderEyesOptions options, string bindHost, int bindPort)
{
    var toolNames = GetEnabledToolNames(options);
    var publiclyReachable = IsPublicBinding(bindHost, options.Security.Mode);

    Console.WriteLine($"{ServiceName} HTTP startup");
    Console.WriteLine($"  Endpoint: http://{bindHost}:{bindPort}{options.Server.Route}");
    Console.WriteLine($"  Binding: {(publiclyReachable ? "public/network" : "local-only")} ({bindHost}:{bindPort})");
    Console.WriteLine($"  Security: {options.Security.Mode}");
    Console.WriteLine($"  Claude compatibility mode: {options.Features.ClaudeCompatibleToolCatalog}");
    Console.WriteLine($"  Run code enabled: {options.Features.EnableRunCode}");
    Console.WriteLine($"  Unrestricted file access: {options.Features.AllowUnrestrictedFileAccess}");
    Console.WriteLine($"  Browser: {options.Browser.BrowserType}, headless={options.Browser.Headless}, channel={options.Browser.Channel ?? "(default)"}");
    Console.WriteLine($"  Session idle timeout: {options.Session.IdleTimeout}");
    Console.WriteLine($"  Tool count: {toolNames.Count}");
    foreach (var toolName in toolNames)
    {
        Console.WriteLine($"    - {toolName}");
    }
}

static List<string> GetEnabledToolNames(SpiderEyesOptions options)
{
    var toolTypes = options.Features.ClaudeCompatibleToolCatalog
        ? [typeof(ClaudeCompatibleBrowserTools)]
        : new[] { typeof(CoreBrowserTools), typeof(NetworkStorageBrowserTools), typeof(DevtoolsBrowserTools) };

    return toolTypes
        .SelectMany(static toolType => toolType
            .GetMethods()
            .Select(method => method.GetCustomAttributes(typeof(McpServerToolAttribute), inherit: false)
                .OfType<McpServerToolAttribute>()
                .Select(attribute => attribute.Name))
            .SelectMany(static names => names))
        .Where(static name => !string.IsNullOrWhiteSpace(name))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
        .ToList()!;
}

static bool IsPublicBinding(string bindHost, SpiderEyesSecurityMode securityMode)
{
    if (securityMode == SpiderEyesSecurityMode.LocalOnly)
    {
        return false;
    }

    if (string.IsNullOrWhiteSpace(bindHost))
    {
        return false;
    }

    if (string.Equals(bindHost, "localhost", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    if (string.Equals(bindHost, "0.0.0.0", StringComparison.OrdinalIgnoreCase)
        || string.Equals(bindHost, "[::]", StringComparison.OrdinalIgnoreCase)
        || string.Equals(bindHost, "::", StringComparison.OrdinalIgnoreCase)
        || string.Equals(bindHost, "+", StringComparison.OrdinalIgnoreCase)
        || string.Equals(bindHost, "*", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    if (Uri.CheckHostName(bindHost) == UriHostNameType.Dns)
    {
        return true;
    }

    if (!System.Net.IPAddress.TryParse(bindHost, out var ipAddress))
    {
        return false;
    }

    return !System.Net.IPAddress.IsLoopback(ipAddress);
}

public partial class Program;
