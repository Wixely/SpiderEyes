# PlaywrightMCPSharp

`PlaywrightMCPSharp` is a .NET-native Playwright MCP server for browser automation over Streamable HTTP or stdio. It does not require Node.js or Python to run the server.

The application ships with the Playwright .NET library, but it does not bundle the Playwright browser binaries by default. On a fresh machine, the host still needs the matching Playwright browser runtime installed once unless you pre-package those browser binaries yourself. PlaywrightMCPSharp itself still does not require a separate Node.js or Python install, because it uses Playwright for .NET rather than the Node.js Playwright package or a Python wrapper.

## What it does

- Hosts a stateful Streamable HTTP MCP endpoint at `http://127.0.0.1:5704/mcp` by default.
- Can also run as a single-session stdio MCP server for local clients.
- Launches one isolated Playwright browser session per MCP session.
- Exposes `browser_*` tools for navigation, snapshots, forms, storage, routing, tracing, verification, and coordinate-based mouse control.
- Uses Playwright AI ARIA snapshots so LLMs can work from structured page state instead of pixel screenshots.
- Supports a C#-based `browser_run_code` tool with access to `page`, `context`, `browser`, `playwright`, `sessionId`, and `ct`.
- Exposes MCP tools to inspect and install the required Playwright browser runtime on the host.

## Requirements

- .NET SDK 10.0+
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
powershell .\src\PlaywrightMCPSharp.Server\bin\Debug\net10.0\playwright.ps1 install chromium
```

3. Run the server over HTTP:

```powershell
dotnet run --project .\src\PlaywrightMCPSharp.Server
```

4. Check health:

```powershell
curl http://127.0.0.1:5704/healthz
```

For stdio instead of HTTP:

```powershell
dotnet run --project .\src\PlaywrightMCPSharp.Server -- --stdio
```

For MCP client configs, prefer launching the built server directly after `dotnet build`:

```powershell
dotnet .\src\PlaywrightMCPSharp.Server\bin\Debug\net10.0\PlaywrightMCPSharp.Server.dll --stdio
```

## Windows Service

For long-running local HTTP hosting on Windows, PlaywrightMCPSharp can run as a Windows Service.

1. Publish the server:

```powershell
dotnet publish .\src\PlaywrightMCPSharp.Server -c Release -r win-x64 --self-contained false
```

2. Create the service pointing at the published executable:

```powershell
sc.exe create PlaywrightMCPSharp binPath= "C:\ABSOLUTE\PATH\TO\PlaywrightMCPSharp.exe" start= auto
```

3. Start it:

```powershell
sc.exe start PlaywrightMCPSharp
```

The service uses HTTP mode and the same `PlaywrightMCPSharp.json` / environment-variable configuration as the normal app. If you need a different port, route, or security mode, set the corresponding `PLAYWRIGHTMCP_PlaywrightMCPSharp__...` environment variables for the service.

## Docker

The repo includes a basic `Dockerfile` for HTTP-mode hosting:

```powershell
docker build -t playwrightmcpsharp .
docker run --rm -p 5704:5704 \
  -e PLAYWRIGHTMCP_PlaywrightMCPSharp__Server__Host=0.0.0.0 \
  -e PLAYWRIGHTMCP_PlaywrightMCPSharp__Server__Port=5704 \
  -e PLAYWRIGHTMCP_PlaywrightMCPSharp__Server__Password=change-me \
  -e PLAYWRIGHTMCP_PlaywrightMCPSharp__Browser__Headless=true \
  playwrightmcpsharp
```

By default the container listens on `http://0.0.0.0:5704/mcp`.

Notes:

- The container image is set up for the server itself, not for preinstalled Playwright browser runtimes.
- After the container starts, you can call `browser_runtime_status` and `browser_install_runtime` if you want to install the configured browser runtime from inside the MCP session.
If an MCP client is already connected, it can also do the browser install over MCP by calling `browser_install_runtime`.

## Configuration

Settings live under `PlaywrightMCPSharp` in `PlaywrightMCPSharp.json` and can be overridden with environment variables such as `PLAYWRIGHTMCP_PlaywrightMCPSharp__Server__Port=9000`. Environment variables win over JSON; use `__` for nested keys, numeric indexes for arrays such as `PLAYWRIGHTMCP_PlaywrightMCPSharp__Server__AllowedHosts__0=*`, and `true`/`false` for booleans.

`PlaywrightMCPSharp:Server:Password` is blank by default. Set it to require an MCP endpoint password; clients may send `Authorization: Bearer <password>`, the Basic auth password, or `X-MCP-Password`. This password gate is separate from the security mode below.

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
  "PlaywrightMCPSharp": {
    "Server": {
      "Transport": "Http",
      "Host": "0.0.0.0",
      "Port": 5704,
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
  "PlaywrightMCPSharp": {
    "Server": {
      "Transport": "Http",
      "Host": "0.0.0.0",
      "Port": 5704,
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
http://127.0.0.1:5704/mcp
```

For stdio clients, launch the server command directly:

```text
dotnet run --project .\src\PlaywrightMCPSharp.Server -- --stdio
```

For desktop/editor MCP clients that store a command plus args, using the built `dll` is usually more reliable than `dotnet run`:

```json
{
  "command": "dotnet",
  "args": [
    "C:\\path\\to\\PlaywrightMCPSharp\\src\\PlaywrightMCPSharp.Server\\bin\\Debug\\net10.0\\PlaywrightMCPSharp.Server.dll",
    "--stdio"
  ]
}
```

If the client supports roots, PlaywrightMCPSharp requests them and restricts file access to those roots plus the per-session artifact directory. If the client does not support roots, PlaywrightMCPSharp falls back to the current working directory.

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

## Security notes

- AI ARIA snapshots are page-derived input and should be treated as untrusted. Prompt injection can be present in page text and accessibility names.
- `browser_run_code` executes arbitrary C# against a live browser session. Disable it with `PLAYWRIGHTMCP_PlaywrightMCPSharp__Features__EnableRunCode=false` if that is too much power for your client.
- `RemoteNoAuth` is intentionally dangerous and should not be exposed on the public internet.
- File operations are root-scoped by default. `AllowUnrestrictedFileAccess=true` removes that guardrail.
- `browser_install_runtime` downloads executables onto the host machine. `withDependencies=true` may also attempt OS-level dependency installation when Playwright supports it.
