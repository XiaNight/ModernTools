# ModernToolset MCP Server Setup

The `mcp__moderntoolset__*` tools are not available. This means the ModernToolset MCP server has not been registered with Claude Code.

Ask the developer: **"Where is your `ModernToolsetMCPServer.exe`? Please provide the full path and I'll add it for you."**

Once the path is provided, register it using one of the two options below.

---

## Option A — Claude Code CLI

Use this if `claude` is available in the terminal:

```bash
claude mcp add moderntoolset "C:\path\to\ModernToolsetMCPServer.exe"
```

---

## Option B — Edit `~/.claude.json` directly

Use this as a fallback if the CLI is not installed.

Open `C:\Users\<username>\.claude.json` (create it if it doesn't exist) and add the `mcpServers` entry:

```json
{
  "mcpServers": {
    "moderntoolset": {
      "command": "C:\\path\\to\\ModernToolsetMCPServer.exe",
      "args": [],
      "type": "stdio"
    }
  }
}
```

If the file already has other content, merge the `mcpServers` key in — do not overwrite the whole file.

---

After either option, tell the developer: **"Added. Please restart Claude Code for the MCP server to take effect, then retry."**

Do **not** fall back to raw `curl` or manual HTTP calls while the server is unregistered.
