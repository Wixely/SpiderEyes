using System.ComponentModel;
using ModelContextProtocol.Server;
using PlaywrightMCPSharp.Server.Models;
using PlaywrightMCPSharp.Server.Services;

namespace PlaywrightMCPSharp.Server.Tools;

[McpServerToolType]
public sealed class NetworkStorageBrowserTools
{
    private readonly BrowserToolExecutor _executor;

    public NetworkStorageBrowserTools(BrowserToolExecutor executor)
    {
        _executor = executor;
    }

    [McpServerTool(Name = "browser_network_state_set", UseStructuredContent = true)]
    [Description("Set offline mode and optional extra HTTP headers for the current session.")]
    public Task<BrowserCommandResult> SetNetworkStateAsync(
        [Description("Whether the browser context should be offline.")] bool offline,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional extra HTTP headers.")] Dictionary<string, string>? headers = null,
        [Description("Optional named browser instance (see browser_instance_create). Omit to use the session's default instance. Use one stable, unique name per agent or job so concurrent agents do not share a browser.")] string? instanceName = null)
        => _executor.SetNetworkStateAsync(server, offline, headers, instanceName, cancellationToken);

    [McpServerTool(Name = "browser_route", UseStructuredContent = true)]
    [Description("Add a network interception rule.")]
    public Task<BrowserCommandResult> RouteAsync(
        [Description("Route mutation settings.")] RouteMutation mutation,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional named browser instance (see browser_instance_create). Omit to use the session's default instance. Use one stable, unique name per agent or job so concurrent agents do not share a browser.")] string? instanceName = null)
        => _executor.RouteAsync(server, mutation, instanceName, cancellationToken);

    [McpServerTool(Name = "browser_route_list", UseStructuredContent = true, ReadOnly = true)]
    [Description("List currently active network interception rules.")]
    public Task<BrowserCommandResult> RouteListAsync(
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional named browser instance (see browser_instance_create). Omit to use the session's default instance. Use one stable, unique name per agent or job so concurrent agents do not share a browser.")] string? instanceName = null)
        => _executor.RouteListAsync(server, instanceName, cancellationToken);

    [McpServerTool(Name = "browser_unroute", UseStructuredContent = true)]
    [Description("Remove a network interception rule by ID.")]
    public Task<BrowserCommandResult> UnrouteAsync(
        [Description("Route rule ID returned by browser_route.")] string routeId,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional named browser instance (see browser_instance_create). Omit to use the session's default instance. Use one stable, unique name per agent or job so concurrent agents do not share a browser.")] string? instanceName = null)
        => _executor.UnrouteAsync(server, routeId, instanceName, cancellationToken);

    [McpServerTool(Name = "browser_cookie_get", UseStructuredContent = true, ReadOnly = true)]
    [Description("Get cookies for the current browser context.")]
    public Task<BrowserCommandResult> CookieGetAsync(
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional cookie name filter.")] string? name = null,
        [Description("Optional named browser instance (see browser_instance_create). Omit to use the session's default instance. Use one stable, unique name per agent or job so concurrent agents do not share a browser.")] string? instanceName = null)
        => _executor.CookieGetAsync(server, name, instanceName, cancellationToken);

    [McpServerTool(Name = "browser_cookie_set", UseStructuredContent = true)]
    [Description("Add or update a cookie in the current browser context.")]
    public Task<BrowserCommandResult> CookieSetAsync(
        [Description("Cookie settings.")] CookieMutation cookie,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional named browser instance (see browser_instance_create). Omit to use the session's default instance. Use one stable, unique name per agent or job so concurrent agents do not share a browser.")] string? instanceName = null)
        => _executor.CookieSetAsync(server, cookie, instanceName, cancellationToken);

    [McpServerTool(Name = "browser_cookie_delete", UseStructuredContent = true)]
    [Description("Delete a cookie by name from the current browser context.")]
    public Task<BrowserCommandResult> CookieDeleteAsync(
        [Description("Cookie name.")] string name,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional named browser instance (see browser_instance_create). Omit to use the session's default instance. Use one stable, unique name per agent or job so concurrent agents do not share a browser.")] string? instanceName = null)
        => _executor.CookieDeleteAsync(server, name, instanceName, cancellationToken);

    [McpServerTool(Name = "browser_cookie_clear", UseStructuredContent = true)]
    [Description("Clear all cookies from the current browser context.")]
    public Task<BrowserCommandResult> CookieClearAsync(
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional named browser instance (see browser_instance_create). Omit to use the session's default instance. Use one stable, unique name per agent or job so concurrent agents do not share a browser.")] string? instanceName = null)
        => _executor.CookieClearAsync(server, instanceName, cancellationToken);

    [McpServerTool(Name = "browser_localstorage_get", UseStructuredContent = true, ReadOnly = true)]
    [Description("Read localStorage for the active page.")]
    public Task<BrowserCommandResult> LocalStorageGetAsync(
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional storage key.")] string? key = null,
        [Description("Optional tab ID.")] string? tabId = null,
        [Description("Optional named browser instance (see browser_instance_create). Omit to use the session's default instance. Use one stable, unique name per agent or job so concurrent agents do not share a browser.")] string? instanceName = null)
        => _executor.LocalStorageGetAsync(server, key, tabId, instanceName, cancellationToken);

    [McpServerTool(Name = "browser_localstorage_set", UseStructuredContent = true)]
    [Description("Set a localStorage key.")]
    public Task<BrowserCommandResult> LocalStorageSetAsync(
        [Description("Storage key.")] string key,
        [Description("Storage value.")] string value,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional tab ID.")] string? tabId = null,
        [Description("Optional named browser instance (see browser_instance_create). Omit to use the session's default instance. Use one stable, unique name per agent or job so concurrent agents do not share a browser.")] string? instanceName = null)
        => _executor.LocalStorageSetAsync(server, key, value, tabId, instanceName, cancellationToken);

    [McpServerTool(Name = "browser_localstorage_remove", UseStructuredContent = true)]
    [Description("Remove a localStorage key.")]
    public Task<BrowserCommandResult> LocalStorageRemoveAsync(
        [Description("Storage key.")] string key,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional tab ID.")] string? tabId = null,
        [Description("Optional named browser instance (see browser_instance_create). Omit to use the session's default instance. Use one stable, unique name per agent or job so concurrent agents do not share a browser.")] string? instanceName = null)
        => _executor.LocalStorageRemoveAsync(server, key, tabId, instanceName, cancellationToken);

    [McpServerTool(Name = "browser_localstorage_clear", UseStructuredContent = true)]
    [Description("Clear localStorage for the active page.")]
    public Task<BrowserCommandResult> LocalStorageClearAsync(
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional tab ID.")] string? tabId = null,
        [Description("Optional named browser instance (see browser_instance_create). Omit to use the session's default instance. Use one stable, unique name per agent or job so concurrent agents do not share a browser.")] string? instanceName = null)
        => _executor.LocalStorageClearAsync(server, tabId, instanceName, cancellationToken);

    [McpServerTool(Name = "browser_sessionstorage_get", UseStructuredContent = true, ReadOnly = true)]
    [Description("Read sessionStorage for the active page.")]
    public Task<BrowserCommandResult> SessionStorageGetAsync(
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional storage key.")] string? key = null,
        [Description("Optional tab ID.")] string? tabId = null,
        [Description("Optional named browser instance (see browser_instance_create). Omit to use the session's default instance. Use one stable, unique name per agent or job so concurrent agents do not share a browser.")] string? instanceName = null)
        => _executor.SessionStorageGetAsync(server, key, tabId, instanceName, cancellationToken);

    [McpServerTool(Name = "browser_sessionstorage_set", UseStructuredContent = true)]
    [Description("Set a sessionStorage key.")]
    public Task<BrowserCommandResult> SessionStorageSetAsync(
        [Description("Storage key.")] string key,
        [Description("Storage value.")] string value,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional tab ID.")] string? tabId = null,
        [Description("Optional named browser instance (see browser_instance_create). Omit to use the session's default instance. Use one stable, unique name per agent or job so concurrent agents do not share a browser.")] string? instanceName = null)
        => _executor.SessionStorageSetAsync(server, key, value, tabId, instanceName, cancellationToken);

    [McpServerTool(Name = "browser_sessionstorage_remove", UseStructuredContent = true)]
    [Description("Remove a sessionStorage key.")]
    public Task<BrowserCommandResult> SessionStorageRemoveAsync(
        [Description("Storage key.")] string key,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional tab ID.")] string? tabId = null,
        [Description("Optional named browser instance (see browser_instance_create). Omit to use the session's default instance. Use one stable, unique name per agent or job so concurrent agents do not share a browser.")] string? instanceName = null)
        => _executor.SessionStorageRemoveAsync(server, key, tabId, instanceName, cancellationToken);

    [McpServerTool(Name = "browser_sessionstorage_clear", UseStructuredContent = true)]
    [Description("Clear sessionStorage for the active page.")]
    public Task<BrowserCommandResult> SessionStorageClearAsync(
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional tab ID.")] string? tabId = null,
        [Description("Optional named browser instance (see browser_instance_create). Omit to use the session's default instance. Use one stable, unique name per agent or job so concurrent agents do not share a browser.")] string? instanceName = null)
        => _executor.SessionStorageClearAsync(server, tabId, instanceName, cancellationToken);

    [McpServerTool(Name = "browser_storage_state", UseStructuredContent = true, ReadOnly = true)]
    [Description("Export the current browser storage state.")]
    public Task<BrowserCommandResult> StorageStateAsync(
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional output file name.")] string? fileName = null,
        [Description("Optional named browser instance (see browser_instance_create). Omit to use the session's default instance. Use one stable, unique name per agent or job so concurrent agents do not share a browser.")] string? instanceName = null)
        => _executor.StorageStateAsync(server, fileName, instanceName, cancellationToken);

    [McpServerTool(Name = "browser_set_storage_state", UseStructuredContent = true)]
    [Description("Replace the current browser context with one initialized from a storage state file or JSON string.")]
    public Task<BrowserCommandResult> SetStorageStateAsync(
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional storage state file name.")] string? fileName = null,
        [Description("Optional storage state JSON.")] string? json = null,
        [Description("Optional named browser instance (see browser_instance_create). Omit to use the session's default instance. Use one stable, unique name per agent or job so concurrent agents do not share a browser.")] string? instanceName = null)
        => _executor.SetStorageStateAsync(server, fileName, json, instanceName, cancellationToken);
}
