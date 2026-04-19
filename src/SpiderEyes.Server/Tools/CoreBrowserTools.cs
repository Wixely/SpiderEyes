using System.ComponentModel;
using ModelContextProtocol.Server;
using SpiderEyes.Server.Models;
using SpiderEyes.Server.Services;

namespace SpiderEyes.Server.Tools;

[McpServerToolType]
public sealed class CoreBrowserTools
{
    private readonly BrowserToolExecutor _executor;

    public CoreBrowserTools(BrowserToolExecutor executor)
    {
        _executor = executor;
    }

    [McpServerTool(Name = "browser_navigate", UseStructuredContent = true, OpenWorld = true)]
    [Description("Navigate the active tab to a URL, optionally opening a new tab.")]
    public Task<BrowserCommandResult> NavigateAsync(
        [Description("URL to navigate to.")] string url,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional tab ID.")] string? tabId = null,
        [Description("Open the URL in a new tab.")] bool newTab = false)
        => _executor.NavigateAsync(server, url, tabId, newTab, cancellationToken);

    [McpServerTool(Name = "browser_navigate_back", UseStructuredContent = true)]
    [Description("Navigate the current tab back in history.")]
    public Task<BrowserCommandResult> NavigateBackAsync(
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional tab ID.")] string? tabId = null)
        => _executor.NavigateBackAsync(server, tabId, cancellationToken);

    [McpServerTool(Name = "browser_close", UseStructuredContent = true)]
    [Description("Close the current tab, a specific tab, or all tabs in the session.")]
    public Task<BrowserCommandResult> CloseAsync(
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional tab ID.")] string? tabId = null,
        [Description("Close every tab in the current session.")] bool closeAll = false)
        => _executor.CloseAsync(server, tabId, closeAll, cancellationToken);

    [McpServerTool(Name = "browser_tabs", UseStructuredContent = true, ReadOnly = true)]
    [Description("List all open tabs for the current session.")]
    public Task<BrowserCommandResult> TabsAsync(McpServer server, CancellationToken cancellationToken)
        => _executor.TabsAsync(server, cancellationToken);

    [McpServerTool(Name = "browser_snapshot", UseStructuredContent = true, ReadOnly = true)]
    [Description("Capture a structured AI-friendly accessibility snapshot of the active page.")]
    public Task<BrowserCommandResult> SnapshotAsync(
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional tab ID.")] string? tabId = null)
        => _executor.SnapshotAsync(server, tabId, cancellationToken);

    [McpServerTool(Name = "browser_take_screenshot", UseStructuredContent = true, ReadOnly = true)]
    [Description("Save a screenshot of the current page.")]
    public Task<BrowserCommandResult> ScreenshotAsync(
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional tab ID.")] string? tabId = null,
        [Description("Optional file name to save under the session artifact directory.")] string? fileName = null,
        [Description("Capture the full scrollable page.")] bool fullPage = false)
        => _executor.ScreenshotAsync(server, tabId, fileName, fullPage, cancellationToken);

    [McpServerTool(Name = "browser_click", UseStructuredContent = true)]
    [Description("Click a target element by aria ref or selector.")]
    public Task<BrowserCommandResult> ClickAsync(
        [Description("Target element reference like e12 or ref=e12, or a selector.")] string target,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional tab ID.")] string? tabId = null,
        [Description("Perform a double-click.")] bool doubleClick = false,
        [Description("Mouse button: left, middle, or right.")] string? button = null)
        => _executor.ClickAsync(server, target, tabId, doubleClick, button, cancellationToken);

    [McpServerTool(Name = "browser_hover", UseStructuredContent = true)]
    [Description("Hover over a target element.")]
    public Task<BrowserCommandResult> HoverAsync(
        [Description("Target element reference like e12 or ref=e12, or a selector.")] string target,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional tab ID.")] string? tabId = null)
        => _executor.HoverAsync(server, target, tabId, cancellationToken);

    [McpServerTool(Name = "browser_drag", UseStructuredContent = true)]
    [Description("Drag from one target element to another.")]
    public Task<BrowserCommandResult> DragAsync(
        [Description("Source element reference or selector.")] string sourceTarget,
        [Description("Destination element reference or selector.")] string targetTarget,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional tab ID.")] string? tabId = null)
        => _executor.DragAsync(server, sourceTarget, targetTarget, tabId, cancellationToken);

    [McpServerTool(Name = "browser_type", UseStructuredContent = true)]
    [Description("Type text into an editable target element.")]
    public Task<BrowserCommandResult> TypeAsync(
        [Description("Target element reference or selector.")] string target,
        [Description("Text to enter.")] string text,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional tab ID.")] string? tabId = null,
        [Description("Press Enter after typing.")] bool submit = false,
        [Description("Type sequentially instead of using fill.")] bool slowly = false)
        => _executor.TypeAsync(server, target, text, tabId, submit, slowly, cancellationToken);

    [McpServerTool(Name = "browser_fill_form", UseStructuredContent = true)]
    [Description("Fill a set of form fields in sequence.")]
    public Task<BrowserCommandResult> FillFormAsync(
        [Description("Form field instructions.")] IReadOnlyList<FormFieldInput> fields,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional tab ID.")] string? tabId = null)
        => _executor.FillFormAsync(server, fields, tabId, cancellationToken);

    [McpServerTool(Name = "browser_select_option", UseStructuredContent = true)]
    [Description("Select one or more options in a select control.")]
    public Task<BrowserCommandResult> SelectOptionAsync(
        [Description("Target select element reference or selector.")] string target,
        [Description("Values to select.")] IReadOnlyList<string> values,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional tab ID.")] string? tabId = null)
        => _executor.SelectOptionAsync(server, target, values, tabId, cancellationToken);

    [McpServerTool(Name = "browser_press_key", UseStructuredContent = true)]
    [Description("Press a keyboard key on the page or a specific target element.")]
    public Task<BrowserCommandResult> PressKeyAsync(
        [Description("Key such as Enter, ArrowDown, Shift+A, or ControlOrMeta+K.")] string key,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional target element reference or selector.")] string? target = null,
        [Description("Optional tab ID.")] string? tabId = null)
        => _executor.PressKeyAsync(server, key, target, tabId, cancellationToken);

    [McpServerTool(Name = "browser_wait_for", UseStructuredContent = true)]
    [Description("Wait for time, text, or a target element condition.")]
    public Task<BrowserCommandResult> WaitForAsync(
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Text that should appear.")] string? text = null,
        [Description("Text that should disappear.")] string? textGone = null,
        [Description("Seconds to wait.")] double? timeSeconds = null,
        [Description("Optional target element reference or selector.")] string? target = null,
        [Description("Optional tab ID.")] string? tabId = null)
        => _executor.WaitForAsync(server, text, textGone, timeSeconds, target, tabId, cancellationToken);

    [McpServerTool(Name = "browser_resize", UseStructuredContent = true)]
    [Description("Resize the viewport for a tab.")]
    public Task<BrowserCommandResult> ResizeAsync(
        [Description("Viewport width in CSS pixels.")] int width,
        [Description("Viewport height in CSS pixels.")] int height,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional tab ID.")] string? tabId = null)
        => _executor.ResizeAsync(server, width, height, tabId, cancellationToken);

    [McpServerTool(Name = "browser_handle_dialog", UseStructuredContent = true)]
    [Description("Arm the next page dialog action.")]
    public Task<BrowserCommandResult> HandleDialogAsync(
        [Description("accept or dismiss.")] string action,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional prompt text when accepting a prompt dialog.")] string? promptText = null)
        => _executor.HandleDialogAsync(server, action, promptText, cancellationToken);

    [McpServerTool(Name = "browser_file_upload", UseStructuredContent = true)]
    [Description("Upload one or more files through an input element.")]
    public Task<BrowserCommandResult> FileUploadAsync(
        [Description("Target file input element reference or selector.")] string target,
        [Description("Files to upload. Relative paths are resolved from the current workspace or MCP roots.")] IReadOnlyList<string> paths,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional tab ID.")] string? tabId = null)
        => _executor.FileUploadAsync(server, target, paths, tabId, cancellationToken);

    [McpServerTool(Name = "browser_console_messages", UseStructuredContent = true, ReadOnly = true)]
    [Description("Return recent console messages captured for the session.")]
    public Task<BrowserCommandResult> ConsoleMessagesAsync(
        [Description("Maximum number of messages to return.")] int limit,
        McpServer server,
        CancellationToken cancellationToken)
        => _executor.ConsoleMessagesAsync(server, limit, cancellationToken);

    [McpServerTool(Name = "browser_network_requests", UseStructuredContent = true, ReadOnly = true)]
    [Description("Return recent network activity captured for the session.")]
    public Task<BrowserCommandResult> NetworkRequestsAsync(
        [Description("Maximum number of requests to return.")] int limit,
        McpServer server,
        CancellationToken cancellationToken)
        => _executor.NetworkRequestsAsync(server, limit, cancellationToken);

    [McpServerTool(Name = "browser_evaluate", UseStructuredContent = true)]
    [Description("Evaluate JavaScript in the current page context.")]
    public Task<BrowserCommandResult> EvaluateAsync(
        [Description("JavaScript expression or function body to evaluate.")] string expression,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional tab ID.")] string? tabId = null)
        => _executor.EvaluateAsync(server, expression, tabId, cancellationToken);

    [McpServerTool(Name = "browser_get_config", UseStructuredContent = true, ReadOnly = true)]
    [Description("Return the effective server and browser configuration.")]
    public Task<BrowserCommandResult> GetConfigAsync(McpServer server, CancellationToken cancellationToken)
        => _executor.GetConfigAsync(server, cancellationToken);
}
