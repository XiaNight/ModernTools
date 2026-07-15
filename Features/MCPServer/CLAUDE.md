# MCPServer

**Exception to the plugin pattern.** This is a standalone Model Context Protocol server (`OutputType=Exe`, no `EnableDynamicLoading`), not a `PageBase` plugin. It exposes the app's capabilities over MCP by proxying the app's HTTP API rather than hosting UI.

Key files: `Program.cs`, `Server/McpServer.cs`, `ModernToolsetApiProxy.cs`, and the JSON-RPC / MCP model types. It talks to the `Base` HTTP API (`Base/Infrastructure/API/`), so keep it in sync when that API changes. Not referenced by the shell in Debug; it is published separately.
