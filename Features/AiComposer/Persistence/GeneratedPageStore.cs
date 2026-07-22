using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiComposer.Model;
using Base.Services;

namespace AiComposer.Persistence;

/// <summary>
/// Human-readable disk store for generated page definitions. One folder per page (named by the
/// page Id) holding manifest.json + page.xaml + logic.cs. The split keeps the source easy to read,
/// diff, and — for the editing flow — load back a prior version. The persisted source is the single
/// source of truth; loading a manifest never touches the (potentially large) source files, so
/// startup can register nav entries from metadata alone.
/// </summary>
public sealed class GeneratedPageStore
{
	private const string ManifestFile = "manifest.json";
	private const string XamlFile = "page.xaml";
	private const string LogicFile = "logic.cs";

	private static readonly JsonSerializerOptions Json = new()
	{
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() },
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	private readonly string rootDir;

	public GeneratedPageStore(string rootDir)
	{
		this.rootDir = rootDir;
		Directory.CreateDirectory(rootDir);
	}

	public string RootDirectory => rootDir;

	private string PageDir(string id) => Path.Combine(rootDir, id);

	/// <summary>
	/// Reads every page's manifest (metadata only — no XAML/C# read). Used at startup to register
	/// nav entries without parsing or compiling anything. Malformed manifests are skipped, not fatal.
	/// </summary>
	public List<GeneratedPageDefinition> LoadAllManifests()
	{
		List<GeneratedPageDefinition> result = new();
		if (!Directory.Exists(rootDir)) return result;

		foreach (string dir in Directory.GetDirectories(rootDir))
		{
			string manifestPath = Path.Combine(dir, ManifestFile);
			if (!File.Exists(manifestPath)) continue;

			try
			{
				GeneratedPageDefinition def =
					JsonSerializer.Deserialize<GeneratedPageDefinition>(File.ReadAllText(manifestPath), Json);
				if (def != null && !string.IsNullOrEmpty(def.Id))
					result.Add(def);
			}
			catch (Exception ex)
			{
				Debug.Log($"[AiComposer] Skipped unreadable manifest '{manifestPath}': {ex.Message}");
			}
		}

		return result;
	}

	/// <summary>Fills <paramref name="def"/>'s Xaml / Csharp from the sibling source files.</summary>
	public void LoadSource(GeneratedPageDefinition def)
	{
		string dir = PageDir(def.Id);
		string xamlPath = Path.Combine(dir, XamlFile);
		string logicPath = Path.Combine(dir, LogicFile);
		def.Xaml = File.Exists(xamlPath) ? File.ReadAllText(xamlPath) : "";
		def.Csharp = File.Exists(logicPath) ? File.ReadAllText(logicPath) : "";
	}

	/// <summary>Loads a full definition (manifest + source), or null if the page folder is missing.</summary>
	public GeneratedPageDefinition LoadFull(string id)
	{
		string manifestPath = Path.Combine(PageDir(id), ManifestFile);
		if (!File.Exists(manifestPath)) return null;

		GeneratedPageDefinition def =
			JsonSerializer.Deserialize<GeneratedPageDefinition>(File.ReadAllText(manifestPath), Json);
		if (def == null) return null;

		LoadSource(def);
		return def;
	}

	/// <summary>Writes the manifest and both source files, overwriting any existing page with the same Id.</summary>
	public void Save(GeneratedPageDefinition def)
	{
		string dir = PageDir(def.Id);
		Directory.CreateDirectory(dir);
		File.WriteAllText(Path.Combine(dir, ManifestFile), JsonSerializer.Serialize(def, Json));
		File.WriteAllText(Path.Combine(dir, XamlFile), def.Xaml ?? "");
		File.WriteAllText(Path.Combine(dir, LogicFile), def.Csharp ?? "");
	}

	/// <summary>Removes a page's folder and all its files.</summary>
	public void Delete(string id)
	{
		string dir = PageDir(id);
		if (Directory.Exists(dir))
			Directory.Delete(dir, recursive: true);
	}
}
