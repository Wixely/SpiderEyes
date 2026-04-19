# SpiderEyes

`SpiderEyes` is a Playwright MCP server written in C# for development-oriented browsing by LLM clients. It supports both Streamable HTTP and stdio transport using the official MCP C# SDK, Playwright for .NET, and Roslyn scripting for the optional `browser_run_code` tool.

The application ships with the Playwright .NET library, but it does not bundle the Playwright browser binaries by default. On a fresh machine, the host still needs the matching Playwright browser runtime installed once unless you pre-package those browser binaries yourself. A separate Node.js install is not required for SpiderEyes itself because it uses Playwright for .NET rather than the Node.js Playwright package.

## What it does

- Hosts a stateful Streamable HTTP MCP endpoint at `http://127.0.0.1:8931/mcp` by default.
- Can also run as a single-session stdio MCP server for local spawned-client workflows.
- Launches one isolated Playwright browser session per MCP session.
- Exposes an official-like `browser_*` tool surface for navigation, snapshots, forms, storage, routing, tracing, verification, and coordinate-based mouse control.
- Uses Playwright AI ARIA snapshots so LLMs can work from structured page state instead of pixel screenshots.
- Supports a C#-based `browser_run_code` tool with access to `page`, `context`, `browser`, `playwright`, `sessionId`, and `ct`.
- Exposes MCP tools to inspect and install the required Playwright browser runtime on the host.

## Repo layout

- `src/SpiderEyes.Server`: MCP server host, Playwright session runtime, tool implementations
- `tests/SpiderEyes.Server.Tests`: unit tests plus end-to-end MCP integration tests
- `Directory.Packages.props`: central package versions
- `NuGet.config`: repo-local NuGet source pinning to `nuget.org`

## Requirements

- .NET SDK 8.0+
- Windows, macOS, or Linux supported by Playwright for .NET
- One-time Playwright browser install after restore/build

## Getting started

1. Restore and build:

```powershell
dotnet restore
dotnet build
```

2. Install the Playwright browser used by the server:

```powershell
powershell .\src\SpiderEyes.Server\bin\Debug\net8.0\playwright.ps1 install chromium
```

3. Run the server over HTTP:

```powershell
dotnet run --project .\src\SpiderEyes.Server
```

4. Check health:

```powershell
curl http://127.0.0.1:8931/healthz
```

For stdio instead of HTTP:

```powershell
dotnet run --project .\src\SpiderEyes.Server -- --stdio
```

For MCP client configs, prefer launching the built server directly after `dotnet build`:

```powershell
dotnet .\src\SpiderEyes.Server\bin\Debug\net8.0\SpiderEyes.Server.dll --stdio
```

If an MCP client is already connected, it can also do the browser install over MCP by calling `browser_install_runtime`.

## VS Code

The repo includes a `.vscode` workspace setup so you can build, run, test, and debug directly in VS Code.

- `Ctrl+Shift+B`: runs the default `build` task
- `Terminal > Run Task > run`: starts the MCP server in Development mode
- `Terminal > Run Task > run-stdio`: starts the MCP server in stdio mode
- `Terminal > Run Task > test`: runs the test suite
- `Terminal > Run Task > install-playwright-chromium`: installs the Playwright Chromium runtime
- `F5`: launches `Debug SpiderEyes Server`

The repo includes both `Debug SpiderEyes Server` for HTTP and `Debug SpiderEyes Server (stdio)` for local spawned-MCP debugging.

## Configuration

Settings live under `SpiderEyes` in `appsettings.json` and can be overridden with environment variables such as `SpiderEyes__Server__Port=9000`.

### Transport modes

- `Http`: Streamable HTTP on `/mcp` plus `/healthz`
- `Stdio`: single-session stdio transport over stdin/stdout

You can also override transport from the command line with `--stdio` or `--http`.

### Security modes

- `LocalOnly`: only loopback clients may connect
- `RemoteBearer`: remote access requires `Authorization: Bearer <token>`
- `RemoteNoAuth`: remote unauthenticated access; this is blocked unless `DangerousAllowRemoteNoAuth=true`

### Example: remote bearer mode

```json
{
  "SpiderEyes": {
    "Server": {
      "Transport": "Http",
      "Host": "0.0.0.0",
      "Port": 8931,
      "Route": "/mcp",
      "AllowedHosts": [ "*" ],
      "AllowedOrigins": [ "https://your-client.example" ]
    },
    "Security": {
      "Mode": "RemoteBearer",
      "BearerToken": "replace-me"
    },
    "Browser": {
      "Headless": true
    }
  }
}
```

### Example: remote no-auth mode

```json
{
  "SpiderEyes": {
    "Server": {
      "Transport": "Http",
      "Host": "0.0.0.0",
      "Port": 8931,
      "AllowedHosts": [ "*" ]
    },
    "Security": {
      "Mode": "RemoteNoAuth",
      "DangerousAllowRemoteNoAuth": true
    }
  }
}
```

This mode is intentionally loud and unsafe. Use it only on a trusted network.

## MCP client connection

For Streamable HTTP clients, point them at:

```text
http://127.0.0.1:8931/mcp
```

For stdio clients, launch the server command directly:

```text
dotnet run --project .\src\SpiderEyes.Server -- --stdio
```

For desktop/editor MCP clients that store a command plus args, using the built `dll` is usually more reliable than `dotnet run`:

```json
{
  "command": "dotnet",
  "args": [
    "C:\\path\\to\\SpiderEyes\\src\\SpiderEyes.Server\\bin\\Debug\\net8.0\\SpiderEyes.Server.dll",
    "--stdio"
  ]
}
```

If the client supports roots, SpiderEyes requests them and restricts file access to those roots plus the per-session artifact directory. If the client does not support roots, SpiderEyes falls back to the current working directory.

## Tool groups

### Core browsing

- `browser_navigate`
- `browser_navigate_back`
- `browser_close`
- `browser_tabs`
- `browser_snapshot`
- `browser_take_screenshot`
- `browser_click`
- `browser_hover`
- `browser_drag`
- `browser_type`
- `browser_fill_form`
- `browser_select_option`
- `browser_press_key`
- `browser_wait_for`
- `browser_resize`
- `browser_handle_dialog`
- `browser_file_upload`
- `browser_console_messages`
- `browser_network_requests`
- `browser_evaluate`
- `browser_get_config`

### Network and storage

- `browser_network_state_set`
- `browser_route`
- `browser_route_list`
- `browser_unroute`
- `browser_cookie_get`
- `browser_cookie_set`
- `browser_cookie_delete`
- `browser_cookie_clear`
- `browser_localstorage_get`
- `browser_localstorage_set`
- `browser_localstorage_remove`
- `browser_localstorage_clear`
- `browser_sessionstorage_get`
- `browser_sessionstorage_set`
- `browser_sessionstorage_remove`
- `browser_sessionstorage_clear`
- `browser_storage_state`
- `browser_set_storage_state`

### Devtools, assertions, and vision

- `browser_highlight`
- `browser_hide_highlight`
- `browser_generate_locator`
- `browser_runtime_status`
- `browser_install_runtime`
- `browser_start_tracing`
- `browser_stop_tracing`
- `browser_verify_element_visible`
- `browser_verify_text_visible`
- `browser_verify_value`
- `browser_verify_list_visible`
- `browser_mouse_click_xy`
- `browser_mouse_down`
- `browser_mouse_drag_xy`
- `browser_mouse_move_xy`
- `browser_mouse_up`
- `browser_mouse_wheel`
- `browser_run_code`

## `browser_run_code`

`browser_run_code` accepts either:

- `code`: inline C# script
- `fileName`: script file path under an MCP root or the workspace

Available globals:

- `page`
- `context`
- `browser`
- `playwright`
- `sessionId`
- `ct`
- `SaveTextAsync(...)`
- `SaveJsonAsync(...)`

Example:

```csharp
var title = await page.TitleAsync();
return new { title, url = page.Url };
```

## Manual smoke test

1. Start the server.
2. Connect an MCP Inspector or another Streamable HTTP MCP client to `http://127.0.0.1:8931/mcp`, or launch the same server with `--stdio` from a stdio-capable client.
3. Call `browser_navigate` with `https://example.com`.
4. Call `browser_snapshot` and confirm the returned snapshot contains `Example Domain`.
5. Call `browser_take_screenshot` and confirm an image is written under `artifacts/<session-id>/`.

If the machine has never downloaded Playwright browsers, call `browser_runtime_status` first and then `browser_install_runtime` from the MCP client.

## Tests

Run the full suite:

```powershell
dotnet test
```

The integration tests require Playwright browsers to be installed first.

## Security notes

- AI ARIA snapshots are page-derived input and should be treated as untrusted. Prompt injection can be present in page text and accessibility names.
- `browser_run_code` executes arbitrary C# against a live browser session. Disable it with `SpiderEyes__Features__EnableRunCode=false` if that is too much power for your client.
- `RemoteNoAuth` is intentionally dangerous and should not be exposed on the public internet.
- File operations are root-scoped by default. `AllowUnrestrictedFileAccess=true` removes that guardrail.
- `browser_install_runtime` downloads executables onto the host machine. `withDependencies=true` may also attempt OS-level dependency installation when Playwright supports it.

## Current scope

Included in v1:

- Streamable HTTP and stdio
- Chromium/Firefox/WebKit launch support
- Per-session isolated browser processes
- AI snapshots, routing, storage, tracing, assertions, and coordinate tools

Not included in v1:

- PDF tooling
- video start/stop
- browser extension attach or CDP attach
- raw JavaScript Playwright snippets
