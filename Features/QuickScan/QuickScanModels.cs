using Base.Protocol;

namespace QuickScan;

/// <summary>
/// One protocol command to scan: how to build the request and how to judge the reply.
/// Serialized to JSON (byte[] as base64) for persistence and import/export.
/// </summary>
public sealed class QuickScanEntry
{
	public string Name { get; set; } = "";
	public string Group { get; set; } = "";
	// Optional sub-group key. Entries that share one (e.g. the fixed sub-indexes of
	// Get FW Info) are rendered together under a QuickScanGroupControl; empty = standalone.
	public string SubGroup { get; set; } = "";
	public string Description { get; set; } = "";

	// Request frame fields.
	public byte Command { get; set; }
	public byte Key { get; set; }
	public ushort Index { get; set; } = 0;
	public byte[] ParamBytes { get; set; } = Array.Empty<byte>();

	// True when the reply's Index must also echo the request's Index.
	public bool ExpectIndexEcho { get; set; }
	// UI hint: this Get needs a key code / index / effect id filled in to be meaningful.
	public bool RequiresParam { get; set; }

	// Validation.
	public ValidationMode Mode { get; set; } = ValidationMode.Structural;
	public List<RangeRule> Rules { get; set; } = new();
	public int CompareLength { get; set; }

	// Captured golden reply data (from Byte 4 onward) used by ExactMatch mode.
	public byte[] Baseline { get; set; }
	public DateTime? BaselineCapturedUtc { get; set; }

	public bool Enabled { get; set; } = true;
	public int TimeoutMs { get; set; } = 150;

	public byte[] BuildRequest() => ProtocolFrame.Build(Command, Key, Index, ParamBytes);

	public ProtocolCheck ToCheck() => new()
	{
		Name = Name,
		Group = Group,
		Command = Command,
		Key = Key,
		Index = Index,
		ExpectIndexEcho = ExpectIndexEcho,
		Mode = Mode,
		Rules = Rules,
		Baseline = Baseline,
		CompareLength = CompareLength,
	};

	public string HeaderHex()
	{
		string index = $"{Index:X4}";
		string param = ParamBytes is { Length: > 0 } ? " data " + BitConverter.ToString(ParamBytes) : "";
		return $"{Command:X2} {Key:X2}{index}{param}";
	}
}

/// <summary>A per-model set of protocol entries to scan.</summary>
public sealed class QuickScanScenario
{
	public string Id { get; set; } = Guid.NewGuid().ToString("N");
	public string Name { get; set; } = "";
	public string ModelName { get; set; } = "";

	// Auto-selection hints. 0 = don't match on that field; fall back to product-name.
	public ushort VID { get; set; }
	public ushort PID { get; set; }

	// Built-in scenarios ship in code; a user copy with the same Id overrides one.
	public bool BuiltIn { get; set; }

	// Structure version of a built-in scenario. Bumped when the built-in's entry set
	// changes; a stored copy with a lower version is treated as stale and replaced by the
	// fresh built-in (see QuickScanStore.GetAll), so built-in updates aren't masked.
	public int Version { get; set; }

	public List<QuickScanEntry> Entries { get; set; } = new();

	/// <summary>How well this scenario matches a connected device (higher = better).</summary>
	public int MatchScore(ushort vid, ushort pid, string productName)
	{
		if (PID != 0 && PID == pid && (VID == 0 || VID == vid)) return 3;
		if (!string.IsNullOrWhiteSpace(ModelName) && !string.IsNullOrEmpty(productName)
			&& productName.Contains(ModelName, StringComparison.OrdinalIgnoreCase)) return 2;
		return 0;
	}

	public QuickScanScenario Clone()
	{
		List<QuickScanEntry> entries = new(Entries.Count);
		foreach (QuickScanEntry entry in Entries)
		{
			entries.Add(new QuickScanEntry
			{
				Name = entry.Name,
				Group = entry.Group,
				SubGroup = entry.SubGroup,
				Description = entry.Description,
				Command = entry.Command,
				Key = entry.Key,
				Index = entry.Index,
				ParamBytes = (byte[])entry.ParamBytes?.Clone() ?? Array.Empty<byte>(),
				ExpectIndexEcho = entry.ExpectIndexEcho,
				RequiresParam = entry.RequiresParam,
				Mode = entry.Mode,
				Rules = entry.Rules?.Select(r => new RangeRule
				{
					Label = r.Label,
					Offset = r.Offset,
					Length = r.Length,
					Min = r.Min,
					Max = r.Max,
					AllowedValues = (byte[])r.AllowedValues?.Clone(),
				}).ToList() ?? new(),
				CompareLength = entry.CompareLength,
				Baseline = (byte[])entry.Baseline?.Clone(),
				BaselineCapturedUtc = entry.BaselineCapturedUtc,
				Enabled = entry.Enabled,
				TimeoutMs = entry.TimeoutMs,
			});
		}

		return new QuickScanScenario
		{
			Id = Id,
			Name = Name,
			ModelName = ModelName,
			VID = VID,
			PID = PID,
			BuiltIn = BuiltIn,
			Version = Version,
			Entries = entries,
		};
	}
}
