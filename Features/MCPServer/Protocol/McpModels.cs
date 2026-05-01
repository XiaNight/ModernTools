using System.Text.Json.Serialization;

namespace MCPServer.Protocol;

// ──────────────────────────────────────────────────────────────────────────────
// initialize
// ──────────────────────────────────────────────────────────────────────────────

public sealed class InitializeParams
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = string.Empty;

    [JsonPropertyName("capabilities")]
    public ClientCapabilities Capabilities { get; set; } = new();

    [JsonPropertyName("clientInfo")]
    public Implementation? ClientInfo { get; set; }
}

public sealed class ClientCapabilities
{
    // Intentionally left sparse; expand as needed.
}

public sealed class InitializeResult
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = McpConstants.ProtocolVersion;

    [JsonPropertyName("capabilities")]
    public ServerCapabilities Capabilities { get; set; } = new();

    [JsonPropertyName("serverInfo")]
    public Implementation ServerInfo { get; set; } = new();
}

public sealed class ServerCapabilities
{
    [JsonPropertyName("tools")]
    public ToolsCapability? Tools { get; set; }
}

public sealed class ToolsCapability
{
    /// <summary>When true the server may emit tools/list_changed notifications.</summary>
    [JsonPropertyName("listChanged")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ListChanged { get; set; }
}

public sealed class Implementation
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}

// ──────────────────────────────────────────────────────────────────────────────
// tools/list
// ──────────────────────────────────────────────────────────────────────────────

public sealed class ToolsListResult
{
    [JsonPropertyName("tools")]
    public List<McpTool> Tools { get; set; } = [];

    [JsonPropertyName("nextCursor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextCursor { get; set; }
}

public sealed class McpTool
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("inputSchema")]
    public JsonSchema InputSchema { get; set; } = new();
}

// ──────────────────────────────────────────────────────────────────────────────
// tools/call
// ──────────────────────────────────────────────────────────────────────────────

public sealed class ToolCallParams
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public System.Text.Json.JsonElement? Arguments { get; set; }
}

public sealed class ToolCallResult
{
    [JsonPropertyName("content")]
    public List<ContentItem> Content { get; set; } = [];

    [JsonPropertyName("isError")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsError { get; set; }
}

public sealed class ContentItem
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }
}

// ──────────────────────────────────────────────────────────────────────────────
// Shared helpers
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>Minimal JSON Schema representation used in tool definitions.</summary>
public sealed class JsonSchema
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    [JsonPropertyName("properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, JsonSchemaProperty>? Properties { get; set; }

    [JsonPropertyName("required")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Required { get; set; }
}

public sealed class JsonSchemaProperty
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
}

public static class McpConstants
{
    public const string ProtocolVersion = "2024-11-05";
    public const string ServerName      = "ModernTools MCP Server";
    public const string ServerVersion   = "1.0.0";
}
