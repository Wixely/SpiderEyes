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

namespace PlaywrightMCPSharp.Server.Tests;

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
        var artifactRoot = Path.Combine(Path.GetTempPath(), "PlaywrightMCPSharp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(artifactRoot);
        var (serverCommand, serverArguments) = GetServerCommandArguments(stdio: false);

        var startInfo = new ProcessStartInfo(serverCommand)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in serverArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["PLAYWRIGHTMCP_PlaywrightMCPSharp__Server__Host"] = "127.0.0.1";
        startInfo.Environment["PLAYWRIGHTMCP_PlaywrightMCPSharp__Server__Port"] = port.ToString();
        startInfo.Environment["PLAYWRIGHTMCP_PlaywrightMCPSharp__Browser__Headless"] = "true";
        startInfo.Environment["PLAYWRIGHTMCP_PlaywrightMCPSharp__Browser__Locale"] = "en-GB";
        startInfo.Environment["PLAYWRIGHTMCP_PlaywrightMCPSharp__Browser__TimezoneId"] = "UTC";
        startInfo.Environment["PLAYWRIGHTMCP_PlaywrightMCPSharp__Session__ArtifactRoot"] = artifactRoot;
        startInfo.Environment["PLAYWRIGHTMCP_PlaywrightMCPSharp__Browser__DownloadsPath"] = Path.Combine(artifactRoot, "downloads");

        _serverProcess = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start PlaywrightMCPSharp server process.");
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

        _client = await McpClient.CreateAsync(transport, CreateClientOptions());
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
        Assert.Contains("PlaywrightMCPSharp Demo", navigateText);

        var snapshotResult = await _client.CallToolAsync("browser_snapshot");
        var snapshotText = GetFirstText(snapshotResult, GetServerLogs());
        var snapshot = GetSnapshot(snapshotText);
        Assert.Contains("PlaywrightMCPSharp Demo", snapshot);

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
        Assert.Contains("PlaywrightMCPSharp Demo", GetFirstText(runCode, GetServerLogs()));
    }

    [Fact]
    public async Task McpServer_ListsTools_AndResponds_OverStdio()
    {
        if (!_integrationEnabled)
        {
            return;
        }

        var artifactRoot = Path.Combine(Path.GetTempPath(), "PlaywrightMCPSharp.Tests.Stdio", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(artifactRoot);
        var (serverCommand, serverArguments) = GetServerCommandArguments(stdio: true);

        await using var client = await McpClient.CreateAsync(
            new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "PlaywrightMCPSharp stdio",
                Command = serverCommand,
                Arguments = serverArguments,
                WorkingDirectory = GetWorkspaceRoot(),
                EnvironmentVariables = new Dictionary<string, string?>
                {
                    ["DOTNET_ENVIRONMENT"] = "Development",
                    ["ASPNETCORE_ENVIRONMENT"] = "Development",
                    ["PLAYWRIGHTMCP_PlaywrightMCPSharp__Browser__Headless"] = "true",
                    ["PLAYWRIGHTMCP_PlaywrightMCPSharp__Session__ArtifactRoot"] = artifactRoot,
                    ["PLAYWRIGHTMCP_PlaywrightMCPSharp__Browser__DownloadsPath"] = Path.Combine(artifactRoot, "downloads"),
                },
                ShutdownTimeout = TimeSpan.FromSeconds(10),
            }),
            CreateClientOptions());

        var tools = await client.ListToolsAsync();
        Assert.Contains(tools, tool => tool.Name == "browser_runtime_status");
        Assert.Contains(tools, tool => tool.Name == "browser_navigate");

        var runtimeStatusResult = await client.CallToolAsync("browser_runtime_status");
        var runtimeStatusText = GetFirstText(runtimeStatusResult, string.Empty);
        using var runtimeStatusDocument = JsonDocument.Parse(runtimeStatusText);
        Assert.Equal("browser_runtime_status", runtimeStatusDocument.RootElement.GetProperty("tool").GetString());
        Assert.Equal("stdio-session", runtimeStatusDocument.RootElement.GetProperty("sessionId").GetString());
    }

    [Fact]
    public async Task McpServer_NamedInstances_AreIsolated_AndNavigateConcurrently()
    {
        if (!_integrationEnabled)
        {
            return;
        }

        Assert.NotNull(_client);
        Assert.NotNull(_site);

        var tools = await _client!.ListToolsAsync();
        Assert.Contains(tools, tool => tool.Name == "browser_instance_create");
        Assert.Contains(tools, tool => tool.Name == "browser_instance_list");
        Assert.Contains(tools, tool => tool.Name == "browser_instance_status");
        Assert.Contains(tools, tool => tool.Name == "browser_instance_close");

        // Two logical agents sharing one MCP session create their own named instances.
        var createA = await _client.CallToolAsync("browser_instance_create", new Dictionary<string, object?>
        {
            ["instanceName"] = "agent-a",
        });
        Assert.Contains("agent-a", GetFirstText(createA, GetServerLogs()));

        var createB = await _client.CallToolAsync("browser_instance_create", new Dictionary<string, object?>
        {
            ["instanceName"] = "agent-b",
        });
        Assert.Contains("agent-b", GetFirstText(createB, GetServerLogs()));

        // Duplicate creation must fail rather than silently reuse the instance.
        var duplicate = await _client.CallToolAsync("browser_instance_create", new Dictionary<string, object?>
        {
            ["instanceName"] = "agent-a",
        });
        Assert.True(duplicate.IsError);

        // Both instances navigate concurrently; per-instance locks mean neither waits on the other.
        var navigateATask = _client.CallToolAsync("browser_navigate", new Dictionary<string, object?>
        {
            ["url"] = _site!.BaseUri.ToString(),
            ["instanceName"] = "agent-a",
        });
        var navigateBTask = _client.CallToolAsync("browser_navigate", new Dictionary<string, object?>
        {
            ["url"] = _site!.BaseUri.ToString(),
            ["instanceName"] = "agent-b",
        });
        var navigations = await Task.WhenAll(navigateATask.AsTask(), navigateBTask.AsTask());

        foreach (var (navigation, expectedInstance) in new[] { (navigations[0], "agent-a"), (navigations[1], "agent-b") })
        {
            var text = GetFirstText(navigation, GetServerLogs());
            using var document = JsonDocument.Parse(text);
            Assert.Equal(expectedInstance, document.RootElement.GetProperty("instanceName").GetString());
            Assert.Contains("PlaywrightMCPSharp Demo", text);
        }

        // State written in agent-a must not be visible in agent-b (separate browser + context).
        var markA = await _client.CallToolAsync("browser_evaluate", new Dictionary<string, object?>
        {
            ["expression"] = "() => { localStorage.setItem('owner', 'agent-a'); return localStorage.getItem('owner'); }",
            ["instanceName"] = "agent-a",
        });
        Assert.Contains("agent-a", GetFirstText(markA, GetServerLogs()));

        var readB = await _client.CallToolAsync("browser_evaluate", new Dictionary<string, object?>
        {
            ["expression"] = "() => localStorage.getItem('owner')",
            ["instanceName"] = "agent-b",
        });
        using (var readBDocument = JsonDocument.Parse(GetFirstText(readB, GetServerLogs())))
        {
            // A null result is serialized as a missing or null 'data' property.
            var ownerVisibleInB = readBDocument.RootElement.TryGetProperty("data", out var data)
                && data.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);
            Assert.False(ownerVisibleInB, "agent-a's localStorage leaked into agent-b.");
        }

        // The lifecycle API reports both named instances as started and isolated.
        var list = await _client.CallToolAsync("browser_instance_list");
        using (var listDocument = JsonDocument.Parse(GetFirstText(list, GetServerLogs())))
        {
            var instances = listDocument.RootElement.GetProperty("data").GetProperty("instances").EnumerateArray()
                .Select(static instance => instance.GetProperty("instanceName").GetString())
                .ToArray();
            Assert.Contains("agent-a", instances);
            Assert.Contains("agent-b", instances);
        }

        var statusA = await _client.CallToolAsync("browser_instance_status", new Dictionary<string, object?>
        {
            ["instanceName"] = "agent-a",
        });
        using (var statusDocument = JsonDocument.Parse(GetFirstText(statusA, GetServerLogs())))
        {
            var data = statusDocument.RootElement.GetProperty("data");
            Assert.True(data.GetProperty("started").GetBoolean());
            Assert.True(data.GetProperty("connected").GetBoolean());
            Assert.True(data.GetProperty("tabCount").GetInt32() >= 1);
        }

        // Unknown names fail deterministically instead of creating a misspelled instance.
        var unknownNavigate = await _client.CallToolAsync("browser_navigate", new Dictionary<string, object?>
        {
            ["url"] = _site!.BaseUri.ToString(),
            ["instanceName"] = "agent-ghost",
        });
        Assert.True(unknownNavigate.IsError);

        // Closing agent-a does not affect agent-b.
        var close = await _client.CallToolAsync("browser_instance_close", new Dictionary<string, object?>
        {
            ["instanceName"] = "agent-a",
        });
        Assert.Contains("Closed", GetFirstText(close, GetServerLogs()));

        var navigateClosed = await _client.CallToolAsync("browser_navigate", new Dictionary<string, object?>
        {
            ["url"] = _site!.BaseUri.ToString(),
            ["instanceName"] = "agent-a",
        });
        Assert.True(navigateClosed.IsError);

        var navigateBAgain = await _client.CallToolAsync("browser_navigate", new Dictionary<string, object?>
        {
            ["url"] = _site!.BaseUri.ToString(),
            ["instanceName"] = "agent-b",
        });
        Assert.Contains("PlaywrightMCPSharp Demo", GetFirstText(navigateBAgain, GetServerLogs()));
    }

    private static bool ArePlaywrightBrowsersInstalled()
    {
        var browserRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ms-playwright");
        return Directory.Exists(browserRoot);
    }

    private static McpClientOptions CreateClientOptions()
    {
        var workspaceRoot = GetWorkspaceRoot();
        return new McpClientOptions
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
        };
    }

    private static string GetWorkspaceRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static (string Command, string[] Arguments) GetServerCommandArguments(bool stdio)
    {
        var configuration = Path.GetFileName(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))
            ?? "Debug";
        var binRoot = Path.GetFullPath(Path.Combine(GetWorkspaceRoot(), "src", "PlaywrightMCPSharp.Server", "bin", configuration, "net10.0"));
        var dllPath = Path.Combine(binRoot, "PlaywrightMCPSharp.dll");
        var exePath = Path.Combine(binRoot, OperatingSystem.IsWindows() ? "PlaywrightMCPSharp.exe" : "PlaywrightMCPSharp");
        var transportArguments = stdio ? ["--stdio"] : Array.Empty<string>();

        if (File.Exists(exePath))
        {
            return (exePath, transportArguments);
        }

        return ("dotnet", [dllPath, .. transportArguments]);
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
                  <title>PlaywrightMCPSharp Demo</title>
                </head>
                <body>
                  <h1>PlaywrightMCPSharp Demo</h1>
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
