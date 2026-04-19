using System.ComponentModel;
using ModelContextProtocol.Server;
using SpiderEyes.Server.Models;
using SpiderEyes.Server.Services;

namespace SpiderEyes.Server.Tools;

[McpServerToolType]
public sealed class DevtoolsBrowserTools
{
    private readonly BrowserToolExecutor _executor;

    public DevtoolsBrowserTools(BrowserToolExecutor executor)
    {
        _executor = executor;
    }

    [McpServerTool(Name = "browser_highlight", UseStructuredContent = true)]
    [Description("Highlight a target element in the page.")]
    public Task<BrowserCommandResult> HighlightAsync(
        [Description("Target element reference or selector.")] string target,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional tab ID.")] string? tabId = null)
        => _executor.HighlightAsync(server, target, tabId, cancellationToken);

    [McpServerTool(Name = "browser_hide_highlight", UseStructuredContent = true)]
    [Description("Remove all highlights previously added by browser_highlight.")]
    public Task<BrowserCommandResult> HideHighlightAsync(
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional tab ID.")] string? tabId = null)
        => _executor.HideHighlightAsync(server, tabId, cancellationToken);

    [McpServerTool(Name = "browser_generate_locator", UseStructuredContent = true, ReadOnly = true)]
    [Description("Generate locator suggestions for a target element.")]
    public Task<BrowserCommandResult> GenerateLocatorAsync(
        [Description("Target element reference or selector.")] string target,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional tab ID.")] string? tabId = null)
        => _executor.GenerateLocatorAsync(server, target, tabId, cancellationToken);

    [McpServerTool(Name = "browser_start_tracing", UseStructuredContent = true)]
    [Description("Start Playwright tracing for the current session.")]
    public Task<BrowserCommandResult> StartTracingAsync(
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional trace title.")] string? title = null)
        => _executor.StartTracingAsync(server, title, cancellationToken);

    [McpServerTool(Name = "browser_stop_tracing", UseStructuredContent = true)]
    [Description("Stop Playwright tracing and save a trace artifact.")]
    public Task<BrowserCommandResult> StopTracingAsync(
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional output file name.")] string? fileName = null)
        => _executor.StopTracingAsync(server, fileName, cancellationToken);

    [McpServerTool(Name = "browser_verify_element_visible", UseStructuredContent = true, ReadOnly = true)]
    [Description("Verify that a target element is currently visible.")]
    public Task<BrowserCommandResult> VerifyElementVisibleAsync(
        [Description("Target element reference or selector.")] string target,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional tab ID.")] string? tabId = null)
        => _executor.VerifyElementVisibleAsync(server, target, tabId, cancellationToken);

    [McpServerTool(Name = "browser_verify_text_visible", UseStructuredContent = true, ReadOnly = true)]
    [Description("Verify that text is visible on the page.")]
    public Task<BrowserCommandResult> VerifyTextVisibleAsync(
        [Description("Text to search for.")] string text,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Require an exact text match.")] bool exact = false,
        [Description("Optional tab ID.")] string? tabId = null)
        => _executor.VerifyTextVisibleAsync(server, text, exact, tabId, cancellationToken);

    [McpServerTool(Name = "browser_verify_value", UseStructuredContent = true, ReadOnly = true)]
    [Description("Verify that an input element has an expected value.")]
    public Task<BrowserCommandResult> VerifyValueAsync(
        [Description("Target input element reference or selector.")] string target,
        [Description("Expected value.")] string expectedValue,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional tab ID.")] string? tabId = null)
        => _executor.VerifyValueAsync(server, target, expectedValue, tabId, cancellationToken);

    [McpServerTool(Name = "browser_verify_list_visible", UseStructuredContent = true, ReadOnly = true)]
    [Description("Verify that a list of expected strings is visible.")]
    public Task<BrowserCommandResult> VerifyListVisibleAsync(
        [Description("Expected strings that should be visible.")] IReadOnlyList<string> items,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional parent target to limit the check.")] string? target = null,
        [Description("Optional tab ID.")] string? tabId = null)
        => _executor.VerifyListVisibleAsync(server, items, target, tabId, cancellationToken);

    [McpServerTool(Name = "browser_mouse_click_xy", UseStructuredContent = true)]
    [Description("Click absolute viewport coordinates.")]
    public Task<BrowserCommandResult> MouseClickAsync(
        [Description("X coordinate in CSS pixels.")] float x,
        [Description("Y coordinate in CSS pixels.")] float y,
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Optional tab ID.")] string? tabId = null)
        => _executor.MouseClickAsync(server, x, y, tabId, cancellationToken);

    [McpServerTool(Name = "browser_mouse_down", UseStructuredContent = true)]
    [Description("Press a mouse button down at the current pointer location.")]
    public Task<BrowserCommandResult> MouseDownAsync(
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Mouse button: left, middle, or right.")] string? button = null)
        => _executor.MouseDownAsync(server, button, cancellationToken);

    [McpServerTool(Name = "browser_mouse_drag_xy", UseStructuredContent = true)]
    [Description("Perform a drag gesture between viewport coordinates.")]
    public Task<BrowserCommandResult> MouseDragAsync(
        [Description("Drag start X coordinate.")] float fromX,
        [Description("Drag start Y coordinate.")] float fromY,
        [Description("Drag end X coordinate.")] float toX,
        [Description("Drag end Y coordinate.")] float toY,
        McpServer server,
        CancellationToken cancellationToken)
        => _executor.MouseDragAsync(server, fromX, fromY, toX, toY, cancellationToken);

    [McpServerTool(Name = "browser_mouse_move_xy", UseStructuredContent = true)]
    [Description("Move the mouse pointer to viewport coordinates.")]
    public Task<BrowserCommandResult> MouseMoveAsync(
        [Description("X coordinate in CSS pixels.")] float x,
        [Description("Y coordinate in CSS pixels.")] float y,
        [Description("Number of intermediate steps.")] int steps,
        McpServer server,
        CancellationToken cancellationToken)
        => _executor.MouseMoveAsync(server, x, y, steps, cancellationToken);

    [McpServerTool(Name = "browser_mouse_up", UseStructuredContent = true)]
    [Description("Release a mouse button at the current pointer location.")]
    public Task<BrowserCommandResult> MouseUpAsync(
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Mouse button: left, middle, or right.")] string? button = null)
        => _executor.MouseUpAsync(server, button, cancellationToken);

    [McpServerTool(Name = "browser_mouse_wheel", UseStructuredContent = true)]
    [Description("Scroll the mouse wheel by the supplied deltas.")]
    public Task<BrowserCommandResult> MouseWheelAsync(
        [Description("Horizontal wheel delta.")] float deltaX,
        [Description("Vertical wheel delta.")] float deltaY,
        McpServer server,
        CancellationToken cancellationToken)
        => _executor.MouseWheelAsync(server, deltaX, deltaY, cancellationToken);

    [McpServerTool(Name = "browser_run_code", UseStructuredContent = true)]
    [Description("Execute a C# script against the current Playwright page and browser context.")]
    public Task<BrowserCommandResult> RunCodeAsync(
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Inline C# script to run.")] string? code = null,
        [Description("Optional script file to load from MCP roots or the workspace.")] string? fileName = null)
        => _executor.RunCodeAsync(server, code, fileName, cancellationToken);

    [McpServerTool(Name = "browser_runtime_status", UseStructuredContent = true, ReadOnly = true)]
    [Description("Report whether the required Playwright browser runtime is already installed on the host.")]
    public Task<BrowserCommandResult> RuntimeStatusAsync(
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Browser to inspect: configured, chromium, firefox, or webkit.")] string? browser = null)
        => _executor.RuntimeStatusAsync(server, browser, cancellationToken);

    [McpServerTool(Name = "browser_install_runtime", UseStructuredContent = true)]
    [Description("Install the required Playwright browser runtime on the host machine from this MCP session.")]
    public Task<BrowserCommandResult> InstallRuntimeAsync(
        McpServer server,
        CancellationToken cancellationToken,
        [Description("Browser to install: configured, chromium, firefox, or webkit.")] string? browser = null,
        [Description("Also install OS-level dependencies when supported by Playwright.")] bool withDependencies = false)
        => _executor.InstallRuntimeAsync(server, browser, withDependencies, cancellationToken);
}
