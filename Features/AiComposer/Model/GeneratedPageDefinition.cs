using System.Text.Json.Serialization;

namespace AiComposer.Model;

/// <summary>
/// The persisted definition of a generated page. This is the single source of truth: the
/// compiled/instantiated page is only a cached projection of it. Nav metadata (Title, Glyph,
/// Group, Order) is the [PageInfo] equivalent for a dynamic page; <see cref="Xaml"/> and
/// <see cref="Csharp"/> hold the source. The manifest.json stores metadata only — the two
/// source strings live in sibling page.xaml / logic.cs files (hence [JsonIgnore]).
/// </summary>
public sealed class GeneratedPageDefinition
{
	/// <summary>Bumped when the on-disk format changes so stale definitions can be detected.</summary>
	public const int CurrentSchemaVersion = 1;

	/// <summary>Stable identity, preserved across edits. Also the storage folder name.</summary>
	public string Id { get; set; } = Guid.NewGuid().ToString("N");

	public int SchemaVersion { get; set; } = CurrentSchemaVersion;

	/// <summary>Navigation title (the [PageInfo] PageName equivalent).</summary>
	public string Title { get; set; } = "Untitled";

	/// <summary>Segoe Fluent glyph, stored as the literal character(s) (e.g. "" in code).</summary>
	public string Glyph { get; set; } = "";

	/// <summary>Nav grouping, "/"-separated for nesting (e.g. "Generated" or "Lab/Mice"). Blank = top level.</summary>
	public string Group { get; set; } = "";

	/// <summary>Nav order within its group.</summary>
	public int Order { get; set; } = int.MaxValue;

	public DateTime CreatedUtc { get; set; }
	public DateTime ModifiedUtc { get; set; }

	/// <summary>Loose XAML for the page UI. Persisted as page.xaml, not inside the manifest.</summary>
	[JsonIgnore]
	public string Xaml { get; set; } = "";

	/// <summary>C# implementing IGeneratedLogic. Persisted as logic.cs, not inside the manifest.</summary>
	[JsonIgnore]
	public string Csharp { get; set; } = "";

	/// <summary>Nav path array derived from <see cref="Group"/> ("Lab/Mice" -&gt; ["Lab","Mice"]).</summary>
	[JsonIgnore]
	public string[] NavPath => string.IsNullOrWhiteSpace(Group)
		? []
		: Group.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

	/// <summary>A stable hash of the source, used to key the compiled-assembly / XAML cache.</summary>
	[JsonIgnore]
	public string SourceHash => Compilation.SourceHasher.Hash(Xaml, Csharp);

	public GeneratedPageDefinition Clone()
		=> (GeneratedPageDefinition)MemberwiseClone();
}
