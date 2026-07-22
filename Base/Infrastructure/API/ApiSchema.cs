#nullable enable
using System.Collections;
using System.IO;
using System.Reflection;
using System.Xml.Linq;

namespace Base.Services.APIService;

// -------------------------------------------------------
// XML documentation store
// -------------------------------------------------------

/// <summary>
/// Loads and caches the compiler-generated XML documentation files (produced by
/// <c>&lt;GenerateDocumentationFile&gt;</c>) that sit next to endpoint assemblies, and resolves
/// <c>&lt;summary&gt;</c> / <c>&lt;param&gt;</c> text for methods and properties. This lets the rich
/// XML docs developers already write flow through to the API/MCP documentation surface.
/// </summary>
public sealed class XmlDocStore
{
	/// <summary>Documentation for a single member (method summary + parameter docs, or property summary).</summary>
	public sealed class MemberDoc
	{
		public string? Summary;
		public Dictionary<string, string>? Params;
	}

	private readonly object sync = new();
	private readonly HashSet<Assembly> loaded = new();

	// Method docs indexed by "M:Namespace.Type.Method" (parameters stripped — endpoints are not overloaded).
	private readonly Dictionary<string, MemberDoc> methodDocs = new(StringComparer.Ordinal);

	// Property docs indexed by their exact XML member id, e.g. "P:Namespace.Type.PropertyName".
	private readonly Dictionary<string, string> propertySummaries = new(StringComparer.Ordinal);

	/// <summary>Parses and caches the XML documentation file for an assembly, once.</summary>
	public void EnsureAssemblyLoaded(Assembly assembly)
	{
		if (assembly == null) return;

		lock (sync)
		{
			if (!loaded.Add(assembly)) return;

			string location;
			try { location = assembly.Location; }
			catch { location = string.Empty; }

			if (string.IsNullOrEmpty(location)) return;

			string xmlPath = Path.ChangeExtension(location, ".xml");
			if (!File.Exists(xmlPath)) return;

			try
			{
				XDocument doc = XDocument.Load(xmlPath);
				XElement? members = doc.Root?.Element("members");
				if (members == null) return;

				foreach (XElement member in members.Elements("member"))
				{
					string? name = member.Attribute("name")?.Value;
					if (string.IsNullOrEmpty(name) || name.Length < 2) continue;

					char kind = name[0];
					string summary = Normalize(member.Element("summary")?.Value);

					if (kind == 'M')
					{
						var doc2 = new MemberDoc { Summary = string.IsNullOrEmpty(summary) ? null : summary };

						Dictionary<string, string>? paramDocs = null;
						foreach (XElement pe in member.Elements("param"))
						{
							string? pName = pe.Attribute("name")?.Value;
							if (string.IsNullOrEmpty(pName)) continue;
							string pText = Normalize(pe.Value);
							if (string.IsNullOrEmpty(pText)) continue;
							(paramDocs ??= new Dictionary<string, string>(StringComparer.Ordinal))[pName] = pText;
						}
						doc2.Params = paramDocs;

						// Strip the parameter list so "M:Type.Method(...)" collapses to "M:Type.Method".
						string key = StripSignature(name);
						methodDocs.TryAdd(key, doc2);
					}
					else if (kind == 'P')
					{
						if (!string.IsNullOrEmpty(summary))
							propertySummaries.TryAdd(StripSignature(name), summary);
					}
				}
			}
			catch
			{
				// Malformed / unreadable XML doc file — ignore, docs are best-effort.
			}
		}
	}

	/// <summary>Returns method documentation, or null when unavailable.</summary>
	public MemberDoc? GetMethodDoc(MethodInfo method)
	{
		if (method?.DeclaringType == null) return null;
		EnsureAssemblyLoaded(method.DeclaringType.Assembly);
		string key = "M:" + TypeDocName(method.DeclaringType) + "." + method.Name;
		lock (sync)
		{
			return methodDocs.TryGetValue(key, out var doc) ? doc : null;
		}
	}

	/// <summary>Returns a property's <c>&lt;summary&gt;</c> text, or null when unavailable.</summary>
	public string? GetPropertySummary(PropertyInfo property)
	{
		if (property?.DeclaringType == null) return null;
		EnsureAssemblyLoaded(property.DeclaringType.Assembly);
		string key = "P:" + TypeDocName(property.DeclaringType) + "." + property.Name;
		lock (sync)
		{
			return propertySummaries.TryGetValue(key, out var summary) ? summary : null;
		}
	}

	// Namespace-qualified type name using '.' for nested types, matching XML doc member ids.
	private static string TypeDocName(Type type)
	{
		return (type.FullName ?? type.Name).Replace('+', '.');
	}

	// Removes an XML member-id parameter list / generic-arity suffix, keeping "X:Namespace.Type.Member".
	private static string StripSignature(string memberId)
	{
		int paren = memberId.IndexOf('(');
		if (paren >= 0) memberId = memberId[..paren];
		int tick = memberId.IndexOf('`');
		if (tick >= 0) memberId = memberId[..tick];
		return memberId;
	}

	private static string Normalize(string? text)
	{
		if (string.IsNullOrWhiteSpace(text)) return string.Empty;
		return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
	}
}

// -------------------------------------------------------
// JSON Schema builder
// -------------------------------------------------------

/// <summary>
/// Builds JSON Schema fragments from .NET parameter and DTO types via reflection. Primitives map to
/// their JSON types, enums emit their names, collections become arrays, and a single complex request
/// DTO is expanded recursively into a nested object schema — with per-property <c>&lt;summary&gt;</c>
/// docs and default values where available.
/// </summary>
public static class ApiSchema
{
	/// <summary>
	/// Builds the input schema for a route. A lone complex DTO parameter (the request-body case) is
	/// expanded as the root object; otherwise every parameter becomes a property of the root object.
	/// </summary>
	public static Dictionary<string, object> BuildInputSchema(
		ParameterInfo[] parameters,
		IReadOnlyDictionary<string, string>? paramDocs,
		XmlDocStore docs)
	{
		parameters ??= Array.Empty<ParameterInfo>();
		docs ??= new XmlDocStore();

		// Single complex DTO → the request body IS that object; expand it as the root schema.
		if (parameters.Length == 1 && APIService.ShouldTreatAsComplex(parameters[0].ParameterType))
		{
			Dictionary<string, object> body = BuildTypeSchema(parameters[0].ParameterType, docs, new HashSet<Type>());
			if (paramDocs != null
				&& parameters[0].Name != null
				&& paramDocs.TryGetValue(parameters[0].Name!, out string? bodyDoc)
				&& !body.ContainsKey("description")
				&& !string.IsNullOrEmpty(bodyDoc))
			{
				body["description"] = bodyDoc;
			}
			return body;
		}

		var properties = new Dictionary<string, object>();
		var required = new List<string>();

		foreach (ParameterInfo p in parameters)
		{
			string name = p.Name ?? "arg";
			Dictionary<string, object> prop = BuildTypeSchema(p.ParameterType, docs, new HashSet<Type>());

			if (paramDocs != null && paramDocs.TryGetValue(name, out string? doc) && !string.IsNullOrEmpty(doc))
				prop["description"] = doc;

			if (p.HasDefaultValue && IsSimpleValue(p.DefaultValue))
				prop["default"] = p.DefaultValue!;

			properties[name] = prop;

			if (!p.HasDefaultValue && !IsNullable(p.ParameterType))
				required.Add(name);
		}

		var schema = new Dictionary<string, object> { ["type"] = "object" };
		if (properties.Count > 0) schema["properties"] = properties;
		if (required.Count > 0) schema["required"] = required;
		return schema;
	}

	/// <summary>
	/// Builds a schema for a route's return shape, describing the <c>{ status, data }</c> envelope the
	/// API wraps responses in. Returns null when nothing meaningful can be said (void / plain Task).
	/// </summary>
	public static Dictionary<string, object>? BuildOutputSchema(Type returnType)
	{
		Type? dataType = UnwrapReturnType(returnType);

		var props = new Dictionary<string, object>
		{
			["status"] = new Dictionary<string, object> { ["type"] = "integer" }
		};

		// ApiResponse carries an untyped Data payload; anything else we can describe, we do.
		if (dataType != null
			&& dataType != typeof(void)
			&& dataType != typeof(object)
			&& dataType.Name != "ApiResponse")
		{
			props["data"] = BuildTypeSchema(dataType, new XmlDocStore(), new HashSet<Type>());
		}

		return new Dictionary<string, object>
		{
			["type"] = "object",
			["properties"] = props
		};
	}

	// -------------------------------------------------------
	// Core type → schema mapping
	// -------------------------------------------------------

	private static Dictionary<string, object> BuildTypeSchema(Type type, XmlDocStore docs, HashSet<Type> visited)
	{
		Type t = Nullable.GetUnderlyingType(type) ?? type;
		var schema = new Dictionary<string, object>();

		if (t == typeof(string) || t == typeof(char) || t == typeof(Guid))
		{
			schema["type"] = "string";
			return schema;
		}

		if (t == typeof(DateTime) || t == typeof(DateTimeOffset))
		{
			schema["type"] = "string";
			schema["format"] = "date-time";
			return schema;
		}

		if (t == typeof(TimeSpan))
		{
			schema["type"] = "string";
			return schema;
		}

		if (t == typeof(bool))
		{
			schema["type"] = "boolean";
			return schema;
		}

		if (t.IsEnum)
		{
			schema["type"] = "string";
			schema["enum"] = Enum.GetNames(t);
			return schema;
		}

		if (IsIntegerType(t))
		{
			schema["type"] = "integer";
			return schema;
		}

		if (IsFloatType(t))
		{
			schema["type"] = "number";
			return schema;
		}

		Type? element = GetEnumerableElementType(t);
		if (element != null)
		{
			schema["type"] = "array";
			schema["items"] = BuildTypeSchema(element, docs, visited);
			return schema;
		}

		// Complex object — expand its public readable properties recursively (with a cycle guard).
		schema["type"] = "object";
		if (!visited.Add(t))
			return schema;

		docs.EnsureAssemblyLoaded(t.Assembly);
		object? instance = TryCreateInstance(t);
		var properties = new Dictionary<string, object>();

		foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			if (!p.CanRead || p.GetIndexParameters().Length > 0) continue;

			Dictionary<string, object> ps = BuildTypeSchema(p.PropertyType, docs, visited);

			string? summary = docs.GetPropertySummary(p);
			if (!string.IsNullOrEmpty(summary))
				ps["description"] = summary;

			if (instance != null)
			{
				try
				{
					object? value = p.GetValue(instance);
					if (IsSimpleValue(value))
						ps["default"] = value!;
				}
				catch
				{
					// Ignore property getters that throw during default probing.
				}
			}

			properties[p.Name] = ps;
		}

		visited.Remove(t);
		if (properties.Count > 0)
			schema["properties"] = properties;
		return schema;
	}

	// -------------------------------------------------------
	// Helpers
	// -------------------------------------------------------

	private static Type? UnwrapReturnType(Type returnType)
	{
		if (returnType == typeof(Task) || returnType == typeof(void))
			return null;

		if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
			return returnType.GetGenericArguments()[0];

		return returnType;
	}

	private static bool IsIntegerType(Type t)
	{
		return t == typeof(int) || t == typeof(uint)
			|| t == typeof(long) || t == typeof(ulong)
			|| t == typeof(short) || t == typeof(ushort)
			|| t == typeof(byte) || t == typeof(sbyte)
			|| t == typeof(nint) || t == typeof(nuint);
	}

	private static bool IsFloatType(Type t)
	{
		return t == typeof(float) || t == typeof(double) || t == typeof(decimal);
	}

	private static bool IsNullable(Type t)
	{
		return !t.IsValueType || Nullable.GetUnderlyingType(t) != null;
	}

	// Only inline scalar defaults into the schema — never reference/collection instances.
	private static bool IsSimpleValue(object? value)
	{
		if (value == null) return false;
		Type t = value.GetType();
		return t.IsPrimitive || t.IsEnum || value is string || value is decimal;
	}

	private static Type? GetEnumerableElementType(Type t)
	{
		if (t == typeof(string)) return null;
		if (typeof(IDictionary).IsAssignableFrom(t)) return null;

		if (t.IsArray) return t.GetElementType();

		if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>))
			return t.GetGenericArguments()[0];

		foreach (Type i in t.GetInterfaces())
		{
			if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
				return i.GetGenericArguments()[0];
		}

		return null;
	}

	private static object? TryCreateInstance(Type t)
	{
		try
		{
			if (t.IsAbstract || t.GetConstructor(Type.EmptyTypes) == null) return null;
			return Activator.CreateInstance(t);
		}
		catch
		{
			return null;
		}
	}
}