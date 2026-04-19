using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using ModelContextProtocol.Server;
using SpiderEyes.Server.Configuration;
using SpiderEyes.Server.Models;

namespace SpiderEyes.Server.Services;

public sealed class BrowserToolExecutor
{
    private readonly BrowserSessionManager _sessionManager;
    private readonly FileAccessService _fileAccessService;
    private readonly PlaywrightRuntimeService _playwrightRuntimeService;
    private readonly RunCodeService _runCodeService;
    private readonly IOptionsMonitor<SpiderEyesOptions> _optionsMonitor;

    public BrowserToolExecutor(
        BrowserSessionManager sessionManager,
        FileAccessService fileAccessService,
        PlaywrightRuntimeService playwrightRuntimeService,
        RunCodeService runCodeService,
        IOptionsMonitor<SpiderEyesOptions> optionsMonitor)
    {
        _sessionManager = sessionManager;
        _fileAccessService = fileAccessService;
        _playwrightRuntimeService = playwrightRuntimeService;
        _runCodeService = runCodeService;
        _optionsMonitor = optionsMonitor;
    }

    public Task<BrowserCommandResult> NavigateAsync(McpServer server, string url, string? tabId, bool newTab, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_navigate", PageCaptureMode.Snapshot, async (session, ct) =>
        {
            var page = newTab ? await session.NewPageAsync(ct) : await session.GetPageAsync(tabId, true, ct);
            var response = await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
            return new BrowserActionPayload(
                Data: new
                {
                    url = page.Url,
                    status = response?.Status,
                    ok = response?.Ok,
                },
                TabId: GetTabId(session, page),
                Message: $"Navigated to {page.Url}.");
        }, cancellationToken);

    public Task<BrowserCommandResult> NavigateBackAsync(McpServer server, string? tabId, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_navigate_back", PageCaptureMode.Snapshot, async (session, ct) =>
        {
            var page = await session.GetPageAsync(tabId, true, ct);
            var response = await page.GoBackAsync();
            return new BrowserActionPayload(
                Data: new { url = page.Url, status = response?.Status },
                TabId: GetTabId(session, page),
                Message: "Navigated back.");
        }, cancellationToken);

    public Task<BrowserCommandResult> CloseAsync(McpServer server, string? tabId, bool closeAll, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_close", PageCaptureMode.Summary, async (session, ct) =>
        {
            if (!session.HasPages)
            {
                return new BrowserActionPayload(Message: "No tabs are open.");
            }

            if (closeAll)
            {
                var tabs = (await session.BuildPageStateAsync(null, includeSnapshot: false, ct))?.Tabs ?? [];
                foreach (var tab in tabs)
                {
                    var page = await session.GetPageAsync(tab.TabId, false, ct);
                    await page.CloseAsync();
                }

                return new BrowserActionPayload(Data: new { closed = tabs.Count }, Message: "Closed all tabs.");
            }

            var pageToClose = await session.GetPageAsync(tabId, false, ct);
            var closedTabId = GetTabId(session, pageToClose);
            await pageToClose.CloseAsync();
            return new BrowserActionPayload(Data: new { closedTabId }, Message: $"Closed tab {closedTabId}.");
        }, cancellationToken);

    public Task<BrowserCommandResult> TabsAsync(McpServer server, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_tabs", PageCaptureMode.Summary, async (session, ct) =>
        {
            await session.EnsureStartedAsync(ct);
            var pageState = await session.BuildPageStateAsync(null, includeSnapshot: false, ct);
            return new BrowserActionPayload(Data: new { tabs = pageState?.Tabs ?? [] }, Message: "Listed tabs.");
        }, cancellationToken);

    public Task<BrowserCommandResult> SnapshotAsync(McpServer server, string? tabId, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_snapshot", PageCaptureMode.Snapshot, static async (session, ct) =>
        {
            var page = await session.GetPageAsync(null, true, ct);
            return new BrowserActionPayload(TabId: tabIdOrCurrent(session, page), Message: "Captured page snapshot.");

            static string tabIdOrCurrent(BrowserSession session, IPage page) => GetTabId(session, page);
        }, cancellationToken);

    public Task<BrowserCommandResult> ScreenshotAsync(McpServer server, string? tabId, string? fileName, bool fullPage, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_take_screenshot", PageCaptureMode.Summary, async (session, ct) =>
        {
            var page = await session.GetPageAsync(tabId, true, ct);
            var path = session.CreateArtifactPath(fileName ?? "page", ".png");
            await page.ScreenshotAsync(new() { Path = path, FullPage = fullPage });
            return new BrowserActionPayload(
                Data: new { path, fullPage },
                TabId: GetTabId(session, page),
                Message: "Saved screenshot.",
                Artifacts: [new ArtifactInfo { Name = Path.GetFileName(path), Path = path, MimeType = "image/png" }]);
        }, cancellationToken);

    public Task<BrowserCommandResult> ClickAsync(McpServer server, string target, string? tabId, bool doubleClick, string? button, CancellationToken cancellationToken)
        => ExecuteTargetAsync(server, "browser_click", target, tabId, PageCaptureMode.Snapshot, async (session, page, locator, ct) =>
        {
            var mouseButton = ParseMouseButton(button);
            if (doubleClick)
            {
                await locator.DblClickAsync(new() { Button = mouseButton });
            }
            else
            {
                await locator.ClickAsync(new() { Button = mouseButton });
            }

            return new BrowserActionPayload(TabId: GetTabId(session, page), Message: "Clicked target.");
        }, cancellationToken);

    public Task<BrowserCommandResult> HoverAsync(McpServer server, string target, string? tabId, CancellationToken cancellationToken)
        => ExecuteTargetAsync(server, "browser_hover", target, tabId, PageCaptureMode.Snapshot, async (session, page, locator, ct) =>
        {
            await locator.HoverAsync();
            return new BrowserActionPayload(TabId: GetTabId(session, page), Message: "Hovered target.");
        }, cancellationToken);

    public Task<BrowserCommandResult> DragAsync(McpServer server, string sourceTarget, string targetTarget, string? tabId, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_drag", PageCaptureMode.Snapshot, async (session, ct) =>
        {
            var page = await session.GetPageAsync(tabId, true, ct);
            var source = session.ResolveTarget(page, sourceTarget);
            var destination = session.ResolveTarget(page, targetTarget);
            await source.DragToAsync(destination);
            return new BrowserActionPayload(TabId: GetTabId(session, page), Message: "Dragged between targets.");
        }, cancellationToken);

    public Task<BrowserCommandResult> TypeAsync(McpServer server, string target, string text, string? tabId, bool submit, bool slowly, CancellationToken cancellationToken)
        => ExecuteTargetAsync(server, "browser_type", target, tabId, PageCaptureMode.Snapshot, async (session, page, locator, ct) =>
        {
            if (slowly)
            {
                await locator.ClickAsync();
                await locator.PressSequentiallyAsync(text);
            }
            else
            {
                await locator.FillAsync(text);
            }

            if (submit)
            {
                await locator.PressAsync("Enter");
            }

            return new BrowserActionPayload(TabId: GetTabId(session, page), Message: "Entered text.");
        }, cancellationToken);

    public Task<BrowserCommandResult> FillFormAsync(McpServer server, IReadOnlyList<FormFieldInput> fields, string? tabId, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_fill_form", PageCaptureMode.Snapshot, async (session, ct) =>
        {
            var page = await session.GetPageAsync(tabId, true, ct);
            foreach (var field in fields)
            {
                var locator = session.ResolveTarget(page, field.Target);
                switch (field.Kind?.ToLowerInvariant())
                {
                    case "select":
                        await locator.SelectOptionAsync(field.Values ?? [field.Value ?? string.Empty]);
                        break;
                    case "check":
                        await locator.CheckAsync();
                        break;
                    case "uncheck":
                        await locator.UncheckAsync();
                        break;
                    default:
                        await locator.FillAsync(field.Value ?? string.Empty);
                        break;
                }
            }

            return new BrowserActionPayload(Data: new { fieldCount = fields.Count }, TabId: GetTabId(session, page), Message: "Filled form fields.");
        }, cancellationToken);

    public Task<BrowserCommandResult> SelectOptionAsync(McpServer server, string target, IReadOnlyList<string> values, string? tabId, CancellationToken cancellationToken)
        => ExecuteTargetAsync(server, "browser_select_option", target, tabId, PageCaptureMode.Snapshot, async (session, page, locator, ct) =>
        {
            await locator.SelectOptionAsync(values);
            return new BrowserActionPayload(Data: new { values }, TabId: GetTabId(session, page), Message: "Selected option(s).");
        }, cancellationToken);

    public Task<BrowserCommandResult> PressKeyAsync(McpServer server, string key, string? target, string? tabId, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_press_key", PageCaptureMode.Snapshot, async (session, ct) =>
        {
            var page = await session.GetPageAsync(tabId, true, ct);
            if (string.IsNullOrWhiteSpace(target))
            {
                await page.Keyboard.PressAsync(key);
            }
            else
            {
                var locator = session.ResolveTarget(page, target);
                await locator.PressAsync(key);
            }

            return new BrowserActionPayload(TabId: GetTabId(session, page), Message: $"Pressed key {key}.");
        }, cancellationToken);

    public Task<BrowserCommandResult> WaitForAsync(McpServer server, string? text, string? textGone, double? timeSeconds, string? target, string? tabId, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_wait_for", PageCaptureMode.Summary, async (session, ct) =>
        {
            var page = await session.GetPageAsync(tabId, true, ct);
            if (timeSeconds is double delaySeconds && delaySeconds > 0)
            {
                await page.WaitForTimeoutAsync((float)(delaySeconds * 1000));
            }

            if (!string.IsNullOrWhiteSpace(target))
            {
                await session.ResolveTarget(page, target).WaitForAsync();
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                await page.GetByText(text).First.WaitForAsync();
            }

            if (!string.IsNullOrWhiteSpace(textGone))
            {
                await page.GetByText(textGone).First.WaitForAsync(new() { State = WaitForSelectorState.Hidden });
            }

            return new BrowserActionPayload(TabId: GetTabId(session, page), Message: "Completed wait.");
        }, cancellationToken);

    public Task<BrowserCommandResult> ResizeAsync(McpServer server, int width, int height, string? tabId, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_resize", PageCaptureMode.Snapshot, async (session, ct) =>
        {
            var page = await session.GetPageAsync(tabId, true, ct);
            await page.SetViewportSizeAsync(width, height);
            return new BrowserActionPayload(Data: new { width, height }, TabId: GetTabId(session, page), Message: "Resized viewport.");
        }, cancellationToken);

    public Task<BrowserCommandResult> HandleDialogAsync(McpServer server, string action, string? promptText, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_handle_dialog", PageCaptureMode.None, async (session, ct) =>
        {
            await session.EnsureStartedAsync(ct);
            session.ArmNextDialog(action, promptText);
            return new BrowserActionPayload(Data: new { armed = true, dialogs = session.GetDialogs() }, Message: "Armed next dialog action.");
        }, cancellationToken);

    public Task<BrowserCommandResult> FileUploadAsync(McpServer server, string target, IReadOnlyList<string> paths, string? tabId, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_file_upload", PageCaptureMode.Snapshot, async (session, ct) =>
        {
            var page = await session.GetPageAsync(tabId, true, ct);
            var locator = session.ResolveTarget(page, target);
            var resolvedPaths = await _fileAccessService.ResolveReadablePathsAsync(session, server, paths, ct);
            await locator.SetInputFilesAsync(resolvedPaths);
            return new BrowserActionPayload(Data: new { files = resolvedPaths }, TabId: GetTabId(session, page), Message: "Uploaded files.");
        }, cancellationToken);

    public Task<BrowserCommandResult> ConsoleMessagesAsync(McpServer server, int limit, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_console_messages", PageCaptureMode.None, async (session, ct) =>
        {
            await session.EnsureStartedAsync(ct);
            return new BrowserActionPayload(Data: new { entries = session.GetConsoleEntries(limit) }, Message: "Collected console messages.");
        }, cancellationToken);

    public Task<BrowserCommandResult> NetworkRequestsAsync(McpServer server, int limit, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_network_requests", PageCaptureMode.None, async (session, ct) =>
        {
            await session.EnsureStartedAsync(ct);
            return new BrowserActionPayload(Data: new { entries = session.GetNetworkEntries(limit) }, Message: "Collected network requests.");
        }, cancellationToken);

    public Task<BrowserCommandResult> EvaluateAsync(McpServer server, string expression, string? tabId, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_evaluate", PageCaptureMode.Summary, async (session, ct) =>
        {
            var page = await session.GetPageAsync(tabId, true, ct);
            var result = await page.EvaluateAsync<JsonElement>(expression);
            return new BrowserActionPayload(Data: JsonSerializer.Deserialize<object>(result.GetRawText()), TabId: GetTabId(session, page), Message: "Evaluated JavaScript.");
        }, cancellationToken);

    public Task<BrowserCommandResult> GetConfigAsync(McpServer server, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_get_config", PageCaptureMode.None, async (session, ct) =>
        {
            await session.EnsureStartedAsync(ct);
            var options = _optionsMonitor.CurrentValue;
            return new BrowserActionPayload(Data: new
            {
                server = new
                {
                    options.Server.Host,
                    options.Server.Port,
                    options.Server.Route,
                },
                security = new
                {
                    mode = options.Security.Mode.ToString(),
                    hasBearerToken = !string.IsNullOrWhiteSpace(options.Security.BearerToken),
                },
                browser = new
                {
                    options.Browser.BrowserType,
                    options.Browser.Headless,
                    options.Browser.Locale,
                    options.Browser.TimezoneId,
                },
                features = options.Features,
            }, Message: "Loaded effective configuration.");
        }, cancellationToken);

    public Task<BrowserCommandResult> SetNetworkStateAsync(McpServer server, bool offline, Dictionary<string, string>? headers, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_network_state_set", PageCaptureMode.None, async (session, ct) =>
        {
            await session.EnsureStartedAsync(ct);
            await session.SetOfflineAsync(offline);
            await session.SetExtraHttpHeadersAsync(headers);
            return new BrowserActionPayload(Data: new { offline, headers }, Message: "Updated network state.");
        }, cancellationToken);

    public Task<BrowserCommandResult> RouteAsync(McpServer server, RouteMutation mutation, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_route", PageCaptureMode.None, async (session, ct) =>
        {
            await session.EnsureStartedAsync(ct);
            var routeId = session.AddRoute(mutation);
            return new BrowserActionPayload(Data: new { routeId }, Message: "Added route rule.");
        }, cancellationToken);

    public Task<BrowserCommandResult> RouteListAsync(McpServer server, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_route_list", PageCaptureMode.None, async (session, ct) =>
        {
            await session.EnsureStartedAsync(ct);
            return new BrowserActionPayload(Data: new { routes = session.GetRoutes() }, Message: "Listed route rules.");
        }, cancellationToken);

    public Task<BrowserCommandResult> UnrouteAsync(McpServer server, string routeId, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_unroute", PageCaptureMode.None, async (session, ct) =>
        {
            await session.EnsureStartedAsync(ct);
            return new BrowserActionPayload(Data: new { removed = session.RemoveRoute(routeId), routeId }, Message: "Removed route rule.");
        }, cancellationToken);

    public Task<BrowserCommandResult> CookieGetAsync(McpServer server, string? name, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_cookie_get", PageCaptureMode.None, async (session, ct) =>
        {
            var page = await session.GetPageAsync(null, true, ct);
            var cookies = await page.Context.CookiesAsync();
            var filtered = string.IsNullOrWhiteSpace(name) ? cookies : cookies.Where(cookie => string.Equals(cookie.Name, name, StringComparison.Ordinal)).ToArray();
            return new BrowserActionPayload(Data: new { cookies = filtered }, Message: "Loaded cookies.");
        }, cancellationToken);

    public Task<BrowserCommandResult> CookieSetAsync(McpServer server, CookieMutation cookie, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_cookie_set", PageCaptureMode.None, async (session, ct) =>
        {
            var page = await session.GetPageAsync(null, true, ct);
            await page.Context.AddCookiesAsync(
            [
                new Cookie
                {
                    Name = cookie.Name,
                    Value = cookie.Value,
                    Url = cookie.Url,
                    Domain = cookie.Domain,
                    Path = cookie.Path,
                    Expires = cookie.ExpiresUnixSeconds,
                    HttpOnly = cookie.HttpOnly ?? false,
                    Secure = cookie.Secure ?? false,
                    SameSite = ParseSameSite(cookie.SameSite),
                }
            ]);
            return new BrowserActionPayload(Data: new { cookie.Name }, Message: "Cookie added.");
        }, cancellationToken);

    public Task<BrowserCommandResult> CookieDeleteAsync(McpServer server, string name, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_cookie_delete", PageCaptureMode.None, async (session, ct) =>
        {
            var page = await session.GetPageAsync(null, true, ct);
            var cookies = await page.Context.CookiesAsync();
            var survivors = cookies.Where(cookie => !string.Equals(cookie.Name, name, StringComparison.Ordinal)).ToArray();
            var statePath = session.CreateArtifactPath("cookies-filtered", ".json");
            await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(new { cookies = survivors }), ct);
            await session.RecreateContextAsync(statePath, ct);
            return new BrowserActionPayload(Data: new { removedName = name }, Message: "Cookie removed by recreating context.");
        }, cancellationToken);

    public Task<BrowserCommandResult> CookieClearAsync(McpServer server, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_cookie_clear", PageCaptureMode.None, async (session, ct) =>
        {
            var emptyState = session.CreateArtifactPath("storage-empty", ".json");
            await File.WriteAllTextAsync(emptyState, "{\"cookies\":[],\"origins\":[]}", ct);
            await session.RecreateContextAsync(emptyState, ct);
            return new BrowserActionPayload(Message: "Cleared cookies by recreating context.");
        }, cancellationToken);

    public Task<BrowserCommandResult> LocalStorageGetAsync(McpServer server, string? key, string? tabId, CancellationToken cancellationToken)
        => StorageGetAsync(server, "browser_localstorage_get", "localStorage", key, tabId, cancellationToken);

    public Task<BrowserCommandResult> LocalStorageSetAsync(McpServer server, string key, string value, string? tabId, CancellationToken cancellationToken)
        => StorageSetAsync(server, "browser_localstorage_set", "localStorage", key, value, tabId, cancellationToken);

    public Task<BrowserCommandResult> LocalStorageRemoveAsync(McpServer server, string key, string? tabId, CancellationToken cancellationToken)
        => StorageRemoveAsync(server, "browser_localstorage_remove", "localStorage", key, tabId, cancellationToken);

    public Task<BrowserCommandResult> LocalStorageClearAsync(McpServer server, string? tabId, CancellationToken cancellationToken)
        => StorageClearAsync(server, "browser_localstorage_clear", "localStorage", tabId, cancellationToken);

    public Task<BrowserCommandResult> SessionStorageGetAsync(McpServer server, string? key, string? tabId, CancellationToken cancellationToken)
        => StorageGetAsync(server, "browser_sessionstorage_get", "sessionStorage", key, tabId, cancellationToken);

    public Task<BrowserCommandResult> SessionStorageSetAsync(McpServer server, string key, string value, string? tabId, CancellationToken cancellationToken)
        => StorageSetAsync(server, "browser_sessionstorage_set", "sessionStorage", key, value, tabId, cancellationToken);

    public Task<BrowserCommandResult> SessionStorageRemoveAsync(McpServer server, string key, string? tabId, CancellationToken cancellationToken)
        => StorageRemoveAsync(server, "browser_sessionstorage_remove", "sessionStorage", key, tabId, cancellationToken);

    public Task<BrowserCommandResult> SessionStorageClearAsync(McpServer server, string? tabId, CancellationToken cancellationToken)
        => StorageClearAsync(server, "browser_sessionstorage_clear", "sessionStorage", tabId, cancellationToken);

    public Task<BrowserCommandResult> StorageStateAsync(McpServer server, string? fileName, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_storage_state", PageCaptureMode.None, async (session, ct) =>
        {
            var page = await session.GetPageAsync(null, true, ct);
            var path = session.CreateArtifactPath(fileName ?? "storage-state", ".json");
            await page.Context.StorageStateAsync(new() { Path = path });
            var json = await File.ReadAllTextAsync(path, ct);
            return new BrowserActionPayload(
                Data: JsonSerializer.Deserialize<object>(json),
                Message: "Exported storage state.",
                Artifacts: [new ArtifactInfo { Name = Path.GetFileName(path), Path = path, MimeType = "application/json" }]);
        }, cancellationToken);

    public Task<BrowserCommandResult> SetStorageStateAsync(McpServer server, string? fileName, string? json, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_set_storage_state", PageCaptureMode.Summary, async (session, ct) =>
        {
            string path;
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                path = await _fileAccessService.ResolveReadablePathAsync(session, server, fileName, ct);
            }
            else
            {
                path = session.CreateArtifactPath("storage-import", ".json");
                await File.WriteAllTextAsync(path, json ?? throw new InvalidOperationException("Storage state JSON is required."), ct);
            }

            await session.RecreateContextAsync(path, ct);
            return new BrowserActionPayload(Data: new { path }, Message: "Recreated browser context with storage state.");
        }, cancellationToken);

    public Task<BrowserCommandResult> HighlightAsync(McpServer server, string target, string? tabId, CancellationToken cancellationToken)
        => ExecuteTargetAsync(server, "browser_highlight", target, tabId, PageCaptureMode.Summary, async (session, page, locator, ct) =>
        {
            await locator.EvaluateAllAsync<object>(
                """
                elements => {
                  for (const element of elements) {
                    element.setAttribute('data-spidereyes-highlight', '1');
                    element.style.outline = '3px solid #ff4d00';
                    element.style.outlineOffset = '2px';
                  }
                }
                """);
            return new BrowserActionPayload(TabId: GetTabId(session, page), Message: "Highlighted target.");
        }, cancellationToken);

    public Task<BrowserCommandResult> HideHighlightAsync(McpServer server, string? tabId, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_hide_highlight", PageCaptureMode.Summary, async (session, ct) =>
        {
            var page = await session.GetPageAsync(tabId, true, ct);
            foreach (var frame in page.Frames)
            {
                await frame.EvaluateAsync(
                    """
                    () => {
                      for (const element of document.querySelectorAll('[data-spidereyes-highlight="1"]')) {
                        element.style.outline = '';
                        element.style.outlineOffset = '';
                        element.removeAttribute('data-spidereyes-highlight');
                      }
                    }
                    """);
            }

            return new BrowserActionPayload(TabId: GetTabId(session, page), Message: "Removed highlights.");
        }, cancellationToken);

    public Task<BrowserCommandResult> GenerateLocatorAsync(McpServer server, string target, string? tabId, CancellationToken cancellationToken)
        => ExecuteTargetAsync(server, "browser_generate_locator", target, tabId, PageCaptureMode.None, async (session, page, locator, ct) =>
        {
            var meta = await locator.EvaluateAsync<JsonElement>(
                """
                element => ({
                  id: element.id || null,
                  testId: element.getAttribute('data-testid'),
                  name: element.getAttribute('name'),
                  ariaLabel: element.getAttribute('aria-label'),
                  placeholder: element.getAttribute('placeholder'),
                  role: element.getAttribute('role'),
                  tagName: element.tagName.toLowerCase(),
                  text: (element.textContent || '').trim().slice(0, 80)
                })
                """);
            var locators = BuildLocatorSuggestions(meta);
            return new BrowserActionPayload(Data: new { target, suggestions = locators }, Message: "Generated locator suggestions.");
        }, cancellationToken);

    public Task<BrowserCommandResult> StartTracingAsync(McpServer server, string? title, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_start_tracing", PageCaptureMode.None, async (session, ct) =>
        {
            await session.EnsureStartedAsync(ct);
            await session.StartTracingAsync(title);
            return new BrowserActionPayload(Message: "Started tracing.");
        }, cancellationToken);

    public Task<BrowserCommandResult> StopTracingAsync(McpServer server, string? fileName, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_stop_tracing", PageCaptureMode.None, async (session, ct) =>
        {
            await session.EnsureStartedAsync(ct);
            var path = await session.StopTracingAsync(fileName ?? "trace");
            return new BrowserActionPayload(
                Data: new { path },
                Message: "Stopped tracing.",
                Artifacts: [new ArtifactInfo { Name = Path.GetFileName(path), Path = path, MimeType = "application/zip" }]);
        }, cancellationToken);

    public Task<BrowserCommandResult> VerifyElementVisibleAsync(McpServer server, string target, string? tabId, CancellationToken cancellationToken)
        => ExecuteTargetAsync(server, "browser_verify_element_visible", target, tabId, PageCaptureMode.None, async (session, page, locator, ct) =>
        {
            var visible = await locator.IsVisibleAsync();
            return new BrowserActionPayload(Data: new { target, visible }, Message: visible ? "Element is visible." : "Element is not visible.");
        }, cancellationToken);

    public Task<BrowserCommandResult> VerifyTextVisibleAsync(McpServer server, string text, bool exact, string? tabId, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_verify_text_visible", PageCaptureMode.None, async (session, ct) =>
        {
            var page = await session.GetPageAsync(tabId, true, ct);
            var locator = page.GetByText(text, new() { Exact = exact }).First;
            var visible = await locator.IsVisibleAsync();
            return new BrowserActionPayload(Data: new { text, exact, visible }, Message: visible ? "Text is visible." : "Text is not visible.");
        }, cancellationToken);

    public Task<BrowserCommandResult> VerifyValueAsync(McpServer server, string target, string expectedValue, string? tabId, CancellationToken cancellationToken)
        => ExecuteTargetAsync(server, "browser_verify_value", target, tabId, PageCaptureMode.None, async (session, page, locator, ct) =>
        {
            var actual = await locator.InputValueAsync();
            var matches = string.Equals(actual, expectedValue, StringComparison.Ordinal);
            return new BrowserActionPayload(Data: new { expectedValue, actual, matches }, Message: matches ? "Value matches." : "Value does not match.");
        }, cancellationToken);

    public Task<BrowserCommandResult> VerifyListVisibleAsync(McpServer server, IReadOnlyList<string> items, string? target, string? tabId, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_verify_list_visible", PageCaptureMode.None, async (session, ct) =>
        {
            var page = await session.GetPageAsync(tabId, true, ct);
            IReadOnlyList<string> texts;
            if (string.IsNullOrWhiteSpace(target))
            {
                texts = await page.Locator("body").AllInnerTextsAsync();
            }
            else
            {
                texts = await session.ResolveTarget(page, target).AllInnerTextsAsync();
            }

            var haystack = string.Join("\n", texts);
            var missing = items.Where(item => !haystack.Contains(item, StringComparison.OrdinalIgnoreCase)).ToArray();
            return new BrowserActionPayload(Data: new { items, missing, allVisible = missing.Length == 0 }, Message: missing.Length == 0 ? "All list items are visible." : "Some list items were not found.");
        }, cancellationToken);

    public Task<BrowserCommandResult> MouseClickAsync(McpServer server, float x, float y, string? tabId, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_mouse_click_xy", PageCaptureMode.Snapshot, async (session, ct) =>
        {
            var page = await session.GetPageAsync(tabId, true, ct);
            await page.Mouse.ClickAsync(x, y);
            return new BrowserActionPayload(Data: new { x, y }, TabId: GetTabId(session, page), Message: "Clicked coordinates.");
        }, cancellationToken);

    public Task<BrowserCommandResult> MouseDownAsync(McpServer server, string? button, CancellationToken cancellationToken)
        => ExecuteMouseAsync(server, "browser_mouse_down", async page =>
        {
            await page.Mouse.DownAsync(new() { Button = ParseMouseButton(button) });
            return new { button = button ?? "left" };
        }, cancellationToken);

    public Task<BrowserCommandResult> MouseDragAsync(McpServer server, float fromX, float fromY, float toX, float toY, CancellationToken cancellationToken)
        => ExecuteMouseAsync(server, "browser_mouse_drag_xy", async page =>
        {
            await page.Mouse.MoveAsync(fromX, fromY);
            await page.Mouse.DownAsync();
            await page.Mouse.MoveAsync(toX, toY);
            await page.Mouse.UpAsync();
            return new { fromX, fromY, toX, toY };
        }, cancellationToken, PageCaptureMode.Snapshot);

    public Task<BrowserCommandResult> MouseMoveAsync(McpServer server, float x, float y, int steps, CancellationToken cancellationToken)
        => ExecuteMouseAsync(server, "browser_mouse_move_xy", async page =>
        {
            await page.Mouse.MoveAsync(x, y, new() { Steps = steps });
            return new { x, y, steps };
        }, cancellationToken);

    public Task<BrowserCommandResult> MouseUpAsync(McpServer server, string? button, CancellationToken cancellationToken)
        => ExecuteMouseAsync(server, "browser_mouse_up", async page =>
        {
            await page.Mouse.UpAsync(new() { Button = ParseMouseButton(button) });
            return new { button = button ?? "left" };
        }, cancellationToken, PageCaptureMode.Snapshot);

    public Task<BrowserCommandResult> MouseWheelAsync(McpServer server, float deltaX, float deltaY, CancellationToken cancellationToken)
        => ExecuteMouseAsync(server, "browser_mouse_wheel", async page =>
        {
            await page.Mouse.WheelAsync(deltaX, deltaY);
            return new { deltaX, deltaY };
        }, cancellationToken);

    public Task<BrowserCommandResult> RunCodeAsync(McpServer server, string? code, string? fileName, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_run_code", PageCaptureMode.Summary, async (session, ct) =>
        {
            if (!_optionsMonitor.CurrentValue.Features.EnableRunCode)
            {
                throw new InvalidOperationException("browser_run_code is disabled by configuration.");
            }

            return await _runCodeService.ExecuteAsync(session, server, code, fileName, ct);
        }, cancellationToken);

    public Task<BrowserCommandResult> RuntimeStatusAsync(McpServer server, string? browser, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_runtime_status", PageCaptureMode.None, async (session, ct) =>
        {
            await Task.CompletedTask;
            return new BrowserActionPayload(
                Data: _playwrightRuntimeService.GetStatus(browser),
                Message: "Loaded Playwright runtime status.");
        }, cancellationToken);

    public Task<BrowserCommandResult> InstallRuntimeAsync(McpServer server, string? browser, bool withDependencies, CancellationToken cancellationToken)
        => ExecuteAsync(server, "browser_install_runtime", PageCaptureMode.None, async (session, ct) =>
        {
            await Task.CompletedTask;
            return new BrowserActionPayload(
                Data: await _playwrightRuntimeService.InstallAsync(browser, withDependencies, ct),
                Message: "Playwright runtime installation completed.");
        }, cancellationToken);

    private Task<BrowserCommandResult> StorageGetAsync(McpServer server, string tool, string storageObject, string? key, string? tabId, CancellationToken cancellationToken)
        => ExecuteAsync(server, tool, PageCaptureMode.None, async (session, ct) =>
        {
            var page = await session.GetPageAsync(tabId, true, ct);
            if (string.IsNullOrWhiteSpace(key))
            {
                var result = await page.EvaluateAsync<JsonElement>($"() => Object.fromEntries(Object.entries({storageObject}))");
                return new BrowserActionPayload(Data: JsonSerializer.Deserialize<object>(result.GetRawText()), Message: $"Loaded {storageObject}.");
            }

            var single = await page.EvaluateAsync<string?>($"key => {storageObject}.getItem(key)", key);
            return new BrowserActionPayload(Data: new { key, value = single }, Message: $"Loaded {storageObject} item.");
        }, cancellationToken);

    private Task<BrowserCommandResult> StorageSetAsync(McpServer server, string tool, string storageObject, string key, string value, string? tabId, CancellationToken cancellationToken)
        => ExecuteAsync(server, tool, PageCaptureMode.None, async (session, ct) =>
        {
            var page = await session.GetPageAsync(tabId, true, ct);
            await page.EvaluateAsync($"args => {storageObject}.setItem(args.key, args.value)", new { key, value });
            return new BrowserActionPayload(Data: new { key, value }, Message: $"Updated {storageObject} item.");
        }, cancellationToken);

    private Task<BrowserCommandResult> StorageRemoveAsync(McpServer server, string tool, string storageObject, string key, string? tabId, CancellationToken cancellationToken)
        => ExecuteAsync(server, tool, PageCaptureMode.None, async (session, ct) =>
        {
            var page = await session.GetPageAsync(tabId, true, ct);
            await page.EvaluateAsync($"key => {storageObject}.removeItem(key)", key);
            return new BrowserActionPayload(Data: new { key }, Message: $"Removed {storageObject} item.");
        }, cancellationToken);

    private Task<BrowserCommandResult> StorageClearAsync(McpServer server, string tool, string storageObject, string? tabId, CancellationToken cancellationToken)
        => ExecuteAsync(server, tool, PageCaptureMode.None, async (session, ct) =>
        {
            var page = await session.GetPageAsync(tabId, true, ct);
            await page.EvaluateAsync($"() => {storageObject}.clear()");
            return new BrowserActionPayload(Message: $"Cleared {storageObject}.");
        }, cancellationToken);

    private Task<BrowserCommandResult> ExecuteMouseAsync(
        McpServer server,
        string tool,
        Func<IPage, Task<object>> action,
        CancellationToken cancellationToken,
        PageCaptureMode captureMode = PageCaptureMode.Summary)
        => ExecuteAsync(server, tool, captureMode, async (session, ct) =>
        {
            var page = await session.GetPageAsync(null, true, ct);
            var data = await action(page);
            return new BrowserActionPayload(Data: data, TabId: GetTabId(session, page));
        }, cancellationToken);

    private Task<BrowserCommandResult> ExecuteTargetAsync(
        McpServer server,
        string tool,
        string target,
        string? tabId,
        PageCaptureMode captureMode,
        Func<BrowserSession, IPage, ILocator, CancellationToken, Task<BrowserActionPayload>> action,
        CancellationToken cancellationToken)
        => ExecuteAsync(server, tool, captureMode, async (session, ct) =>
        {
            var page = await session.GetPageAsync(tabId, true, ct);
            var locator = session.ResolveTarget(page, target);
            return await action(session, page, locator, ct);
        }, cancellationToken);

    private async Task<BrowserCommandResult> ExecuteAsync(
        McpServer server,
        string tool,
        PageCaptureMode captureMode,
        Func<BrowserSession, CancellationToken, Task<BrowserActionPayload>> action,
        CancellationToken cancellationToken)
    {
        var sessionId = server.SessionId ?? throw new InvalidOperationException("MCP session ID is not available.");
        var session = await _sessionManager.GetOrCreateAsync(sessionId, cancellationToken);

        return await session.RunExclusiveAsync(async (lockedSession, ct) =>
        {
            var payload = await action(lockedSession, ct);
            PageState? page = captureMode switch
            {
                PageCaptureMode.None => null,
                PageCaptureMode.Summary => await lockedSession.BuildPageStateAsync(payload.TabId, includeSnapshot: false, ct),
                _ => await lockedSession.BuildPageStateAsync(payload.TabId, includeSnapshot: true, ct),
            };

            return new BrowserCommandResult
            {
                Tool = tool,
                SessionId = sessionId,
                Message = payload.Message,
                Page = page,
                Data = payload.Data,
                Artifacts = payload.Artifacts ?? [],
                Warnings = payload.Warnings ?? [],
            };
        }, cancellationToken);
    }

    private static string GetTabId(BrowserSession session, IPage page)
        => session.GetTabId(page);

    private static MouseButton ParseMouseButton(string? value)
        => value?.ToLowerInvariant() switch
        {
            "middle" => MouseButton.Middle,
            "right" => MouseButton.Right,
            _ => MouseButton.Left,
        };

    private static SameSiteAttribute? ParseSameSite(string? sameSite)
        => sameSite?.ToLowerInvariant() switch
        {
            "lax" => SameSiteAttribute.Lax,
            "strict" => SameSiteAttribute.Strict,
            "none" => SameSiteAttribute.None,
            _ => null,
        };

    private static IReadOnlyList<string> BuildLocatorSuggestions(JsonElement metadata)
    {
        var suggestions = new List<string>();
        if (metadata.TryGetProperty("testId", out var testId) && testId.ValueKind == JsonValueKind.String)
        {
            suggestions.Add($"[data-testid=\"{EscapeSelectorString(testId.GetString())}\"]");
        }

        if (metadata.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString()))
        {
            suggestions.Add($"#{EscapeSelectorString(id.GetString())}");
        }

        if (metadata.TryGetProperty("ariaLabel", out var ariaLabel) && ariaLabel.ValueKind == JsonValueKind.String)
        {
            suggestions.Add($"getByLabel(\"{EscapeStringLiteral(ariaLabel.GetString())}\")");
        }

        if (metadata.TryGetProperty("role", out var role) && role.ValueKind == JsonValueKind.String &&
            metadata.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
        {
            suggestions.Add($"getByRole(\"{role.GetString()}\", new() {{ Name = \"{EscapeStringLiteral(text.GetString())}\" }})");
        }

        if (metadata.TryGetProperty("placeholder", out var placeholder) && placeholder.ValueKind == JsonValueKind.String)
        {
            suggestions.Add($"getByPlaceholder(\"{EscapeStringLiteral(placeholder.GetString())}\")");
        }

        if (metadata.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
        {
            suggestions.Add($"[name=\"{EscapeSelectorString(name.GetString())}\"]");
        }

        return suggestions.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string EscapeSelectorString(string? value)
        => (value ?? string.Empty).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string EscapeStringLiteral(string? value)
        => (value ?? string.Empty).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}
