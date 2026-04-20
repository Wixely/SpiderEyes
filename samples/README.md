# MCP Config Samples

These samples show how to connect local SpiderEyes instances to common MCP clients.

Assumptions:

- You are running on Windows.
- This repo has already been built with `dotnet build SpiderEyes.sln -c Debug`.
- Replace `C:\\ABSOLUTE\\PATH\\TO\\SpiderEyes` in stdio samples with your actual local repo path.
- HTTP samples assume SpiderEyes is already running at `http://127.0.0.1:8931/mcp`.

Startup mode:

- Use stdio samples when you want the client to launch SpiderEyes itself.
- Use HTTP samples when you want to run SpiderEyes separately with `dotnet run --project .\\src\\SpiderEyes.Server`.
- For stdio, the underlying command is:

```powershell
dotnet C:\ABSOLUTE\PATH\TO\SpiderEyes\src\SpiderEyes.Server\bin\Debug\net8.0\SpiderEyes.Server.dll --stdio
```

Sample files:

- `opencode.stdio.json`
  Target: `opencode.json` in the project root or `~/.config/opencode/opencode.json`
- `opencode.http.json`
  Target: `opencode.json` in the project root or `~/.config/opencode/opencode.json`
- `cline.stdio.json`
  Target: `cline_mcp_settings.json`
- `cline.http.json`
  Target: `cline_mcp_settings.json`
- `copilot-vscode.stdio.json`
  Target: `.vscode/mcp.json` or the VS Code user `mcp.json`
- `copilot-vscode.http.json`
  Target: `.vscode/mcp.json` or the VS Code user `mcp.json`
- `copilot-cli.stdio.json`
  Target: `~/.copilot/mcp-config.json`
- `copilot-cli.http.json`
  Target: `~/.copilot/mcp-config.json`
- `claude-code.stdio.json`
  Target: `.mcp.json` in the project root, or add the same `mcpServers` entry with `claude mcp add`
- `claude-code.http.json`
  Target: `.mcp.json` in the project root, or add the same `mcpServers` entry with `claude mcp add`
- `codex.stdio.toml`
  Target: `~/.codex/config.toml`
- `codex.http.toml`
  Target: `~/.codex/config.toml`

Config shape by client:

- OpenCode uses `mcp`.
- Cline uses `mcpServers`.
- GitHub Copilot in VS Code uses `servers`.
- GitHub Copilot CLI uses `mcpServers`.
- Claude Code uses `mcpServers` in `.mcp.json`.
- Codex uses TOML under `mcp_servers`.

References:

- OpenCode config and MCP docs: `https://opencode.ai/docs/config`, `https://opencode.ai/docs/mcp-servers`
- Cline MCP docs: `https://docs.cline.bot/mcp/adding-and-configuring-servers`, `https://docs.cline.bot/mcp/connecting-to-a-remote-server`
- VS Code MCP config reference: `https://code.visualstudio.com/docs/copilot/reference/mcp-configuration`
- GitHub Copilot CLI MCP docs: `https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-mcp-servers`
- Claude Code MCP docs: `https://docs.anthropic.com/en/docs/claude-code/mcp`
- OpenAI Docs MCP / Codex MCP docs: `https://developers.openai.com/learn/docs-mcp`
