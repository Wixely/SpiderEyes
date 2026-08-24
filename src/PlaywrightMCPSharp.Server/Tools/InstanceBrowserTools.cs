using System.ComponentModel;
using ModelContextProtocol.Server;
using PlaywrightMCPSharp.Server.Models;
using PlaywrightMCPSharp.Server.Services;

namespace PlaywrightMCPSharp.Server.Tools;

[McpServerToolType]
public sealed class InstanceBrowserTools
{
    private readonly BrowserToolExecutor _executor;

    public InstanceBrowserTools(BrowserToolExecutor executor)
    {
        _executor = executor;
    }

    [McpServerTool(Name = "browser_instance_create", UseStructuredContent = true)]
    [Description("Create a named, isolated browser instance for this MCP session. Each instance gets its own browser process, context, cookies, storage, tabs, and artifacts, and runs concurrently with other instances. Use one stable, unique name per agent or job (e.g. 'agent-a') and pass it as instanceName to browser tools. Creation is explicit: browser tools fail for names that were never created.")]
    public Task<BrowserCommandResult> CreateAsync(
        [Description("Unique instance name: lowercase letters, digits, '.', '_', or '-'; max 64 chars. Fails if the name already exists.")] string instanceName,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Browser engine for this instance: chromium, firefox, or webkit. Defaults to the server's configured browser.")] string? browserType = null,
        [Description("Run this instance headless. Defaults to the server's configured setting.")] bool? headless = null,
        [Description("Optional Playwright storage state JSON (cookies and localStorage) to seed the instance's context.")] string? storageState = null)
        => _executor.InstanceCreateAsync(server, instanceName, browserType, headless, storageState, cancellationToken);

    [McpServerTool(Name = "browser_instance_list", UseStructuredContent = true, ReadOnly = true)]
    [Description("List the browser instances that exist for this MCP session, including the default instance if it has been used.")]
    public Task<BrowserCommandResult> ListAsync(McpServer server, CancellationToken cancellationToken)
        => _executor.InstanceListAsync(server, cancellationToken);

    [McpServerTool(Name = "browser_instance_status", UseStructuredContent = true, ReadOnly = true)]
    [Description("Report the status of one named browser instance: engine, headless flag, whether the browser has started, tab count, busy state, and timestamps. Fails for unknown names.")]
    public Task<BrowserCommandResult> StatusAsync(
        [Description("Instance name to inspect.")] string instanceName,
        McpServer server,
        CancellationToken cancellationToken)
        => _executor.InstanceStatusAsync(server, instanceName, cancellationToken);

    [McpServerTool(Name = "browser_instance_close", UseStructuredContent = true)]
    [Description("Close a named browser instance, disposing its browser process, context, tabs, and in-memory state. Other instances are unaffected. Waits for the instance's in-flight command to finish. Fails for unknown names.")]
    public Task<BrowserCommandResult> CloseAsync(
        [Description("Instance name to close.")] string instanceName,
        McpServer server,
        CancellationToken cancellationToken)
        => _executor.InstanceCloseAsync(server, instanceName, cancellationToken);
}
