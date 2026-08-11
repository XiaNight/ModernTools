using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;

namespace Base.Pages;

/// <summary>
/// Result of parsing a firmware package name into a human-readable device name and version.
/// </summary>
public sealed class FirmwareParseResult(string deviceName, Version version)
{
	public string DeviceName { get; init; } = deviceName;
	public Version Version { get; init; } = version;
}

public readonly partial struct Version(int major, int minor, int patch) : IComparable<Version>
{
	public int Major { get; init; } = major;
	public int Minor { get; init; } = minor;
	public int Patch { get; init; } = patch;

	public static Version Convert(string[] segments)
	{
		int major = segments.Length > 0 && int.TryParse(segments[0], out int m) ? m : 0;
		int minor = segments.Length > 1 && int.TryParse(segments[1], out int n) ? n : 0;
		int patch = segments.Length > 2 && int.TryParse(segments[2], out int p) ? p : 0;
		return new Version(major, minor, patch);
	}

	[GeneratedRegex(@"^[Vv]?(\d+)[^\d]*(\d+)[^\d]*(\d+)")]
	private static partial Regex VersionRegex();

	/// <summary>
	/// Tries to parse a version string into a Version struct.
	/// Ignoring any leading 'V' or 'v' and splitting by any none integers.
	/// </summary>
	/// <param name="raw"></param>
	/// <param name="version"></param>
	/// <returns></returns>
	public static bool TryParse(string raw, out Version version)
	{
		version = default;

		if (string.IsNullOrWhiteSpace(raw))
			return false;

		Match match = VersionRegex().Match(raw);
		if (match.Success)
		{
			int major = int.TryParse(match.Groups[1].Value, out int m) ? m : 0;
			int minor = int.TryParse(match.Groups[2].Value, out int n) ? n : 0;
			int patch = int.TryParse(match.Groups[3].Value, out int p) ? p : 0;
			version = new Version(major, minor, patch);
			return true;
		}
		else
		{
			return false;
		}
	}

	public bool IsValid => Major > 0 || Minor > 0 || Patch > 0;

	public readonly int CompareTo(Version other)
	{
		int cmp = Major.CompareTo(other.Major);
		if (cmp != 0) return cmp;
		cmp = Minor.CompareTo(other.Minor);
		return cmp != 0 ? cmp : Patch.CompareTo(other.Patch);
	}

	public override readonly string ToString()
	{
		return IsValid ? $"V{Major:D2}_{Minor:D2}_{Patch:D2}" : "--";
	}
}

/// <summary>
/// Parses a Device Firmware Update package name that follows the strict in-house scheme, e.g.
/// <c>FW_update_tool_M708_DEVICE_V96_90_05_991002</c>.
///
/// Rules (agreed with the peripheral team):
/// <list type="bullet">
///   <item>Tokens are separated by '_'. The constant <c>FW_update_tool_</c> prefix is dropped.</item>
///   <item>Everything before the first <c>V##</c> token is the device name.</item>
///   <item>The version is the leading <c>V##</c> token plus the maximal run of following
///         two-digit tokens (each version number is a byte). Single-digit tokens and 3+-digit
///         clumps (e.g. a trailing date like <c>991002</c>) are not version numbers and stop
///         the run.</item>
/// </list>
/// </summary>
public static class FirmwareNameParser
{
	private const string KnownPrefix = "FW_update_tool_";

	public static FirmwareParseResult Parse(string rawName)
	{
		string name = rawName ?? string.Empty;

		if (name.StartsWith(KnownPrefix, StringComparison.OrdinalIgnoreCase))
			name = name.Substring(KnownPrefix.Length);

		string[] tokens = name.Split('_', StringSplitOptions.RemoveEmptyEntries);

		int versionIndex = -1;
		for (int i = 0; i < tokens.Length; i++)
		{
			if (IsVersionHead(tokens[i]))
			{
				versionIndex = i;
				break;
			}
		}

		if (versionIndex < 0)
		{
			// No version token at all: treat the whole (prefix-stripped) name as the device name.
			return new FirmwareParseResult(tokens.Length > 0 ? string.Join(" ", tokens) : rawName ?? string.Empty, default);
		}

		string deviceName = versionIndex > 0
			? string.Join(" ", tokens[..versionIndex])
			: (rawName ?? string.Empty);

		List<string> segments =
		[
			tokens[versionIndex].Substring(1) // strip the leading 'V'
		];

		for (int i = versionIndex + 1; i < tokens.Length; i++)
		{
			if (!IsVersionSegment(tokens[i]))
				break;

			segments.Add(tokens[i]);
		}

		return new FirmwareParseResult(deviceName, Version.Convert(segments.ToArray()));
	}

	/// <summary>A version head is 'V' (or 'v') followed by one or more digits, e.g. "V96".</summary>
	private static bool IsVersionHead(string token)
	{
		if (token.Length < 2)
			return false;

		if (token[0] is not 'V' and not 'v')
			return false;

		for (int i = 1; i < token.Length; i++)
		{
			if (!char.IsDigit(token[i]))
				return false;
		}

		return true;
	}

	/// <summary>A version segment is exactly two digits (a zero-padded byte).</summary>
	private static bool IsVersionSegment(string token)
	{
		return token.Length == 2 && char.IsDigit(token[0]) && char.IsDigit(token[1]);
	}
}

/// <summary>
/// One firmware package as shown in the list. A package is keyed by its base name and may exist
/// as a zip, an unzipped folder, or both simultaneously.
/// </summary>
public sealed class FirmwarePackage : INotifyPropertyChanged
{
	public string BaseName { get; init; } = string.Empty;
	public string DeviceName { get; init; } = string.Empty;
	public Version Version { get; init; } = default;
	public string FullName { get; init; } = string.Empty;

	public bool HasZip { get; init; }
	public bool HasUnzipped { get; init; }
	public string ZipPath { get; init; }
	public string FolderPath { get; init; }

	public DateTime Created { get; init; }
	public DateTime Modified { get; init; }

	public bool HasDeviceBat { get; init; }
	public bool HasDongleBat { get; init; }
	public bool HasFotaBat { get; init; }
	public string DeviceBatPath { get; init; }
	public string DongleBatPath { get; init; }
	public string FotaBatPath { get; init; }

	private bool isBusy;

	/// <summary>True while one of this package's batch files is running; hides the action buttons.</summary>
	public bool IsBusy
	{
		get => isBusy;
		set
		{
			if (isBusy == value)
				return;

			isBusy = value;
			OnPropertyChanged(nameof(IsBusy));
			OnPropertyChanged(nameof(IsIdle));
			OnPropertyChanged(nameof(CanUnzip));
			OnPropertyChanged(nameof(CanUpdateDevice));
			OnPropertyChanged(nameof(CanUpdateDongle));
			OnPropertyChanged(nameof(CanUpdateFota));
			OnPropertyChanged(nameof(CanDeleteZip));
		}
	}

	public bool IsIdle => !isBusy;

	public string VersionDisplay => Version.IsValid ? Version.ToString() : "—";
	public string CreatedText => Created.ToString("yyyy-MM-dd HH:mm");
	public string ModifiedText => Modified.ToString("yyyy-MM-dd HH:mm");
	public string StateText => HasUnzipped ? (HasZip ? "Unzipped (+ ZIP)" : "Unzipped") : "Zipped";

	public bool CanUnzip => HasZip && !HasUnzipped && !IsBusy;
	public bool CanUpdateDevice => HasUnzipped && HasDeviceBat && !IsBusy;
	public bool CanUpdateDongle => HasUnzipped && HasDongleBat && !IsBusy;
	public bool CanUpdateFota => HasUnzipped && HasFotaBat && !IsBusy;
	public bool CanDeleteZip => HasZip && HasUnzipped && !IsBusy;

	public event PropertyChangedEventHandler PropertyChanged;

	private void OnPropertyChanged(string propertyName)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}

/// <summary>
/// Scans a folder for firmware packages, grouping a zip and its unzipped folder under one entry
/// and detecting the per-package update batch files.
/// </summary>
public static class FirmwareScanner
{
	private static readonly string[] deviceBatNames = ["fw_update_device.bat", "Gamepad_FW_Update.bat"];
	private static readonly string[] dongleBatNames = ["fw_update_dongle.bat", "Dongle_FW_Update.bat"];
	private static readonly string[] fotaBatNames = ["fw_update_device_FOTA.bat"];

	public static List<FirmwarePackage> Scan(string folder)
	{
		List<FirmwarePackage> result = [];

		if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
			return result;

		Dictionary<string, Accumulator> map = new(StringComparer.OrdinalIgnoreCase);

		foreach (string zip in Directory.EnumerateFiles(folder, "*.zip", SearchOption.TopDirectoryOnly))
		{
			Accumulator acc = GetOrAdd(map, Path.GetFileNameWithoutExtension(zip));
			acc.HasZip = true;
			acc.ZipPath = zip;
		}

		foreach (string dir in Directory.EnumerateDirectories(folder, "*", SearchOption.TopDirectoryOnly))
		{
			Accumulator acc = GetOrAdd(map, Path.GetFileName(dir));
			acc.HasUnzipped = true;
			acc.FolderPath = dir;
		}

		foreach (KeyValuePair<string, Accumulator> entry in map)
		{
			Accumulator acc = entry.Value;
			FirmwareParseResult parsed = FirmwareNameParser.Parse(entry.Key);

			string deviceBat = acc.HasUnzipped ? FindBat(acc.FolderPath, deviceBatNames) : null;
			string dongleBat = acc.HasUnzipped ? FindBat(acc.FolderPath, dongleBatNames) : null;
			string fotaBat = acc.HasUnzipped ? FindBat(acc.FolderPath, fotaBatNames) : null;

			// Keep the list to genuine firmware: either the name parsed to a version, or the
			// unzipped folder contains one of the known update batch files.
			bool looksLikeFirmware = parsed.Version.IsValid
				|| deviceBat != null || dongleBat != null || fotaBat != null;

			if (!looksLikeFirmware)
				continue;

			(DateTime created, DateTime modified) = GetTimestamps(acc);

			result.Add(new FirmwarePackage
			{
				BaseName = entry.Key,
				DeviceName = parsed.DeviceName,
				Version = parsed.Version,
				FullName = entry.Key,
				HasZip = acc.HasZip,
				HasUnzipped = acc.HasUnzipped,
				ZipPath = acc.ZipPath,
				FolderPath = acc.FolderPath,
				Created = created,
				Modified = modified,
				HasDeviceBat = deviceBat != null,
				HasDongleBat = dongleBat != null,
				HasFotaBat = fotaBat != null,
				DeviceBatPath = deviceBat,
				DongleBatPath = dongleBat,
				FotaBatPath = fotaBat
			});
		}

		return result;
	}

	// Prefer the zip's timestamps (the developer usually just downloaded it); fall back to the folder.
	private static (DateTime created, DateTime modified) GetTimestamps(Accumulator acc)
	{
		try
		{
			if (acc.HasZip)
			{
				FileInfo fi = new(acc.ZipPath);
				return (fi.CreationTime, fi.LastWriteTime);
			}

			DirectoryInfo di = new(acc.FolderPath);
			return (di.CreationTime, di.LastWriteTime);
		}
		catch
		{
			return (DateTime.MinValue, DateTime.MinValue);
		}
	}

	// The batch file may sit at the folder root or one level down after extraction.
	private static string FindBat(string folder, string[] fileNames)
	{
		try
		{
			foreach (string fileName in fileNames)
			{
				string direct = Path.Combine(folder, fileName);

				if (File.Exists(direct))
					return direct;

				string result = Directory.EnumerateFiles(folder, fileName, SearchOption.AllDirectories)
					.FirstOrDefault(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase));

				if (!string.IsNullOrEmpty(result)) return result;
			}
		}
		catch { }
		return null;
	}

	private static Accumulator GetOrAdd(Dictionary<string, Accumulator> map, string key)
	{
		if (!map.TryGetValue(key, out Accumulator acc))
		{
			acc = new Accumulator();
			map[key] = acc;
		}

		return acc;
	}

	private sealed class Accumulator
	{
		public bool HasZip;
		public bool HasUnzipped;
		public string ZipPath;
		public string FolderPath;
	}
}
