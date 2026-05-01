using System.Net.Http;
using System.Text;
using System.Text.Json;
using MCPServer.Protocol;

namespace MCPServer.Server;

/// <summary>
/// Connects to a running ModernTools application via its local HTTP API (default port 2345),
/// discovers all registered API routes, and provides methods for proxying MCP tool calls
/// as HTTP requests to those routes.
/// </summary>
public sealed class ModernToolsApiProxy : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    public ModernToolsApiProxy(string baseUrl = "http://127.0.0.1:2345")
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    // ── Connectivity ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when the ModernTools HTTP API is reachable.
    /// </summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await _http.GetAsync($"{_baseUrl}/api/v1/listroute", ct)
                                  .ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // ── Route discovery ───────────────────────────────────────────────────────

    /// <summary>
    /// Retrieves the route listing from ModernTools and parses it into
    /// <see cref="ParsedRoute"/> objects ready for MCP tool registration.
    /// </summary>
    public async Task<IReadOnlyList<ParsedRoute>> DiscoverRoutesAsync(CancellationToken ct = default)
    {
        try
        {
            var json = await _http.GetStringAsync($"{_baseUrl}/api/v1/listroute", ct)
                                  .ConfigureAwait(false);

            // The response is: { "status": 200, "data": ["GET /path...", ...] }
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var dataEl))
                return [];

            var routes = new List<ParsedRoute>();
            foreach (var item in dataEl.EnumerateArray())
            {
                var line = item.GetString();
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parsed = RouteParser.Parse(line);
                if (parsed is not null)
                    routes.Add(parsed);
            }
            return routes;
        }
        catch (Exception ex)
        {
            LogError($"Route discovery failed: {ex.Message}");
            return [];
        }
    }

    // ── Tool invocation ───────────────────────────────────────────────────────

    /// <summary>
    /// Proxies a tool call to the matching ModernTools HTTP endpoint.
    /// GET routes receive parameters as query-string; POST routes receive them as JSON body.
    /// </summary>
    public async Task<ToolCallResult> InvokeAsync(
        ParsedRoute route,
        JsonElement? arguments,
        CancellationToken ct = default)
    {
        try
        {
            // Flatten arguments to a string-keyed dictionary
            var args = FlattenArguments(arguments);

            string url;
            HttpContent? body = null;

            if (route.Verb == "GET")
            {
                var qs = BuildQueryString(args);
                url = $"{_baseUrl}{route.Path}{qs}";
            }
            else
            {
                url  = $"{_baseUrl}{route.Path}";
                var payload = args.Count > 0
                    ? JsonSerializer.Serialize(args, JsonOpts)
                    : "{}";
                body = new StringContent(payload, Encoding.UTF8, "application/json");
            }

            HttpResponseMessage resp = route.Verb switch
            {
                "GET"    => await _http.GetAsync(url, ct).ConfigureAwait(false),
                "POST"   => await _http.PostAsync(url, body, ct).ConfigureAwait(false),
                "PUT"    => await _http.PutAsync(url, body, ct).ConfigureAwait(false),
                "DELETE" => await _http.DeleteAsync(url, ct).ConfigureAwait(false),
                _        => await _http.PostAsync(url, body, ct).ConfigureAwait(false),
            };

            var responseText = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            // Try to pretty-print JSON responses
            var displayText = TryPrettyPrint(responseText);

            return new ToolCallResult
            {
                Content  = [new ContentItem { Type = "text", Text = displayText }],
                IsError  = !resp.IsSuccessStatusCode,
            };
        }
        catch (TaskCanceledException)
        {
            return ErrorResult("Request timed out.");
        }
        catch (HttpRequestException ex)
        {
            return ErrorResult($"HTTP error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ErrorResult($"Unexpected error: {ex.Message}");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> FlattenArguments(JsonElement? arguments)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (arguments is null || arguments.Value.ValueKind != JsonValueKind.Object)
            return dict;

        foreach (var prop in arguments.Value.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String  => prop.Value.GetString(),
                JsonValueKind.Number  => prop.Value.TryGetInt64(out var l) ? (object)l : prop.Value.GetDouble(),
                JsonValueKind.True    => true,
                JsonValueKind.False   => false,
                JsonValueKind.Null    => null,
                _                    => prop.Value.GetRawText()
            };
        }
        return dict;
    }

    private static string BuildQueryString(Dictionary<string, object?> args)
    {
        if (args.Count == 0) return string.Empty;
        var pairs = args
            .Where(kv => kv.Value is not null)
            .Select(kv =>
            {
                var valueStr = kv.Value?.ToString() ?? string.Empty;
                return $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(valueStr)}";
            });
        return "?" + string.Join("&", pairs);
    }

    private static string TryPrettyPrint(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "(empty response)";
        try
        {
            using var doc = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return text;
        }
    }

    private static ToolCallResult ErrorResult(string message) => new()
    {
        Content = [new ContentItem { Type = "text", Text = message }],
        IsError = true,
    };

    private static void LogError(string message) =>
        Console.Error.WriteLine($"[MCPServer] {message}");

    public void Dispose() => _http.Dispose();
}
