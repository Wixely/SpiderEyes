using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;

var endpoint = GetEndpoint(args);

try
{
    using var httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(15),
    };
    httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/event-stream");

    var sessionId = await InitializeAsync(httpClient, endpoint);
    await SendInitializedNotificationAsync(httpClient, endpoint, sessionId);
    var tools = await ListToolsAsync(httpClient, endpoint, sessionId);

    Console.WriteLine($"Connected to {endpoint}");
    Console.WriteLine($"Discovered {tools.Count} tool(s).");

    foreach (var tool in tools.OrderBy(static tool => tool.Name, StringComparer.OrdinalIgnoreCase))
    {
        Console.WriteLine();
        Console.WriteLine(tool.Name);
        if (!string.IsNullOrWhiteSpace(tool.Description))
        {
            Console.WriteLine($"  {tool.Description}");
        }
    }

    await RunInteractiveMenuAsync(httpClient, endpoint, sessionId);

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to list MCP tools from {endpoint}");
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static async Task RunInteractiveMenuAsync(HttpClient httpClient, Uri endpoint, string sessionId)
{
    while (true)
    {
        Console.WriteLine();
        Console.WriteLine("Test options:");
        Console.WriteLine("  1. Get server config");
        Console.WriteLine("  2. Get runtime status");
        Console.WriteLine("  3. List tabs");
        Console.WriteLine("  4. Navigate to https://example.com");
        Console.WriteLine("  5. Capture page snapshot");
        Console.WriteLine("  q. Quit");
        Console.Write("Select an option: ");

        var input = Console.ReadLine()?.Trim();
        if (string.Equals(input, "q", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            switch (input)
            {
                case "1":
                    await PrintToolResultAsync(httpClient, endpoint, sessionId, "browser_get_config");
                    break;
                case "2":
                    await PrintToolResultAsync(httpClient, endpoint, sessionId, "browser_runtime_status");
                    break;
                case "3":
                    await PrintToolResultAsync(httpClient, endpoint, sessionId, "browser_tabs");
                    break;
                case "4":
                    await PrintToolResultAsync(
                        httpClient,
                        endpoint,
                        sessionId,
                        "browser_navigate",
                        new Dictionary<string, object?>
                        {
                            ["url"] = "https://example.com",
                        });
                    break;
                case "5":
                    await PrintToolResultAsync(httpClient, endpoint, sessionId, "browser_snapshot");
                    break;
                default:
                    Console.WriteLine("Unknown option.");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Request failed: {ex.Message}");
        }
    }
}

static async Task<string> InitializeAsync(HttpClient httpClient, Uri endpoint)
{
    using var response = await SendRequestAsync(
        httpClient,
        endpoint,
        sessionId: null,
        new JsonRpcRequest(
            Id: "1",
            Method: "initialize",
            Params: new
            {
                protocolVersion = "2025-03-26",
                capabilities = new { },
                clientInfo = new
                {
                    name = "SpiderEyes.McpProbe",
                    version = "1.0.0",
                },
            }));

    _ = ParseEventStreamPayload(await response.Content.ReadAsStringAsync());
    if (!response.Headers.TryGetValues("Mcp-Session-Id", out var values))
    {
        throw new InvalidOperationException("Initialize response did not include an Mcp-Session-Id header.");
    }

    var sessionId = values.FirstOrDefault();
    if (string.IsNullOrWhiteSpace(sessionId))
    {
        throw new InvalidOperationException("Initialize response returned an empty Mcp-Session-Id header.");
    }

    return sessionId;
}

static async Task SendInitializedNotificationAsync(HttpClient httpClient, Uri endpoint, string sessionId)
{
    using var response = await SendRequestAsync(
        httpClient,
        endpoint,
        sessionId,
        new JsonRpcNotification(
            Method: "notifications/initialized",
            Params: new { }));
}

static async Task<IReadOnlyList<McpTool>> ListToolsAsync(HttpClient httpClient, Uri endpoint, string sessionId)
{
    using var response = await SendRequestAsync(
        httpClient,
        endpoint,
        sessionId,
        new JsonRpcRequest(
            Id: "2",
            Method: "tools/list",
            Params: new { }));

    using var document = ParseEventStreamPayload(await response.Content.ReadAsStringAsync());
    var toolsElement = document
        .RootElement
        .GetProperty("result")
        .GetProperty("tools");

    var tools = new List<McpTool>();
    foreach (var toolElement in toolsElement.EnumerateArray())
    {
        tools.Add(new McpTool(
            toolElement.GetProperty("name").GetString() ?? "<unnamed>",
            toolElement.TryGetProperty("description", out var descriptionElement)
                ? descriptionElement.GetString()
                : null));
    }

    return tools;
}

static async Task PrintToolResultAsync(
    HttpClient httpClient,
    Uri endpoint,
    string sessionId,
    string toolName,
    object? arguments = null)
{
    using var response = await SendRequestAsync(
        httpClient,
        endpoint,
        sessionId,
        new JsonRpcRequest(
            Id: Guid.NewGuid().ToString("N"),
            Method: "tools/call",
            Params: new
            {
                name = toolName,
                arguments = arguments ?? new { },
            }));

    using var document = ParseEventStreamPayload(await response.Content.ReadAsStringAsync());
    Console.WriteLine();
    Console.WriteLine($"Result for {toolName}:");
    Console.WriteLine(document.RootElement.ToString());
}

static async Task<HttpResponseMessage> SendRequestAsync(HttpClient httpClient, Uri endpoint, string? sessionId, object payload)
{
    var json = JsonSerializer.Serialize(payload, CreateJsonOptions());
    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
    {
        Content = new StringContent(json, Encoding.UTF8, mediaType: "application/json"),
    };

    if (!string.IsNullOrWhiteSpace(sessionId))
    {
        request.Headers.Add("Mcp-Session-Id", sessionId);
    }

    var response = await httpClient.SendAsync(request);
    response.EnsureSuccessStatusCode();
    return response;
}

static JsonDocument ParseEventStreamPayload(string content)
{
    var dataLine = content
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .FirstOrDefault(static line => line.StartsWith("data: ", StringComparison.Ordinal));

    if (dataLine is null)
    {
        throw new InvalidOperationException("MCP response did not contain an SSE data payload.");
    }

    return JsonDocument.Parse(dataLine["data: ".Length..]);
}

static Uri GetEndpoint(string[] args)
{
    if (args.Length == 0)
    {
        return new Uri("http://127.0.0.1:8931/mcp");
    }

    if (args.Length == 2 && string.Equals(args[0], "--url", StringComparison.OrdinalIgnoreCase))
    {
        return new Uri(args[1], UriKind.Absolute);
    }

    if (args.Length == 1)
    {
        return new Uri(args[0], UriKind.Absolute);
    }

    throw new ArgumentException("Usage: SpiderEyes.McpProbe [--url <mcp-endpoint>] or SpiderEyes.McpProbe [<mcp-endpoint>]");
}

static JsonSerializerOptions CreateJsonOptions()
    => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

internal sealed record JsonRpcRequest(string Id, string Method, object Params)
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; } = "2.0";
}

internal sealed record JsonRpcNotification(string Method, object Params)
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; } = "2.0";
}

internal sealed record McpTool(string Name, string? Description);
