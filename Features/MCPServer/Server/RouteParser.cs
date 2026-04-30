using System.Text.RegularExpressions;
using MCPServer.Protocol;

namespace MCPServer.Server;

/// <summary>
/// Parses the route listing strings returned by the ModernTools HTTP API's
/// <c>GET /api/v1/listroute</c> endpoint and converts them into <see cref="McpTool"/>
/// definitions together with the information needed to proxy calls back.
/// </summary>
public static class RouteParser
{
    // Pattern: "VERB /path/segment?param1=TypeName(default)&param2=TypeName => Namespace.Class.Method"
    private static readonly Regex RouteRegex = new(
        @"^(?<verb>GET|POST|PUT|DELETE|PATCH)\s+(?<path>[^\s?]+)(?:\?(?<params>[^\s=>]+))?\s*=>\s*(?<handler>.+)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Parameter token: "name=TypeName" or "name=TypeName(defaultValue)"
    private static readonly Regex ParamRegex = new(
        @"(?<name>\w+)=(?<type>\w+)(?:\((?<default>[^)]*)\))?",
        RegexOptions.Compiled);

    // ── Public ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a single route listing string into a <see cref="ParsedRoute"/>.
    /// Returns null for routes that cannot be parsed or should be excluded.
    /// </summary>
    public static ParsedRoute? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var m = RouteRegex.Match(line.Trim());
        if (!m.Success) return null;

        var verb     = m.Groups["verb"].Value.ToUpperInvariant();
        var path     = m.Groups["path"].Value;
        var handler  = m.Groups["handler"].Value.Trim();

        // Resolve parameter list
        var parameters = new List<ParsedParam>();
        if (m.Groups["params"].Success)
        {
            foreach (Match pm in ParamRegex.Matches(m.Groups["params"].Value))
            {
                parameters.Add(new ParsedParam(
                    Name:         pm.Groups["name"].Value,
                    DotNetType:   pm.Groups["type"].Value,
                    HasDefault:   pm.Groups["default"].Success,
                    DefaultValue: pm.Groups["default"].Success ? pm.Groups["default"].Value : null
                ));
            }
        }

        var toolName = BuildToolName(verb, path);
        var description = BuildDescription(verb, path, handler, parameters);
        var schema = BuildSchema(parameters);

        return new ParsedRoute(
            Verb:        verb,
            Path:        path,
            ToolName:    toolName,
            Description: description,
            Schema:      schema,
            Parameters:  parameters
        );
    }

    // ── Naming ────────────────────────────────────────────────────────────────

    private static string BuildToolName(string verb, string path)
    {
        // Strip known boilerplate prefixes
        var normalised = path
            .TrimStart('/')
            .Replace("/Base/Services/", "", StringComparison.OrdinalIgnoreCase)
            .Replace("/Base/", "", StringComparison.OrdinalIgnoreCase)
            .Replace("/Features/", "", StringComparison.OrdinalIgnoreCase)
            .Replace("/api/v1/", "api_", StringComparison.OrdinalIgnoreCase)
            .Replace("/api/", "api_", StringComparison.OrdinalIgnoreCase);

        // Convert PascalCase words and path separators to snake_case
        var parts = normalised.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var tokens = parts.SelectMany(PascalToWords);

        // POST routes get the verb prepended; GET routes keep it cleaner
        string prefix = verb == "GET" ? string.Empty : verb.ToLowerInvariant() + "_";

        var name = prefix + string.Join("_", tokens);
        // Collapse any accidental double underscores and trim
        name = Regex.Replace(name, @"_+", "_").Trim('_');
        return name.ToLowerInvariant();
    }

    /// <summary>Splits a PascalCase or camelCase token into lowercase words.</summary>
    private static IEnumerable<string> PascalToWords(string token)
    {
        var result = Regex.Replace(token, @"([A-Z])(?=[a-z])|(?<=[a-z])([A-Z])", "_$1$2");
        return result.Split('_', StringSplitOptions.RemoveEmptyEntries)
                     .Select(w => w.ToLowerInvariant());
    }

    private static string BuildDescription(
        string verb, string path, string handler, List<ParsedParam> parameters)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"[{verb}] {path}");

        // Trim annotation tags from handler string for cleanliness
        var cleanHandler = handler
            .Replace(" (static)", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" (UI thread)", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        sb.Append($" — {cleanHandler}");

        if (parameters.Count > 0)
        {
            var paramList = string.Join(", ", parameters.Select(p =>
                p.HasDefault ? $"{p.Name}?: {MapTypeDescription(p.DotNetType)}"
                             : $"{p.Name}: {MapTypeDescription(p.DotNetType)}"));
            sb.Append($" | params: {paramList}");
        }

        return sb.ToString();
    }

    private static JsonSchema BuildSchema(List<ParsedParam> parameters)
    {
        if (parameters.Count == 0)
            return new JsonSchema { Type = "object" };

        var props = new Dictionary<string, JsonSchemaProperty>();
        var required = new List<string>();

        foreach (var p in parameters)
        {
            props[p.Name] = new JsonSchemaProperty
            {
                Type        = MapJsonType(p.DotNetType),
                Description = $".NET type: {p.DotNetType}" + (p.HasDefault ? $" (default: {p.DefaultValue})" : "")
            };
            if (!p.HasDefault)
                required.Add(p.Name);
        }

        return new JsonSchema
        {
            Type       = "object",
            Properties = props,
            Required   = required.Count > 0 ? required : null
        };
    }

    // ── Type mapping ──────────────────────────────────────────────────────────

    private static string MapJsonType(string dotNetType) => dotNetType.ToLowerInvariant() switch
    {
        "string"          => "string",
        "bool" or "boolean" => "boolean",
        "int32" or "int" or "int64" or "long" or "short" or "byte" => "integer",
        "single" or "float" or "double" or "decimal"               => "number",
        _ => "string"  // complex types are serialized as JSON strings / objects
    };

    private static string MapTypeDescription(string dotNetType) => dotNetType.ToLowerInvariant() switch
    {
        "string"  => "string",
        "bool" or "boolean" => "boolean",
        "int32" or "int" or "int64" or "long" or "short" or "byte" => "integer",
        "single" or "float" or "double" or "decimal"               => "number",
        _ => $"object ({dotNetType})"
    };
}

// ── Data records ──────────────────────────────────────────────────────────────

public sealed record ParsedRoute(
    string Verb,
    string Path,
    string ToolName,
    string Description,
    JsonSchema Schema,
    List<ParsedParam> Parameters
);

public sealed record ParsedParam(
    string Name,
    string DotNetType,
    bool   HasDefault,
    string? DefaultValue
);
