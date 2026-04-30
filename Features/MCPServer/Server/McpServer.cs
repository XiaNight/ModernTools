using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPServer.Protocol;

namespace MCPServer.Server;

/// <summary>
/// Core MCP server that communicates over stdin/stdout using the JSON-RPC 2.0
/// transport defined by the Model Context Protocol specification (2024-11-05).
///
/// All ModernTools HTTP-API routes are auto-discovered and exposed as MCP tools.
/// Dedicated tools for Python sandbox management are always registered regardless
/// of whether ModernTools is currently running.
/// </summary>
public sealed class McpServer
{
    // ── Configuration ─────────────────────────────────────────────────────────

    private const string DefaultModernToolsUrl = "http://127.0.0.1:2345";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy         = null,
        DefaultIgnoreCondition       = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive  = true,
        WriteIndented                = false,
    };

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly ModernToolsApiProxy _proxy;

    /// <summary>
    /// Route table: tool-name → parsed route, populated at initialise time.
    /// </summary>
    private readonly Dictionary<string, ParsedRoute> _routeTable = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Hard-coded tool definitions that are always present.</summary>
    private List<McpTool> _staticTools = [];

    private bool _initialized;

    // ── Construction ──────────────────────────────────────────────────────────

    public McpServer(string modernToolsUrl = DefaultModernToolsUrl)
    {
        _proxy = new ModernToolsApiProxy(modernToolsUrl);
        _staticTools = BuildStaticTools();
    }

    // ── Entry point ───────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the MCP server loop: reads newline-delimited JSON from stdin and writes
    /// responses to stdout until stdin is closed or cancellation is requested.
    /// </summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        // Use raw streams with UTF-8 to avoid platform line-ending translation.
        using var stdin  = new StreamReader(Console.OpenStandardInput(),  Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: false);
        using var stdout = new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8, leaveOpen: false) { AutoFlush = true };

        Log("ModernTools MCP Server started — waiting for client…");

        while (!ct.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await stdin.ReadLineAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line is null) break;           // stdin closed → exit gracefully
            if (string.IsNullOrWhiteSpace(line)) continue;

            var response = await HandleLineAsync(line, ct).ConfigureAwait(false);
            if (response is not null)
            {
                var json = JsonSerializer.Serialize(response, JsonOpts);
                await stdout.WriteLineAsync(json).ConfigureAwait(false);
            }
        }

        Log("MCP Server shut down.");
    }

    // ── Message dispatch ──────────────────────────────────────────────────────

    private async Task<JsonRpcResponse?> HandleLineAsync(string line, CancellationToken ct)
    {
        JsonRpcRequest? request;
        JsonElement? rawId = null;
        try
        {
            request = JsonSerializer.Deserialize<JsonRpcRequest>(line, JsonOpts);
            rawId   = request?.Id;
        }
        catch (JsonException ex)
        {
            return ErrorResponse(null, JsonRpcError.ParseError, $"Parse error: {ex.Message}");
        }

        if (request is null)
            return ErrorResponse(null, JsonRpcError.InvalidRequest, "Null request.");

        // Notifications have no id — handle them but send no response.
        if (request.IsNotification)
        {
            await HandleNotificationAsync(request, ct).ConfigureAwait(false);
            return null;
        }

        try
        {
            var result = await DispatchAsync(request, ct).ConfigureAwait(false);
            return new JsonRpcResponse { Id = rawId, Result = result };
        }
        catch (McpException mex)
        {
            return ErrorResponse(rawId, mex.Code, mex.Message);
        }
        catch (Exception ex)
        {
            return ErrorResponse(rawId, JsonRpcError.InternalError, ex.Message);
        }
    }

    private async Task<object?> DispatchAsync(JsonRpcRequest request, CancellationToken ct)
    {
        return request.Method switch
        {
            "initialize"   => await HandleInitializeAsync(request, ct).ConfigureAwait(false),
            "tools/list"   => await HandleToolsListAsync(ct).ConfigureAwait(false),
            "tools/call"   => await HandleToolsCallAsync(request, ct).ConfigureAwait(false),
            "ping"         => new { },
            _              => throw new McpException(JsonRpcError.MethodNotFound, $"Method not found: {request.Method}")
        };
    }

    private Task HandleNotificationAsync(JsonRpcRequest notification, CancellationToken ct)
    {
        // Currently no notification requires a side-effect response.
        Log($"Notification received: {notification.Method}");
        return Task.CompletedTask;
    }

    // ── Handler: initialize ───────────────────────────────────────────────────

    private async Task<InitializeResult> HandleInitializeAsync(JsonRpcRequest request, CancellationToken ct)
    {
        // On initialize, attempt to discover routes from the running ModernTools instance.
        await RefreshRoutesAsync(ct).ConfigureAwait(false);
        _initialized = true;

        return new InitializeResult
        {
            ProtocolVersion = McpConstants.ProtocolVersion,
            Capabilities = new ServerCapabilities
            {
                Tools = new ToolsCapability { ListChanged = false }
            },
            ServerInfo = new Implementation
            {
                Name    = McpConstants.ServerName,
                Version = McpConstants.ServerVersion,
            }
        };
    }

    // ── Handler: tools/list ───────────────────────────────────────────────────

    private async Task<ToolsListResult> HandleToolsListAsync(CancellationToken ct)
    {
        // Refresh routes in case ModernTools was started after this server.
        if (!_initialized || _routeTable.Count == 0)
            await RefreshRoutesAsync(ct).ConfigureAwait(false);

        var tools = new List<McpTool>(_staticTools);
        tools.AddRange(_routeTable.Values.Select(r => new McpTool
        {
            Name        = r.ToolName,
            Description = r.Description,
            InputSchema = r.Schema,
        }));

        return new ToolsListResult { Tools = tools };
    }

    // ── Handler: tools/call ───────────────────────────────────────────────────

    private async Task<ToolCallResult> HandleToolsCallAsync(JsonRpcRequest request, CancellationToken ct)
    {
        var p = Deserialize<ToolCallParams>(request.Params)
                ?? throw new McpException(JsonRpcError.InvalidParams, "Missing tool call parameters.");

        if (string.IsNullOrWhiteSpace(p.Name))
            throw new McpException(JsonRpcError.InvalidParams, "Tool name is required.");

        // Try static (Python sandbox) tools first.
        var staticResult = await TryInvokeStaticToolAsync(p.Name, p.Arguments, ct).ConfigureAwait(false);
        if (staticResult is not null)
            return staticResult;

        // Then try dynamically discovered API proxy tools.
        if (_routeTable.TryGetValue(p.Name, out var route))
            return await _proxy.InvokeAsync(route, p.Arguments, ct).ConfigureAwait(false);

        // Unknown tool — refresh routes once and retry.
        await RefreshRoutesAsync(ct).ConfigureAwait(false);
        if (_routeTable.TryGetValue(p.Name, out route))
            return await _proxy.InvokeAsync(route, p.Arguments, ct).ConfigureAwait(false);

        throw new McpException(JsonRpcError.MethodNotFound, $"Unknown tool: {p.Name}");
    }

    // ── Static (Python sandbox) tools ─────────────────────────────────────────

    private static List<McpTool> BuildStaticTools() =>
    [
        new McpTool
        {
            Name        = "python_setup",
            Description = "Download and install the embedded Python 3.11 runtime used by ModernTools. " +
                          "Safe to call multiple times; returns immediately if Python is already available.",
            InputSchema = new JsonSchema { Type = "object" }
        },
        new McpTool
        {
            Name        = "python_run",
            Description = "Execute Python code using the embedded Python runtime. " +
                          "The execution is asynchronous; use python_read to retrieve output.",
            InputSchema = new JsonSchema
            {
                Type = "object",
                Properties = new Dictionary<string, JsonSchemaProperty>
                {
                    ["code"] = new() { Type = "string", Description = "Python source code to execute." },
                    ["name"] = new() { Type = "string", Description = "Optional execution name (default: 'default')." }
                },
                Required = ["code"]
            }
        },
        new McpTool
        {
            Name        = "python_status",
            Description = "Get the status of a named Python execution (state and exit code).",
            InputSchema = new JsonSchema
            {
                Type = "object",
                Properties = new Dictionary<string, JsonSchemaProperty>
                {
                    ["name"] = new() { Type = "string", Description = "Execution name (default: 'default')." }
                }
            }
        },
        new McpTool
        {
            Name        = "python_read",
            Description = "Read stdout/stderr output produced by a named Python execution since the last read.",
            InputSchema = new JsonSchema
            {
                Type = "object",
                Properties = new Dictionary<string, JsonSchemaProperty>
                {
                    ["name"] = new() { Type = "string", Description = "Execution name (default: 'default')." }
                }
            }
        },
        new McpTool
        {
            Name        = "python_write",
            Description = "Send a line of text to the stdin of a named Python execution (for interactive scripts).",
            InputSchema = new JsonSchema
            {
                Type = "object",
                Properties = new Dictionary<string, JsonSchemaProperty>
                {
                    ["input"] = new() { Type = "string", Description = "Text to send to stdin." },
                    ["name"]  = new() { Type = "string", Description = "Execution name (default: 'default')." }
                },
                Required = ["input"]
            }
        },
        new McpTool
        {
            Name        = "python_close",
            Description = "Terminate and clean up a named Python execution.",
            InputSchema = new JsonSchema
            {
                Type = "object",
                Properties = new Dictionary<string, JsonSchemaProperty>
                {
                    ["name"] = new() { Type = "string", Description = "Execution name (default: 'default')." }
                }
            }
        },
        new McpTool
        {
            Name        = "moderntools_status",
            Description = "Check whether the ModernTools application is running and its HTTP API is reachable.",
            InputSchema = new JsonSchema { Type = "object" }
        },
        new McpTool
        {
            Name        = "moderntools_list_routes",
            Description = "List all HTTP API routes currently registered in the running ModernTools instance.",
            InputSchema = new JsonSchema { Type = "object" }
        },
    ];

    private async Task<ToolCallResult?> TryInvokeStaticToolAsync(
        string toolName, JsonElement? arguments, CancellationToken ct)
    {
        var args = arguments is { } je && je.ValueKind == JsonValueKind.Object
            ? je
            : (JsonElement?)null;

        switch (toolName)
        {
            case "python_setup":
                return await ProxyPostAsync("/Base/Services/PythonSandboxService/setup", null, ct);

            case "python_run":
            {
                var code = GetString(args, "code");
                var name = GetString(args, "name") ?? "default";
                if (string.IsNullOrWhiteSpace(code))
                    return ErrorTool("'code' parameter is required.");
                return await ProxyPostAsync("/Base/Services/PythonSandboxService/run",
                    new { name, body = code }, ct);
            }

            case "python_status":
            {
                var name = GetString(args, "name") ?? "default";
                return await ProxyGetAsync($"/Base/Services/PythonSandboxService/status?name={Uri.EscapeDataString(name)}", ct);
            }

            case "python_read":
            {
                var name = GetString(args, "name") ?? "default";
                return await ProxyGetAsync($"/Base/Services/PythonSandboxService/read?name={Uri.EscapeDataString(name)}", ct);
            }

            case "python_write":
            {
                var input = GetString(args, "input");
                var name  = GetString(args, "name") ?? "default";
                if (input is null)
                    return ErrorTool("'input' parameter is required.");
                return await ProxyPostAsync("/Base/Services/PythonSandboxService/write",
                    new { name, input }, ct);
            }

            case "python_close":
            {
                var name = GetString(args, "name") ?? "default";
                return await ProxyPostAsync("/Base/Services/PythonSandboxService/close",
                    new { name }, ct);
            }

            case "moderntools_status":
            {
                var available = await _proxy.IsAvailableAsync(ct).ConfigureAwait(false);
                var text = available
                    ? "ModernTools is running and the HTTP API is reachable."
                    : "ModernTools is NOT running or the HTTP API is unavailable (expected on http://127.0.0.1:2345).";
                return new ToolCallResult
                {
                    Content  = [new ContentItem { Type = "text", Text = text }],
                    IsError  = !available,
                };
            }

            case "moderntools_list_routes":
            {
                await RefreshRoutesAsync(ct).ConfigureAwait(false);
                var lines = _routeTable.Values
                    .Select(r => $"{r.Verb,-6} {r.Path}")
                    .OrderBy(s => s)
                    .ToList();
                var text = lines.Count > 0
                    ? string.Join('\n', lines)
                    : "No routes discovered (is ModernTools running?).";
                return new ToolCallResult
                {
                    Content = [new ContentItem { Type = "text", Text = text }]
                };
            }

            default:
                return null;
        }
    }

    // ── HTTP proxy helpers ────────────────────────────────────────────────────

    private async Task<ToolCallResult> ProxyGetAsync(string relativeUrl, CancellationToken ct)
    {
        var route = new ParsedRoute("GET", relativeUrl, "", "", new JsonSchema(), []);
        return await _proxy.InvokeAsync(route, null, ct).ConfigureAwait(false);
    }

    private async Task<ToolCallResult> ProxyPostAsync(string relativePath, object? body, CancellationToken ct)
    {
        JsonElement? argsEl = body is not null
            ? JsonSerializer.SerializeToElement(body, JsonOpts)
            : null;
        var route = new ParsedRoute("POST", relativePath, "", "", new JsonSchema(), []);
        return await _proxy.InvokeAsync(route, argsEl, ct).ConfigureAwait(false);
    }

    // ── Route table management ────────────────────────────────────────────────

    private async Task RefreshRoutesAsync(CancellationToken ct)
    {
        var routes = await _proxy.DiscoverRoutesAsync(ct).ConfigureAwait(false);
        _routeTable.Clear();
        foreach (var r in routes)
        {
            // Avoid overwriting static tool names with dynamic ones.
            if (_staticTools.Any(t => t.Name == r.ToolName)) continue;

            // Resolve name collisions uniquely by appending the HTTP verb then a numeric counter.
            var finalName = r.ToolName;
            if (_routeTable.ContainsKey(finalName))
            {
                finalName = $"{r.ToolName}_{r.Verb.ToLowerInvariant()}";
                int suffix = 2;
                while (_routeTable.ContainsKey(finalName))
                    finalName = $"{r.ToolName}_{r.Verb.ToLowerInvariant()}_{suffix++}";
            }

            _routeTable[finalName] = r;
        }

        Log($"Route table refreshed: {_routeTable.Count} dynamic tool(s) registered.");
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    private static T? Deserialize<T>(JsonElement? element) where T : class
    {
        if (element is null) return null;
        return JsonSerializer.Deserialize<T>(element.Value.GetRawText(), JsonOpts);
    }

    private static string? GetString(JsonElement? el, string key)
    {
        if (el is null || el.Value.ValueKind != JsonValueKind.Object) return null;
        return el.Value.TryGetProperty(key, out var v) ? v.GetString() : null;
    }

    private static ToolCallResult ErrorTool(string message) => new()
    {
        Content = [new ContentItem { Type = "text", Text = message }],
        IsError = true,
    };

    private static JsonRpcResponse ErrorResponse(JsonElement? id, int code, string message) => new()
    {
        Id    = id,
        Error = new JsonRpcError { Code = code, Message = message },
    };

    private static void Log(string message) =>
        Console.Error.WriteLine($"[MCPServer] {message}");
}

/// <summary>Thrown by handler methods to produce a JSON-RPC error response.</summary>
public sealed class McpException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}
