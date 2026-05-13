using ModelContextProtocol.Server;
using Microsoft.Extensions.Options;
using PlaywrightMCPSharp.Server.Configuration;
using PlaywrightMCPSharp.Server.Hosting;
using PlaywrightMCPSharp.Server.Services;
using PlaywrightMCPSharp.Server.Tools;
using Serilog;
using Serilog.Events;

const string ServiceName = "PlaywrightMCPSharp";
const string EnvironmentPrefix = "PLAYWRIGHTMCP_";
var contentRoot = AppContext.BaseDirectory;
McpSharpIcon.ApplyConsoleWindowIcon();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose)
    .WriteTo.File(
        Path.Combine(contentRoot, "logs", "playwrightmcp-bootstrap-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        shared: true)
    .CreateBootstrapLogger();

try
{
    AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception in AppDomain");
    TaskScheduler.UnobservedTaskException += (_, e) =>
    {
        Log.Error(e.Exception, "Unobserved task exception");
        e.SetObserved();
    };

    var transportOverride = GetTransportOverride(args);
    var filteredArgs = FilterTransportAliases(args);
    var bootstrapConfiguration = BuildBootstrapConfiguration(filteredArgs);
    var bootstrapOptions = BindOptions(bootstrapConfiguration);
    var transport = transportOverride ?? bootstrapOptions.Server.Transport;

    if (transport == PlaywrightMCPSharpTransportMode.Stdio)
    {
        await RunStdioAsync(filteredArgs, transportOverride);
    }
    else
    {
        await RunHttpAsync(filteredArgs, transportOverride);
    }
}
catch (Exception ex)
{
    Log.Fatal(ex, "Server terminated unexpectedly");
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

return;

static async Task RunHttpAsync(string[] args, PlaywrightMCPSharpTransportMode? transportOverride)
{
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory,
    });
    ConfigurePlaywrightConfiguration(builder.Configuration, args);
    builder.Host.UseWindowsService(options =>
    {
        options.ServiceName = ServiceName;
    });
    builder.Host.UseSerilog((ctx, services, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());
    ApplyTransportOverride(builder.Configuration, transportOverride);
    ConfigureOptions(builder.Services, builder.Configuration);

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddProblemDetails();
    ConfigureCoreServices(builder.Services);

    var bootstrapOptions = BindOptions(builder.Configuration);
    var bindHost = string.IsNullOrWhiteSpace(bootstrapOptions.Server.Host) ? "127.0.0.1" : bootstrapOptions.Server.Host;
    var bindPort = bootstrapOptions.Server.Port <= 0 ? 5704 : bootstrapOptions.Server.Port;
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

    var options = app.Services.GetRequiredService<IOptions<PlaywrightMCPSharpOptions>>().Value;
    WriteHttpStartupDiagnostics(options, bindHost, bindPort);
    if (options.Security.Mode == PlaywrightMCPSharpSecurityMode.RemoteNoAuth)
    {
        app.Logger.LogWarning("PlaywrightMCPSharp is running in RemoteNoAuth mode. The MCP endpoint is exposed without authentication.");
    }

    app.UseMiddleware<McpSecurityMiddleware>();

    app.MapFavicon();
    app.MapGet("/healthz", () => Results.Ok(new
    {
        status = "ok",
        server = ServiceName,
        transport = options.Server.Transport.ToString(),
        path = options.Server.Route,
        route = options.Server.Route,
        securityMode = options.Security.Mode.ToString(),
        timeUtc = DateTimeOffset.UtcNow,
    }));

    app.MapMcp(options.Server.Route);

    await app.RunAsync();
}

static async Task RunStdioAsync(string[] args, PlaywrightMCPSharpTransportMode? transportOverride)
{
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory,
    });
    ConfigurePlaywrightConfiguration(builder.Configuration, args);
    ApplyTransportOverride(builder.Configuration, transportOverride);
    ConfigureOptions(builder.Services, builder.Configuration);

    Log.Logger = CreateStdioLogger();
    builder.Logging.ClearProviders();
    builder.Logging.AddSerilog(Log.Logger, dispose: false);

    ConfigureCoreServices(builder.Services);
    ConfigureToolCatalog(
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport(),
        BindOptions(builder.Configuration));

    var host = builder.Build();
    var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("PlaywrightMCPSharp.Startup");
    logger.LogInformation("PlaywrightMCPSharp is running over stdio transport.");

    await host.RunAsync();
}

static void ConfigurePlaywrightConfiguration(ConfigurationManager configuration, string[] args)
{
    var environmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
        ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

    configuration.Sources.Clear();
    configuration
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

    if (!string.IsNullOrWhiteSpace(environmentName))
    {
        configuration.AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: true);
    }

    configuration
        .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables()
        .AddEnvironmentVariables(prefix: EnvironmentPrefix);

    if (args.Length > 0)
    {
        configuration.AddCommandLine(args);
    }
}

static void ConfigureOptions(IServiceCollection services, IConfiguration configuration)
{
    services
        .AddOptions<PlaywrightMCPSharpOptions>()
        .Bind(configuration.GetSection(PlaywrightMCPSharpOptions.SectionName))
        .ValidateOnStart();
    services.AddSingleton<IValidateOptions<PlaywrightMCPSharpOptions>, PlaywrightMCPSharpOptionsValidator>();
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

static void ConfigureToolCatalog(IMcpServerBuilder builder, PlaywrightMCPSharpOptions options)
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

    builder.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
    builder.AddEnvironmentVariables();
    builder.AddEnvironmentVariables(prefix: EnvironmentPrefix);
    if (args.Length > 0)
    {
        builder.AddCommandLine(args);
    }

    return builder.Build();
}

static PlaywrightMCPSharpOptions BindOptions(IConfiguration configuration)
{
    var options = new PlaywrightMCPSharpOptions();
    configuration.GetSection(PlaywrightMCPSharpOptions.SectionName).Bind(options);
    return options;
}

static void ApplyTransportOverride(ConfigurationManager configuration, PlaywrightMCPSharpTransportMode? transportOverride)
{
    if (transportOverride is null)
    {
        return;
    }

    configuration.AddInMemoryCollection(
    [
        new KeyValuePair<string, string?>($"{PlaywrightMCPSharpOptions.SectionName}:Server:Transport", transportOverride.ToString()),
    ]);
}

static PlaywrightMCPSharpTransportMode? GetTransportOverride(IEnumerable<string> args)
{
    PlaywrightMCPSharpTransportMode? transport = null;
    foreach (var arg in args)
    {
        if (string.Equals(arg, "--stdio", StringComparison.OrdinalIgnoreCase))
        {
            transport = PlaywrightMCPSharpTransportMode.Stdio;
        }
        else if (string.Equals(arg, "--http", StringComparison.OrdinalIgnoreCase))
        {
            transport = PlaywrightMCPSharpTransportMode.Http;
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

static Serilog.ILogger CreateStdioLogger()
    => new LoggerConfiguration()
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
        .MinimumLevel.Override("System", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithProcessId()
        .Enrich.WithThreadId()
        .WriteTo.Console(
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}",
            standardErrorFromLevel: LogEventLevel.Verbose)
        .WriteTo.File(
            Path.Combine(AppContext.BaseDirectory, "logs", "playwrightmcp-.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            fileSizeLimitBytes: 52428800,
            rollOnFileSizeLimit: true,
            shared: true,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
        .CreateLogger();

static void WriteHttpStartupDiagnostics(PlaywrightMCPSharpOptions options, string bindHost, int bindPort)
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

static List<string> GetEnabledToolNames(PlaywrightMCPSharpOptions options)
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

static bool IsPublicBinding(string bindHost, PlaywrightMCPSharpSecurityMode securityMode)
{
    if (securityMode == PlaywrightMCPSharpSecurityMode.LocalOnly)
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
