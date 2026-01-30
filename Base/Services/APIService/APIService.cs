#nullable enable
using API;
using Base.Core;
using System.Collections.Concurrent;
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
    public abstract class HttpMethodAttribute(string path, bool requireMainThread = false) : Attribute
    {
        public string Path { get; } = Normalize(path);
        public bool RequireMainThread { get; } = requireMainThread;

        internal static string Normalize(string p)
        {
            if (string.IsNullOrWhiteSpace(p)) return "/";
            p = p.Trim();
            if (!p.StartsWith("/")) p = "/" + p;
            if (p.Length > 1 && p.EndsWith("/")) p = p.TrimEnd('/');
            return p;
        }
    }

    public sealed class GETAttribute(string path, bool requireMainThread = false) : HttpMethodAttribute(path, requireMainThread) { }
    public sealed class POSTAttribute(string path, bool requireMainThread = false) : HttpMethodAttribute(path, requireMainThread) { }
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
        public ParameterInfo[] Parameters = Array.Empty<ParameterInfo>();
        public bool IsStatic;
        public bool RequireMainThread;
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
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly ConcurrentDictionary<(string verb, string path), List<Route>> routes = new();
        private readonly object sync = new();

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

            Start(8080);
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

                    var prefix = $"{host}:{port}/";
                    listener.Prefixes.Clear();
                    listener.Prefixes.Add(prefix);
                    listener.Start();

                    cts = new CancellationTokenSource();
                    thread = new Thread(() => RunLoop(cts.Token)) { IsBackground = true, Name = "APIService.HttpListener" };
                    thread.Start();
                    IsRunning = true;
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
                try { thread?.Join(2000); } catch { }
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
                    var get = listener.BeginGetContext(null, null);
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
            var req = ctx.Request;
            var res = ctx.Response;
            res.ContentType = "application/json; charset=utf-8";

            try
            {
                var verb = req.HttpMethod.ToUpperInvariant();
                var path = HttpMethodAttribute.Normalize(req.Url!.AbsolutePath).ToLowerInvariant();

                if (!routes.TryGetValue((verb, path), out var candidates) || candidates.Count == 0)
                {
                    WriteJson(res, (int)HttpStatusCode.NotFound, new { status = 404, error = "Not Found", path, verb });
                    return;
                }

                var queryKV = ParseQuery(req.Url.Query);

                Dictionary<string, object?> bodyKV = new();
                object? bodyRoot = null;
                if (verb is "POST" or "PUT" or "PATCH")
                {
                    if (IsJson(req.ContentType))
                    {
                        using var sr = new StreamReader(req.InputStream, Encoding.UTF8);
                        var bodyText = sr.ReadToEnd();
                        if (!string.IsNullOrWhiteSpace(bodyText))
                        {
                            try
                            {
                                bodyRoot = JsonSerializer.Deserialize<object>(bodyText, jsonOptions);
                                if (bodyRoot is JsonElement je && je.ValueKind == JsonValueKind.Object)
                                {
                                    foreach (var p in je.EnumerateObject())
                                        bodyKV[p.Name] = ToObject(p.Value);
                                }
                            }
                            catch (Exception ex)
                            {
                                WriteJson(res, 400, new { status = 400, error = "Invalid JSON", detail = ex.Message });
                                return;
                            }
                        }
                    }
                    else if (IsFormUrlEncoded(req.ContentType))
                    {
                        using var sr = new StreamReader(req.InputStream, Encoding.UTF8);
                        var bodyText = sr.ReadToEnd();
                        foreach (var kv in ParseQuery("?" + bodyText)) bodyKV[kv.Key] = kv.Value;
                    }
                }

                Exception? lastBindError = null;
                foreach (var route in candidates)
                {
                    try
                    {
                        var (target, args) = Bind(route, queryKV, bodyKV, bodyRoot);

                        object? result;
                        if (route.RequireMainThread)
                        {
                            result = InvokeOnUI(() => route.Method.Invoke(target, args));
                        }
                        else
                        {
                            result = route.Method.Invoke(target, args);
                        }

                        // Await if it's a Task or Task<T>
                        if (result is Task task)
                        {
                            await task.ConfigureAwait(false);
                            if (result.GetType().IsGenericType && result.GetType().GetGenericTypeDefinition() == typeof(Task<>))
                            {
                                result = result.GetType().GetProperty("Result")!.GetValue(result);
                            }
                            else
                            {
                                result = null; // Task with no result
                            }
                        }

                        WriteOk(res, result);
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

            var pars = route.Parameters;
            var args = new object?[pars.Length];

            if (pars.Length == 0)
                return (target, args);

            if (pars.Length == 1 && ShouldTreatAsComplex(pars[0].ParameterType))
            {
                var pType = pars[0].ParameterType;
                if (bodyRoot is JsonElement je)
                    args[0] = JsonSerializer.Deserialize(je.GetRawText(), pType, jsonOptions);
                else if (bodyKV.Count > 0)
                    args[0] = MapDictionaryToObject(bodyKV, pType);
                else
                    args[0] = BindSimple(pars[0], queryKV.GetValueOrDefault(pars[0].Name!, null));
                return (target, args);
            }

            for (int i = 0; i < pars.Length; i++)
            {
                var p = pars[i];
                var name = p.Name!;
                object? val = null;

                if (queryKV.TryGetValue(name, out var qv))
                {
                    val = ConvertTo(qv, p.ParameterType);
                }
                else if (bodyKV.TryGetValue(name, out var bv))
                {
                    if (bv is JsonElement je)
                        val = JsonToType(je, p.ParameterType);
                    else
                        val = ChangeTypeFlexible(bv, p.ParameterType);
                }
                else if (p.HasDefaultValue)
                {
                    val = p.DefaultValue;
                }
                else if (IsNullable(p.ParameterType))
                {
                    val = null;
                }
                else
                {
                    throw new InvalidOperationException($"Missing required parameter '{name}'.");
                }

                args[i] = val;
            }

            return (target, args);
        }

        private object? ResolveInstance(Type type)
        {
            var finder = Main;
            if (finder is null) return null;

            var mi = finder.GetType().GetMethod("FindObjectOfType", BindingFlags.Public | BindingFlags.Instance);
            if (mi is null) return null;

            var g = mi.MakeGenericMethod(type);
            return g.Invoke(finder, [true]);
        }

        // -------------------------------------------------------
        // UI-thread helpers
        // -------------------------------------------------------
        private T InvokeOnUI<T>(Func<T> func)
        {
            var d = uiDispatcher;
            if (d == null) return func();
            if (d.CheckAccess()) return func();
            return d.Invoke(func);
        }

        private void InvokeOnUI(Action action)
        {
            var d = uiDispatcher;
            if (d == null) { action(); return; }
            if (d.CheckAccess()) { action(); return; }
            d.Invoke(action);
        }

        // -------------------------------------------------------
        // Helpers
        // -------------------------------------------------------
        private static bool ShouldTreatAsComplex(Type t)
        {
            if (t == typeof(string)) return false;
            if (t.IsPrimitive) return false;
            if (Nullable.GetUnderlyingType(t)?.IsPrimitive == true) return false;
            return true;
        }

        private static bool IsNullable(Type t) => !t.IsValueType || Nullable.GetUnderlyingType(t) != null;

        private object? BindSimple(ParameterInfo p, string? value)
        {
            if (value is null)
            {
                if (IsNullable(p.ParameterType)) return null;
                throw new InvalidOperationException($"Missing required parameter '{p.Name}'.");
            }
            return ConvertTo(value, p.ParameterType);
        }

        private static object? ConvertTo(string? input, Type targetType)
        {
            if (input is null) return null;
            var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (t.IsEnum) return Enum.Parse(t, input, ignoreCase: true);
            if (t == typeof(Guid)) return Guid.Parse(input);
            if (t == typeof(DateTime)) return DateTime.Parse(input, null, System.Globalization.DateTimeStyles.RoundtripKind);
            if (t == typeof(TimeSpan)) return TimeSpan.Parse(input);
            return Convert.ChangeType(input, t);
        }

        private object? ChangeTypeFlexible(object? value, Type targetType)
        {
            if (value is null) return null;
            var t = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (value is JsonElement je) return JsonToType(je, t);
            if (t.IsInstanceOfType(value)) return value;

            if (value is string s) return ConvertTo(s, targetType);

            try { return Convert.ChangeType(value, t); }
            catch
            {
                var json = JsonSerializer.Serialize(value, jsonOptions);
                return JsonSerializer.Deserialize(json, t, jsonOptions);
            }
        }

        private object? JsonToType(JsonElement je, Type t)
        {
            if (t == typeof(string)) return je.ToString();
            return JsonSerializer.Deserialize(je.GetRawText(), t, jsonOptions);
        }

        private static object ToObject(JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.Null => null!,
                JsonValueKind.Undefined => null!,
                JsonValueKind.String => je.GetString()!,
                JsonValueKind.Number => je.TryGetInt64(out var l) ? l :
                                        je.TryGetDouble(out var d) ? d :
                                        je.GetRawText(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Object => je,
                JsonValueKind.Array => je,
                _ => je.GetRawText()
            };
        }

        private static bool IsJson(string? contentType)
            => !string.IsNullOrEmpty(contentType) && contentType!.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsFormUrlEncoded(string? contentType)
            => !string.IsNullOrEmpty(contentType) && contentType!.IndexOf("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) >= 0;

        private static Dictionary<string, string?> ParseQuery(string query)
        {
            var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query)) return dict;
            if (query.StartsWith("?")) query = query[1..];

            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var idx = part.IndexOf('=');
                if (idx < 0)
                {
                    dict[WebUtility.UrlDecode(part)] = null;
                }
                else
                {
                    var k = WebUtility.UrlDecode(part[..idx]);
                    var v = WebUtility.UrlDecode(part[(idx + 1)..]);
                    dict[k] = v;
                }
            }
            return dict;
        }

        private void WriteOk(HttpListenerResponse res, object? payload)
        {
            if (payload is null)
            {
                WriteJson(res, 200, new { status = 200 });
            }
            else
            {
                WriteJson(res, 200, new { status = 200, data = payload });
            }
        }

        private void WriteJson(HttpListenerResponse res, int statusCode, object obj)
        {
            res.StatusCode = statusCode;
            var json = JsonSerializer.Serialize(obj, jsonOptions);
            var buf = Encoding.UTF8.GetBytes(json);
            res.ContentLength64 = buf.Length;
            using var s = res.OutputStream;
            s.Write(buf, 0, buf.Length);
        }

        private void BuildRouteTable(IEnumerable<Assembly> assemblies)
        {
            routes.Clear();

            static IEnumerable<Type> SafeGetTypes(Assembly a)
            {
                try { return a.GetTypes(); }
                catch { return Array.Empty<Type>(); }
            }

            var allTypes = assemblies.SelectMany(SafeGetTypes);
            foreach (var t in allTypes)
            {
                if (t.IsAbstract) continue;

                IEnumerable<MethodInfo> methods;
                try
                {
                    methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                }
                catch { continue; }

                foreach (var m in methods)
                {
                    var get = m.GetCustomAttribute<GETAttribute>(true);
                    if (get != null)
                        AddRoute("GET", BuildFullPath(get.Path, t, m), m, t, get.RequireMainThread);

                    var post = m.GetCustomAttribute<POSTAttribute>(true);
                    if (post != null)
                        AddRoute("POST", BuildFullPath(post.Path, t, m), m, t, post.RequireMainThread);
                }
            }
        }

        private object MapDictionaryToObject(Dictionary<string, object?> dict, Type type)
        {
            var obj = Activator.CreateInstance(type)
                      ?? throw new InvalidOperationException($"Could not create instance of {type.FullName}");

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!prop.CanWrite) continue;
                if (dict.TryGetValue(prop.Name, out var value) && value != null)
                {
                    try
                    {
                        if (value is JsonElement je)
                        {
                            value = JsonSerializer.Deserialize(je.GetRawText(), prop.PropertyType, jsonOptions);
                        }
                        else
                        {
                            value = ChangeTypeFlexible(value, prop.PropertyType);
                        }

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
            var list = new List<string>();
            foreach (var route in routes)
            {
                foreach (var r in route.Value)
                {
                    var path = r.Path;
                    if (r.IsStatic)
                        path += " (static)";
                    if (r.RequireMainThread)
                        path += " (UI thread only)";
                    list.Add($"{r.Verb} {path} => {r.DeclaringType.FullName}.{r.Method.Name}");
                }
            }
            list.Sort();
            return list.ToArray();
        }

        private void AddRoute(string verb, string path, MethodInfo m, Type declaring, bool requireMainThread)
        {
            var key = (
                verb.ToUpperInvariant(),
                HttpMethodAttribute.Normalize(path).ToLowerInvariant()
            );

            var route = new Route
            {
                Verb = key.Item1,
                Path = key.Item2,
                Method = m,
                DeclaringType = declaring,
                Parameters = m.GetParameters(),
                IsStatic = m.IsStatic,
                RequireMainThread = requireMainThread,
            };
            routes.AddOrUpdate(key,
                _ => new List<Route> { route },
                (_, list) => { list.Add(route); return list; });
        }

        private static string BuildFullPath(string attrPath, Type declaringType, MethodInfo method)
        {
            // If dev prefixes with "~/", treat as absolute override (keeps current behavior).
            if (!string.IsNullOrWhiteSpace(attrPath) && attrPath.StartsWith("~/"))
                return HttpMethodAttribute.Normalize(attrPath[1..]); // remove '~'

            // Base: Namespace + Type => "MyGame/Controllers/HealthController"
            var basePath = (declaringType.FullName ?? "")
                .Replace('.', '/')
                .Trim('/');

            // Suffix: normalize attribute path; if empty or "/", fallback to method name
            var suffix = HttpMethodAttribute.Normalize(attrPath);
            if (string.IsNullOrWhiteSpace(attrPath) || suffix == "/")
                suffix = "/" + method.Name;

            var full = "/" + basePath + suffix;
            return HttpMethodAttribute.Normalize(full);
        }

        // -------------------------------------------------------
        // Optional: If you want to manually refresh routes at runtime
        // -------------------------------------------------------
        public void RebuildRoutes() => BuildRouteTable(AppDomain.CurrentDomain.GetAssemblies());
    }
}