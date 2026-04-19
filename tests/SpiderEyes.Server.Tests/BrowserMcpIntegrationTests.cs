using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace SpiderEyes.Server.Tests;

public sealed class BrowserMcpIntegrationTests : IAsyncLifetime
{
    private TestSiteHost? _site;
    private Process? _serverProcess;
    private McpClient? _client;
    private bool _integrationEnabled;
    private readonly StringBuilder _serverLogs = new();

    public async Task InitializeAsync()
    {
        _integrationEnabled = ArePlaywrightBrowsersInstalled();
        if (!_integrationEnabled)
        {
            return;
        }

        _site = await TestSiteHost.StartAsync();
        var port = GetFreeTcpPort();
        var serverExe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "SpiderEyes.Server", "bin", "Debug", "net8.0", "SpiderEyes.Server.exe"));
        var artifactRoot = Path.Combine(Path.GetTempPath(), "SpiderEyes.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(artifactRoot);

        var startInfo = new ProcessStartInfo(serverExe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.Environment["SpiderEyes__Server__Host"] = "127.0.0.1";
        startInfo.Environment["SpiderEyes__Server__Port"] = port.ToString();
        startInfo.Environment["SpiderEyes__Browser__Headless"] = "true";
        startInfo.Environment["SpiderEyes__Browser__Locale"] = "en-GB";
        startInfo.Environment["SpiderEyes__Browser__TimezoneId"] = "UTC";
        startInfo.Environment["SpiderEyes__Session__ArtifactRoot"] = artifactRoot;
        startInfo.Environment["SpiderEyes__Browser__DownloadsPath"] = Path.Combine(artifactRoot, "downloads");

        _serverProcess = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start SpiderEyes server process.");
        _serverProcess.OutputDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                lock (_serverLogs)
                {
                    _serverLogs.AppendLine(eventArgs.Data);
                }
            }
        };
        _serverProcess.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                lock (_serverLogs)
                {
                    _serverLogs.AppendLine(eventArgs.Data);
                }
            }
        };
        _serverProcess.BeginOutputReadLine();
        _serverProcess.BeginErrorReadLine();
        await WaitForServerAsync(new Uri($"http://127.0.0.1:{port}/healthz"));

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"http://127.0.0.1:{port}/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            ConnectionTimeout = TimeSpan.FromSeconds(10),
        });

        var workspaceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        _client = await McpClient.CreateAsync(
            transport,
            new McpClientOptions
            {
                Handlers = new McpClientHandlers
                {
                    RootsHandler = (_, _) => ValueTask.FromResult(new ListRootsResult
                    {
                        Roots =
                        [
                            new Root
                            {
                                Name = "workspace",
                                Uri = new Uri(workspaceRoot).AbsoluteUri,
                            },
                        ],
                    }),
                },
            });
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        if (_serverProcess is not null && !_serverProcess.HasExited)
        {
            _serverProcess.Kill(entireProcessTree: true);
            await _serverProcess.WaitForExitAsync();
        }

        if (_site is not null)
        {
            await _site.DisposeAsync();
        }
    }

    [Fact]
    public async Task McpServer_ListsTools_AndCanNavigateAndClickByRef()
    {
        if (!_integrationEnabled)
        {
            return;
        }

        Assert.NotNull(_client);
        Assert.NotNull(_site);

        var tools = await _client!.ListToolsAsync();
        Assert.Contains(tools, tool => tool.Name == "browser_navigate");
        Assert.Contains(tools, tool => tool.Name == "browser_snapshot");
        Assert.Contains(tools, tool => tool.Name == "browser_run_code");
        Assert.Contains(tools, tool => tool.Name == "browser_runtime_status");
        Assert.Contains(tools, tool => tool.Name == "browser_install_runtime");

        var runtimeStatusResult = await _client.CallToolAsync("browser_runtime_status");
        var runtimeStatusText = GetFirstText(runtimeStatusResult, GetServerLogs());
        using (var runtimeStatusDocument = JsonDocument.Parse(runtimeStatusText))
        {
            Assert.True(runtimeStatusDocument.RootElement.GetProperty("data").GetProperty("isInstalled").GetBoolean());
        }

        var navigateResult = await _client.CallToolAsync("browser_navigate", new Dictionary<string, object?>
        {
            ["url"] = _site!.BaseUri.ToString(),
            ["newTab"] = false,
        });
        var navigateText = GetFirstText(navigateResult, GetServerLogs());
        Assert.Contains("SpiderEyes Demo", navigateText);

        var snapshotResult = await _client.CallToolAsync("browser_snapshot");
        var snapshotText = GetFirstText(snapshotResult, GetServerLogs());
        var snapshot = GetSnapshot(snapshotText);
        Assert.Contains("SpiderEyes Demo", snapshot);

        var buttonRef = ExtractRefForText(snapshot, "Click me");
        var clickResult = await _client.CallToolAsync("browser_click", new Dictionary<string, object?>
        {
            ["target"] = buttonRef,
            ["doubleClick"] = false,
        });
        var clickText = GetFirstText(clickResult, GetServerLogs());
        Assert.Contains("Clicked", GetSnapshot(clickText));
    }

    [Fact]
    public async Task McpServer_CanUseStorageAndRunCode()
    {
        if (!_integrationEnabled)
        {
            return;
        }

        Assert.NotNull(_client);
        Assert.NotNull(_site);

        var installRuntime = await _client!.CallToolAsync("browser_install_runtime", new Dictionary<string, object?>
        {
            ["browser"] = "chromium",
        });
        var installRuntimeText = GetFirstText(installRuntime, GetServerLogs());
        using (var installDocument = JsonDocument.Parse(installRuntimeText))
        {
            Assert.Equal("chromium", installDocument.RootElement.GetProperty("data").GetProperty("installedBrowser").GetString());
            Assert.True(installDocument.RootElement.GetProperty("data").GetProperty("status").GetProperty("isInstalled").GetBoolean());
        }

        await _client!.CallToolAsync("browser_navigate", new Dictionary<string, object?>
        {
            ["url"] = _site!.BaseUri.ToString(),
            ["newTab"] = false,
        });

        var setStorage = await _client.CallToolAsync("browser_localstorage_set", new Dictionary<string, object?>
        {
            ["key"] = "theme",
            ["value"] = "dark",
        });
        Assert.Contains("theme", GetFirstText(setStorage, GetServerLogs()));

        var getStorage = await _client.CallToolAsync("browser_localstorage_get", new Dictionary<string, object?>
        {
            ["key"] = "theme",
        });
        Assert.Contains("dark", GetFirstText(getStorage, GetServerLogs()));

        var runCode = await _client.CallToolAsync("browser_run_code", new Dictionary<string, object?>
        {
            ["code"] = "var title = await page.TitleAsync(); return new { title };",
        });
        Assert.Contains("SpiderEyes Demo", GetFirstText(runCode, GetServerLogs()));
    }

    private static bool ArePlaywrightBrowsersInstalled()
    {
        var browserRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ms-playwright");
        return Directory.Exists(browserRoot);
    }

    private string GetServerLogs()
    {
        lock (_serverLogs)
        {
            return _serverLogs.ToString();
        }
    }

    private static string GetFirstText(CallToolResult result, string serverLogs)
    {
        var block = result.Content.OfType<TextContentBlock>().FirstOrDefault();
        Assert.NotNull(block);
        var text = block!.Text ?? string.Empty;
        Assert.False(
            text.Contains("An error occurred invoking", StringComparison.Ordinal),
            $"Tool invocation failed. Result text: {text}{Environment.NewLine}Server logs:{Environment.NewLine}{serverLogs}");
        return text;
    }

    private static string GetSnapshot(string jsonText)
    {
        using var document = JsonDocument.Parse(jsonText);
        return document.RootElement.GetProperty("page").GetProperty("snapshot").GetString() ?? string.Empty;
    }

    private static string ExtractRefForText(string snapshotText, string textFragment)
    {
        var match = Regex.Match(snapshotText, $"{Regex.Escape(textFragment)}.*?\\[ref=(?<ref>[^\\]]+)\\]", RegexOptions.Singleline);
        Assert.True(match.Success, $"No aria ref was found near text '{textFragment}' in snapshot:{Environment.NewLine}{snapshotText}");
        return match.Groups["ref"].Value;
    }

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task WaitForServerAsync(Uri healthUri)
    {
        using var client = new HttpClient();
        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            try
            {
                using var response = await client.GetAsync(healthUri);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"Server did not become healthy at {healthUri}.");
    }

    private sealed class TestSiteHost : IAsyncDisposable
    {
        private readonly WebApplication _app;

        private TestSiteHost(WebApplication app, Uri baseUri)
        {
            _app = app;
            BaseUri = baseUri;
        }

        public Uri BaseUri { get; }

        public static async Task<TestSiteHost> StartAsync()
        {
            var port = GetFreeTcpPort();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

            var app = builder.Build();
            app.MapGet("/", () => Results.Content(
                """
                <!doctype html>
                <html lang="en">
                <head>
                  <meta charset="utf-8" />
                  <title>SpiderEyes Demo</title>
                </head>
                <body>
                  <h1>SpiderEyes Demo</h1>
                  <label for="nameInput">Name</label>
                  <input id="nameInput" aria-label="Name input" />
                  <button id="clickButton" onclick="document.getElementById('status').textContent = 'Clicked';">Click me</button>
                  <div id="status">Waiting</div>
                  <label for="colorSelect">Color</label>
                  <select id="colorSelect" aria-label="Color select">
                    <option value="red">Red</option>
                    <option value="green">Green</option>
                  </select>
                  <div id="api-status">loading</div>
                  <script>
                    fetch('/api/data')
                      .then(response => response.json())
                      .then(data => { document.getElementById('api-status').textContent = data.value; });
                  </script>
                </body>
                </html>
                """,
                "text/html"));
            app.MapGet("/api/data", () => Results.Json(new { value = "live-data" }));

            await app.StartAsync();
            return new TestSiteHost(app, new Uri($"http://127.0.0.1:{port}/"));
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
