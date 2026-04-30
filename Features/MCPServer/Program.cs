using MCPServer.Server;

// ──────────────────────────────────────────────────────────────────────────────
// ModernTools MCP Server – entry point
//
// This process communicates over stdin/stdout using the Model Context Protocol
// (MCP) specification 2024-11-05, allowing Claude Code and other MCP clients to
// interact with all ModernTools APIs and the embedded Python execution sandbox.
//
// Usage:
//   ModernToolsMCPServer.exe [--url http://127.0.0.1:2345]
//
// Claude Code configuration (~/.config/claude/settings.json):
//   {
//     "mcpServers": {
//       "moderntools": {
//         "command": "C:\\path\\to\\ModernToolsMCPServer.exe"
//       }
//     }
//   }
// ──────────────────────────────────────────────────────────────────────────────

// Parse optional --url argument
var modernToolsUrl = "http://127.0.0.1:2345";
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i].Equals("--url", StringComparison.OrdinalIgnoreCase))
    {
        modernToolsUrl = args[i + 1];
        break;
    }
}

using var cts = new CancellationTokenSource();

// Graceful shutdown on Ctrl+C or SIGTERM
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

var server = new McpServer(modernToolsUrl);
await server.RunAsync(cts.Token);
