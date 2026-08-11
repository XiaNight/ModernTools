#nullable enable
using API;
using Base.Core;
using System.CodeDom;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Threading;

// -------------------------------------------------------
// Attributes
// -------------------------------------------------------
namespace Base.Services.APIService
{
    [AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    public abstract class HttpMethodAttribute(string path = "", bool requireMainThread = false) : Attribute
    {
        public string Path { get; } = Normalize(path);
        public bool RequireMainThread { get; } = requireMainThread;

        /// <summary>
        /// Optional human-facing description of the endpoint. When set it is preferred over the
        /// method's XML <c>&lt;summary&gt;</c> as the documentation surfaced to API/MCP clients.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Optional short summary of the endpoint. Purely informational; the XML <c>&lt;summary&gt;</c>
        /// is used automatically when neither this nor <see cref="Description"/> is supplied.
        /// </summary>
        public string? Summary { get; set; }

        internal static string Normalize(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return "/";
            p = p.Trim();
            if (!p.StartsWith("/")) p = "/" + p;
            if (p.Length > 1 && p.EndsWith("/")) p = p.TrimEnd('/');
            return p;
        }
    }

    public sealed class GETAttribute(string path = "", bool requireMainThread = false) : HttpMethodAttribute(path, requireMainThread) { }
    public sealed class POSTAttribute(string path = "", bool requireMainThread = false) : HttpMethodAttribute(path, requireMainThread) { }
}

// -------------------------------------------------------
// API Service (Singleton, non-static) - uses Main.FindObjectOfType<T>()
// -------------------------------------------------------
namespace Base.Services.APIService
{
    internal sealed class Route
    {
        public string Path = "/";
        public string Verb = "GET";
        public MethodInfo Method = default!;
        public Type DeclaringType = default!;
        public ParameterInfo[] Parameters = [];
        public bool IsStatic;
        public bool RequireMainThread;

        /// <summary>Resolved endpoint description (attribute override, else XML summary).</summary>
        public string? Description;

        /// <summary>The method's XML summary text, if any.</summary>
        public string? Summary;

        /// <summary>Per-parameter documentation from XML <c>&lt;param&gt;</c> tags (name → text).</summary>
        public IReadOnlyDictionary<string, string>? ParamDocs;
    }

    public sealed class ApiResponse
    {
        public int Status { get; set; }
        public object? Data { get; set; }
    }

    public class APIService : WpfBehaviourSingleton<APIService>
    {
        private readonly HttpListener listener = new();
        private Thread? thread;
        private CancellationTokenSource? cts;

        private Dispatcher? uiDispatcher;
        private readonly JsonSerializerOptions jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            IncludeFields = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly ConcurrentDictionary<(string verb, string path), List<Route>> routes = new();
        private readonly object sync = new();

        // XML documentation loaded from the endpoints' assemblies, used to describe routes and
        // expand DTO property docs in the /api/v1/schema manifest. Rebuilt with the route table.
        private XmlDocStore xmlDocs = new();

        public bool IsRunning { get; private set; }
        public int Port { get; private set; }

        // -------------------------------------------------------
        // Lifecycle
        // -------------------------------------------------------
        public override void Awake()
        {
            base.Awake();

            // Capture UI Dispatcher from the main (UI) thread.
            uiDispatcher = Application.Current?.Dispatcher;

            _ = V1.Instance;

            // Route-table reflection + HTTP listener bind have no UI dependency, so run them
            // off the UI thread. Doing this work inline would block startup (and stall the
            // loading-cover fade-out) for no reason — handlers are marshalled back to
            // uiDispatcher per-request when RequireMainThread is set.
            _ = Task.Run(() => Start(2345));
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            Stop();
        }

        public void Start(int port, string host = "http://127.0.0.1")
        {
            lock (sync)
            {
                if (IsRunning) return;
                try
                {
                    Port = port;
                    BuildRouteTable(AppDomain.CurrentDomain.GetAssemblies());

                    string prefix = $"{host}:{port}/";
                    listener.Prefixes.Clear();
                    listener.Prefixes.Add(prefix);
                    listener.Start();

                    cts = new CancellationTokenSource();
                    thread = new Thread(() => RunLoop(cts.Token)) { IsBackground = true, Name = "APIService.HttpListener" };
                    thread.Start();
                    IsRunning = true;

                    Debug.Log($"APIService started and listening on {prefix}");
                    Debug.Log($"Use /api/v1/listroute to list all routes");
                }
                catch (Exception e)
                {
                    Debug.Log($"APIService failed to start on {host}:{port}: {e.Message}");
                }
            }
        }

        public void Stop()
        {
            lock (sync)
            {
                if (!IsRunning) return;
                cts?.Cancel();
                try { listener.Stop(); } catch { }
                try { _ = (thread?.Join(2000)); } catch { }
                IsRunning = false;
            }
        }

        private void RunLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                HttpListenerContext? ctx = null;
                try
                {
                    IAsyncResult get = listener.BeginGetContext(null, null);
                    if (WaitHandle.WaitAny(new[] { get.AsyncWaitHandle, ct.WaitHandle }) == 1) break;
                    ctx = listener.EndGetContext(get);
                    _ = ThreadPool.UnsafeQueueUserWorkItem(_ => Handle(ctx), null);
                }
                catch (ObjectDisposedException) { break; }
                catch (HttpListenerException) { if (!listener.IsListening) break; }
                catch { }
            }
        }

        // -------------------------------------------------------
        // Request handling
        // -------------------------------------------------------
        private async Task Handle(HttpListenerContext ctx)
        {
            HttpListenerRequest req = ctx.Request;
            HttpListenerResponse res = ctx.Response;
            res.ContentType = "application/json; charset=utf-8";

            try
            {
                string verb = req.HttpMethod.ToUpperInvariant();
                string path = HttpMethodAttribute.Normalize(req.Url!.AbsolutePath).ToLowerInvariant();

                if (!routes.TryGetValue((verb, path), out List<Route>? candidates) || candidates.Count == 0)
                {
                    WriteJson(res, (int)HttpStatusCode.NotFound, new { status = 404, error = "Not Found", path, verb });
                    return;
                }

                Dictionary<string, string?> queryKV = ParseQuery(req.Url.Query);

                Dictionary<string, object?> paramsKV = [];
                object? bodyRoot = null;
                if (verb is "POST" or "PUT" or "PATCH")
                {
                    using StreamReader sr = new(req.InputStream, Encoding.UTF8);
                    string bodyText = sr.ReadToEnd();

                    if (!string.IsNullOrWhiteSpace(bodyText))
                    {
                        // Parse url encoded into paramsKV
                        foreach (KeyValuePair<string, string?> kv in ParseQuery("?" + bodyText)) paramsKV[kv.Key] = kv.Value;

                        if (IsJson(req.ContentType))
                        {
                            try
                            {
                                bodyRoot = JsonSerializer.Deserialize<object>(bodyText, jsonOptions);
                                if (bodyRoot is JsonElement je && je.ValueKind == JsonValueKind.Object)
                                {
                                    foreach (JsonProperty p in je.EnumerateObject())
                                        paramsKV[p.Name] = ToObject(p.Value);
                                }
                            }
                            catch (Exception ex)
                            {
                                WriteJson(res, 400, new { status = 400, error = "Invalid JSON", detail = ex.Message });
                                return;
                            }
                        }
                        else if (IsPlainText(req.ContentType))
                        {
                            paramsKV["body"] = bodyText;
                        }
                    }
                }

                Exception? lastBindError = null;
                bool hasBodyData = paramsKV.Count > 0 || bodyRoot != null;
                foreach (Route route in candidates)
                {
                    // When a JSON/form body is present, params may come from the body instead of the
                    // query string, so skip the strict count check and let Bind resolve them.
                    // Without a body, require: required params ≤ queryKV.Count ≤ total params
                    // (allowing optional/default params to be omitted from the query string).
                    if (!hasBodyData)
                    {
                        int requiredParamCount = route.Parameters.Count(p => !p.HasDefaultValue);
                        if (queryKV.Count < requiredParamCount || queryKV.Count > route.Parameters.Length) continue;
                    }
                    try
                    {
                        (object target, object?[] args) = Bind(route, queryKV, paramsKV, bodyRoot);

                        object? result = route.RequireMainThread ? InvokeOnUI(() => route.Method.Invoke(target, args)) : route.Method.Invoke(target, args);

                        // Await if it's a Task or Task<T>
                        if (result is Task task)
                        {
                            await task.ConfigureAwait(false);

                            Type returnType = route.Method.ReturnType;

                            result = returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>)
                                ? returnType.GetProperty(nameof(Task<object>.Result))!.GetValue(result)
                                : null;
                        }

                        WriteResponse(res, result);
                        return;
                    }
                    catch (TargetInvocationException tie)
                    {
                        WriteJson(res, 500, new
                        {
                            status = 500,
                            error = "Server Error",
                            detail = tie.InnerException?.Message ?? tie.Message,
                            path,
                            verb
                        });
                        return;
                    }
                    catch (Exception bindEx)
                    {
                        lastBindError = bindEx;
                    }
                }

                WriteJson(res, 400, new
                {
                    status = 400,
                    error = "Bad Request",
                    detail = lastBindError?.Message ?? "No route matched the provided parameters.",
                    path,
                    verb
                });
            }
            catch (Exception ex)
            {
                WriteJson(res, 500, new { status = 500, error = "Server Error", detail = ex.Message });
            }
            finally
            {
                try { res.OutputStream.Flush(); } catch { }
                try { res.Close(); } catch { }
            }
        }

        // -------------------------------------------------------
        // Binding & instance resolution (uses Main.FindObjectOfType<T>())
        // -------------------------------------------------------
        private (object? target, object?[] args) Bind(Route route, Dictionary<string, string?> queryKV, Dictionary<string, object?> bodyKV, object? bodyRoot)
        {
            object? target = null;

            if (!route.IsStatic)
            {
                target = ResolveInstance(route.DeclaringType)
                         ?? throw new InvalidOperationException($"No instance found for {route.DeclaringType.FullName} via Main.FindObjectOfType<T>().");
            }

            ParameterInfo[] pars = route.Parameters;
            object?[] args = new object?[pars.Length];

            if (pars.Length == 0)
                return (target, args);

            if (pars.Length == 1 && ShouldTreatAsComplex(pars[0].ParameterType))
            {
                Type pType = pars[0].ParameterType;
                args[0] = bodyRoot is JsonElement je
                    ? JsonSerializer.Deserialize(je.GetRawText(), pType, jsonOptions)
                    : bodyKV.Count > 0 ? MapDictionaryToObject(bodyKV, pType) : BindSimple(pars[0], queryKV.GetValueOrDefault(pars[0].Name!, null));
                return (target, args);
            }

            for (int i = 0; i < pars.Length; i++)
            {
                ParameterInfo p = pars[i];
                string name = p.Name!;
                object? val = queryKV.TryGetValue(name, out string? qv)
                    ? ConvertTo(qv, p.ParameterType)
                    : bodyKV.TryGetValue(name, out object? bv)
                        ? bv is JsonElement je ? JsonToType(je, p.ParameterType) : ChangeTypeFlexible(bv, p.ParameterType)
                        : p.HasDefaultValue
                        ? p.DefaultValue
                        : IsNullable(p.ParameterType) ? null : throw new InvalidOperationException($"Missing required parameter '{name}'.");
                args[i] = val;
            }

            return (target, args);
        }

        private static WpfBehaviour? ResolveInstance(Type type)
        {
            WpfBehaviour wpfBehaviour = Main.FindObjectOfType(type, true);
            if(wpfBehaviour == null)
            {
                Main.Dispatcher.InvokeAsync(() =>
                {
                    Main.SelectPageByType(type);
                }).Wait();
            }
            return Main.FindObjectOfType(type, true);
        }

        // -------------------------------------------------------
        // UI-thread helpers
        // -------------------------------------------------------
        private T InvokeOnUI<T>(Func<T> func)
        {
            Dispatcher? d = uiDispatcher;
            return d == null ? func() : d.CheckAccess() ? func() : d.Invoke(func);
        }

        private void InvokeOnUI(Action action)
        {
            Dispatcher? d = uiDispatcher;
            if (d == null) { action(); return; }
            if (d.CheckAccess()) { action(); return; }
            d.Invoke(action);
        }

        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------
        internal static bool ShouldTreatAsComplex(Type t)
        {
            return t != typeof(string) && (!t.IsPrimitive && (Nullable.GetUnderlyingType(t)?.IsPrimitive) != true);
        }

        private static bool IsNullable(Type t)
        {
            return !t.IsValueType || Nullable.GetUnderlyingType(t) != null;
        }

        private static object? BindSimple(ParameterInfo p, string? value)
        {
            return value is null
                ? IsNullable(p.ParameterType) ? null : throw new InvalidOperationException($"Missing required parameter '{p.Name}'.")
                : ConvertTo(value, p.ParameterType);
        }

        private static object? ConvertTo(string? input, Type targetType)
        {
            if (input is null) return null;
            Type t = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (t.IsEnum) return Enum.Parse(t, input, ignoreCase: true);
            if (t == typeof(Guid)) return Guid.Parse(input);
            if (t == typeof(DateTime)) return DateTime.Parse(input, null, System.Globalization.DateTimeStyles.RoundtripKind);
            if (t == typeof(TimeSpan)) return TimeSpan.Parse(input);
            try
            {
                return Convert.ChangeType(input, t);
            }
            catch (FormatException)
            {
                long hexValue = long.Parse(input, NumberStyles.AllowHexSpecifier);
                return Convert.ChangeType(hexValue, t);
            }
        }

        private object? ChangeTypeFlexible(object? value, Type targetType)
        {
            if (value is null) return null;
            Type t = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (value is JsonElement je) return JsonToType(je, t);
            if (t.IsInstanceOfType(value)) return value;

            if (value is string s) return ConvertTo(s, targetType);

            try { return Convert.ChangeType(value, t); }
            catch
            {
                string json = JsonSerializer.Serialize(value, jsonOptions);
                return JsonSerializer.Deserialize(json, t, jsonOptions);
            }
        }

        private object? JsonToType(JsonElement je, Type t)
        {
            return t == typeof(string) ? je.ToString() : JsonSerializer.Deserialize(je.GetRawText(), t, jsonOptions);
        }

        private static object ToObject(JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.Null => null!,
                JsonValueKind.Undefined => null!,
                JsonValueKind.String => je.GetString()!,
                JsonValueKind.Number => je.TryGetInt64(out long l) ? l :
                                        je.TryGetDouble(out double d) ? d :
                                        je.GetRawText(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Object => je,
                JsonValueKind.Array => je,
                _ => je.GetRawText()
            };
        }

        private const string APPLICATION_JSON = "application/json";
        private const string FORM_URLENCODED = "application/x-www-form-urlencoded";
        private const string MULTIPART_FORM_DATA = "multipart/form-data";
        private const string PLAIN_TEXT = "text/plain";

        private static bool IsJson(string? contentType)
        {
            return !string.IsNullOrEmpty(contentType) && contentType.Contains(APPLICATION_JSON, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFormUrlEncoded(string? contentType)
        {
            return !string.IsNullOrEmpty(contentType) && contentType.Contains(FORM_URLENCODED, StringComparison.OrdinalIgnoreCase);
        }

        static bool IsMultipartFormData(string? contentType)
        {
            return !string.IsNullOrEmpty(contentType) && contentType.Contains(MULTIPART_FORM_DATA, StringComparison.OrdinalIgnoreCase);
        }

        static bool IsPlainText(string? contentType)
        {
            return !string.IsNullOrEmpty(contentType) && contentType.Contains(PLAIN_TEXT, StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, string?> ParseQuery(string query)
        {
            Dictionary<string, string?> dict = new(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query)) return dict;
            if (query.StartsWith('?')) query = query[1..];

            foreach (string part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int idx = part.IndexOf('=');
                if (idx < 0)
                {
                    dict[WebUtility.UrlDecode(part)] = null;
                }
                else
                {
                    string k = WebUtility.UrlDecode(part[..idx]);
                    string v = WebUtility.UrlDecode(part[(idx + 1)..]);
                    dict[k] = v;
                }
            }
            return dict;
        }

        private void WriteResponse(HttpListenerResponse res, object? payload, int defaultCode = 200)
        {
            if (payload is null)
            {
                WriteJson(res, defaultCode, new { status = defaultCode });
            }
            else if (payload is ApiResponse apiRes)
            {
                WriteJson(res, apiRes.Status, new { status = apiRes.Status, data = apiRes.Data });
            }
            else
            {
                WriteJson(res, defaultCode, new { status = defaultCode, data = payload });
            }
        }

        private void WriteJson(HttpListenerResponse res, int statusCode, object obj)
        {
            res.StatusCode = statusCode;
            string json = JsonSerializer.Serialize(obj, jsonOptions);
            byte[] buf = Encoding.UTF8.GetBytes(json);
            res.ContentLength64 = buf.Length;
            using Stream s = res.OutputStream;
            s.Write(buf, 0, buf.Length);
        }

        private void BuildRouteTable(IEnumerable<Assembly> assemblies)
        {
            routes.Clear();
            xmlDocs = new XmlDocStore();

            static IEnumerable<Type> SafeGetTypes(Assembly a)
            {
                try { return a.GetTypes(); }
                catch { return []; }
            }

            IEnumerable<Type> allTypes = assemblies.SelectMany(SafeGetTypes);
            foreach (Type t in allTypes)
            {
                RegisterType(t);
            }
        }

        public void RegisterType(Type t)
        {
            if (t.IsAbstract) return;

            IEnumerable<MethodInfo> methods;
            try
            {
                methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            }
            catch { return; }

            foreach (MethodInfo m in methods)
            {
                GETAttribute? get = m.GetCustomAttribute<GETAttribute>(true);
                if (get != null)
                    AddRoute("GET", BuildFullPath(get.Path, t, m), m, t, get);

                POSTAttribute? post = m.GetCustomAttribute<POSTAttribute>(true);
                if (post != null)
                    AddRoute("POST", BuildFullPath(post.Path, t, m), m, t, post);
            }
        }

        private object MapDictionaryToObject(Dictionary<string, object?> dict, Type type)
        {
            object obj = Activator.CreateInstance(type)
                      ?? throw new InvalidOperationException($"Could not create instance of {type.FullName}");

            foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanWrite) continue;
                if (dict.TryGetValue(prop.Name, out object? value) && value != null)
                {
                    try
                    {
                        value = value is JsonElement je
                            ? JsonSerializer.Deserialize(je.GetRawText(), prop.PropertyType, jsonOptions)
                            : ChangeTypeFlexible(value, prop.PropertyType);

                        prop.SetValue(obj, value);
                    }
                    catch
                    {
                        // skip on conversion errors instead of failing completely
                    }
                }
            }
            return obj;
        }

        public string[] ListRoute()
        {
            List<string> list = [];
            foreach (KeyValuePair<(string verb, string path), List<Route>> route in routes)
            {
                foreach (Route r in route.Value)
                {
                    string path = r.Path;
                    string staticTag = r.IsStatic ? " (static)" : "";
                    string uiTag = r.RequireMainThread ? " (UI thread)" : "";

                    string paramStr = string.Join("&", r.Parameters.Select(p =>
                        {
                            string typeName = Nullable.GetUnderlyingType(p.ParameterType)?.Name ?? p.ParameterType.Name;
                            string suffix = p.HasDefaultValue ? $"({p.DefaultValue ?? "null"})" : "";
                            return $"{p.Name}={typeName}{suffix}";
                        }));

                    if (paramStr.Length > 0)
                        path += "?";

                    list.Add($"{r.Verb} {path}{paramStr} =>{staticTag}{uiTag} {r.DeclaringType.FullName}.{r.Method.Name}");
                }
            }
            list.Sort();
            return list.ToArray();
        }

        /// <summary>
        /// Returns a structured manifest describing every route: verb, path, resolved description,
        /// a JSON Schema for the accepted inputs, and (when derivable) an output schema. This is the
        /// documentation surfaced to MCP clients; it complements the string-based <see cref="ListRoute"/>.
        /// </summary>
        public object[] ListSchema()
        {
            List<object> list = [];
            foreach (KeyValuePair<(string verb, string path), List<Route>> bucket in routes)
            {
                foreach (Route r in bucket.Value)
                {
                    Dictionary<string, object> descriptor = new()
                    {
                        ["verb"] = r.Verb,
                        ["path"] = r.Path,
                        ["handler"] = $"{r.DeclaringType.FullName}.{r.Method.Name}",
                        ["requireMainThread"] = r.RequireMainThread,
                        ["static"] = r.IsStatic,
                        ["description"] = r.Description ?? $"[{r.Verb}] {r.Path}",
                        ["inputSchema"] = ApiSchema.BuildInputSchema(r.Parameters, r.ParamDocs, xmlDocs),
                    };

                    if (!string.IsNullOrWhiteSpace(r.Summary))
                        descriptor["summary"] = r.Summary;

                    Dictionary<string, object>? output = ApiSchema.BuildOutputSchema(r.Method.ReturnType);
                    if (output != null)
                        descriptor["outputSchema"] = output;

                    list.Add(descriptor);
                }
            }
            list.Sort((a, b) => string.CompareOrdinal(
                (string)((Dictionary<string, object>)a)["path"] + ((Dictionary<string, object>)a)["verb"],
                (string)((Dictionary<string, object>)b)["path"] + ((Dictionary<string, object>)b)["verb"]));
            return list.ToArray();
        }

        private void AddRoute(string verb, string path, MethodInfo m, Type declaring, HttpMethodAttribute attr)
        {
            (string, string) key = (
                verb.ToUpperInvariant(),
                HttpMethodAttribute.Normalize(path).ToLowerInvariant()
            );

            // Resolve documentation. The attribute wins when set; the method's XML <summary> is the
            // fallback. Description falls back to the (attribute or XML) summary when not set explicitly.
            xmlDocs.EnsureAssemblyLoaded(declaring.Assembly);
            XmlDocStore.MemberDoc? methodDoc = xmlDocs.GetMethodDoc(m);
            string? summary = !string.IsNullOrWhiteSpace(attr.Summary) ? attr.Summary : methodDoc?.Summary;

            Route route = new()
            {
                Verb = key.Item1,
                Path = key.Item2,
                Method = m,
                DeclaringType = declaring,
                Parameters = m.GetParameters(),
                IsStatic = m.IsStatic,
                RequireMainThread = attr.RequireMainThread,
                Summary = summary,
                Description = !string.IsNullOrWhiteSpace(attr.Description) ? attr.Description : summary,
                ParamDocs = methodDoc?.Params,
            };
            _ = routes.AddOrUpdate(key,
                _ => [route],
                (_, list) => { list.Add(route); return list; });
        }

        private static string BuildFullPath(string attrPath, Type declaringType, MethodInfo method)
        {
            // If dev prefixes with "~/", treat as absolute override (keeps current Behaviour).
            if (!string.IsNullOrWhiteSpace(attrPath) && attrPath.StartsWith("~/"))
                return HttpMethodAttribute.Normalize(attrPath[1..]); // remove '~'

            // Base: Namespace + Type => "MyGame/Controllers/HealthController"
            string basePath = (declaringType.FullName ?? "")
                .Replace('.', '/')
                .Trim('/');

            // Suffix: normalize attribute path; if empty or "/", fallback to method name
            string suffix = HttpMethodAttribute.Normalize(attrPath);
            if (string.IsNullOrWhiteSpace(attrPath) || suffix == "/")
                suffix = "/" + method.Name;

            string full = "/" + basePath + suffix;
            return HttpMethodAttribute.Normalize(full);
        }

        // -------------------------------------------------------
        // Optional: If you want to manually refresh routes at runtime
        // -------------------------------------------------------
        public void RebuildRoutes()
        {
            BuildRouteTable(AppDomain.CurrentDomain.GetAssemblies());
        }
    }
}