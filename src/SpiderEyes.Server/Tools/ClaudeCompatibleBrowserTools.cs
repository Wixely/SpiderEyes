using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using SpiderEyes.Server.Models;
using SpiderEyes.Server.Services;

namespace SpiderEyes.Server.Tools;

[McpServerToolType]
public sealed class ClaudeCompatibleBrowserTools
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly BrowserToolExecutor _executor;

    public ClaudeCompatibleBrowserTools(BrowserToolExecutor executor)
    {
        _executor = executor;
    }

    [McpServerTool(Name = "browser_runtime_status", ReadOnly = true)]
    [Description("Report whether the configured Playwright browser runtime is installed.")]
    public Task<string> RuntimeStatusAsync(McpServer server, CancellationToken cancellationToken)
        => SerializeAsync(_executor.RuntimeStatusAsync(server, browser: null, cancellationToken));

    [McpServerTool(Name = "browser_install_runtime")]
    [Description("Install the configured Playwright browser runtime.")]
    public Task<string> InstallRuntimeAsync(McpServer server, CancellationToken cancellationToken)
        => SerializeAsync(_executor.InstallRuntimeAsync(server, browser: null, withDependencies: false, cancellationToken));

    [McpServerTool(Name = "browser_get_config", ReadOnly = true)]
    [Description("Return the effective server and browser configuration.")]
    public Task<string> GetConfigAsync(McpServer server, CancellationToken cancellationToken)
        => SerializeAsync(_executor.GetConfigAsync(server, cancellationToken));

    [McpServerTool(Name = "browser_tabs", ReadOnly = true)]
    [Description("List open tabs for the current session.")]
    public Task<string> TabsAsync(McpServer server, CancellationToken cancellationToken)
        => SerializeAsync(_executor.TabsAsync(server, cancellationToken));

    [McpServerTool(Name = "browser_navigate")]
    [Description("Navigate the active tab to a URL.")]
    public Task<string> NavigateAsync(
        [Description("URL to navigate to.")] string url,
        McpServer server,
        CancellationToken cancellationToken)
        => SerializeAsync(_executor.NavigateAsync(server, url, tabId: null, newTab: false, cancellationToken));

    [McpServerTool(Name = "browser_navigate_back")]
    [Description("Navigate the active tab back in history.")]
    public Task<string> NavigateBackAsync(McpServer server, CancellationToken cancellationToken)
        => SerializeAsync(_executor.NavigateBackAsync(server, tabId: null, cancellationToken));

    [McpServerTool(Name = "browser_snapshot", ReadOnly = true)]
    [Description("Capture an AI-friendly accessibility snapshot of the active page.")]
    public Task<string> SnapshotAsync(McpServer server, CancellationToken cancellationToken)
        => SerializeAsync(_executor.SnapshotAsync(server, tabId: null, cancellationToken));

    [McpServerTool(Name = "browser_take_screenshot", ReadOnly = true)]
    [Description("Save a screenshot of the active page.")]
    public Task<string> ScreenshotAsync(McpServer server, CancellationToken cancellationToken)
        => SerializeAsync(_executor.ScreenshotAsync(server, tabId: null, fileName: null, fullPage: false, cancellationToken));

    [McpServerTool(Name = "browser_click")]
    [Description("Click a target element by aria ref or selector.")]
    public Task<string> ClickAsync(
        [Description("Target element reference or selector.")] string target,
        McpServer server,
        CancellationToken cancellationToken)
        => SerializeAsync(_executor.ClickAsync(server, target, tabId: null, doubleClick: false, button: null, cancellationToken));

    [McpServerTool(Name = "browser_type")]
    [Description("Type text into an editable target element.")]
    public Task<string> TypeAsync(
        [Description("Target element reference or selector.")] string target,
        [Description("Text to enter.")] string text,
        McpServer server,
        CancellationToken cancellationToken)
        => SerializeAsync(_executor.TypeAsync(server, target, text, tabId: null, submit: false, slowly: false, cancellationToken));

    [McpServerTool(Name = "browser_press_key")]
    [Description("Press a keyboard key on the active page.")]
    public Task<string> PressKeyAsync(
        [Description("Key such as Enter, ArrowDown, or Escape.")] string key,
        McpServer server,
        CancellationToken cancellationToken)
        => SerializeAsync(_executor.PressKeyAsync(server, key, target: null, tabId: null, cancellationToken));

    [McpServerTool(Name = "browser_verify_text_visible", ReadOnly = true)]
    [Description("Verify that text is visible on the page.")]
    public Task<string> VerifyTextVisibleAsync(
        [Description("Text to search for.")] string text,
        McpServer server,
        CancellationToken cancellationToken)
        => SerializeAsync(_executor.VerifyTextVisibleAsync(server, text, exact: false, tabId: null, cancellationToken));

    private static async Task<string> SerializeAsync(Task<BrowserCommandResult> task)
    {
        var result = await task;
        return JsonSerializer.Serialize(result, JsonOptions);
    }
}
