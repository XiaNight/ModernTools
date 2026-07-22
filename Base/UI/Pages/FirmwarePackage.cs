using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace Base.Pages;

/// <summary>
/// Result of parsing a firmware package name into a human-readable device name and version.
/// </summary>
public sealed class FirmwareParseResult
{
	public string DeviceName { get; init; } = string.Empty;
	public string Version { get; init; } = string.Empty;
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
			return new FirmwareParseResult
			{
				DeviceName = tokens.Length > 0 ? string.Join(" ", tokens) : rawName ?? string.Empty,
				Version = string.Empty
			};
		}

		string deviceName = versionIndex > 0
			? string.Join(" ", tokens[..versionIndex])
			: (rawName ?? string.Empty);

		List<string> segments = new()
		{
			tokens[versionIndex].Substring(1) // strip the leading 'V'
		};

		for (int i = versionIndex + 1; i < tokens.Length; i++)
		{
			if (!IsVersionSegment(tokens[i]))
				break;

			segments.Add(tokens[i]);
		}

		return new FirmwareParseResult
		{
			DeviceName = deviceName,
			Version = string.Join(".", segments)
		};
	}

	/// <summary>A version head is 'V' (or 'v') followed by one or more digits, e.g. "V96".</summary>
	private static bool IsVersionHead(string token)
	{
		if (token.Length < 2)
			return false;

		if (token[0] != 'V' && token[0] != 'v')
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
	public string Version { get; init; } = string.Empty;
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

	public string VersionDisplay => string.IsNullOrEmpty(Version) ? "—" : Version;
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
	public const string DeviceBatName = "fw_update_device.bat";
	public const string DongleBatName = "fw_update_dongle.bat";
	public const string FotaBatName = "fw_update_device_FOTA.bat";

	public static List<FirmwarePackage> Scan(string folder)
	{
		List<FirmwarePackage> result = new();

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

			string deviceBat = acc.HasUnzipped ? FindBat(acc.FolderPath, DeviceBatName) : null;
			string dongleBat = acc.HasUnzipped ? FindBat(acc.FolderPath, DongleBatName) : null;
			string fotaBat = acc.HasUnzipped ? FindBat(acc.FolderPath, FotaBatName) : null;

			// Keep the list to genuine firmware: either the name parsed to a version, or the
			// unzipped folder contains one of the known update batch files.
			bool looksLikeFirmware = !string.IsNullOrEmpty(parsed.Version)
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
	private static string FindBat(string folder, string fileName)
	{
		try
		{
			string direct = Path.Combine(folder, fileName);
			if (File.Exists(direct))
				return direct;

			return Directory
				.EnumerateFiles(folder, fileName, SearchOption.AllDirectories)
				.FirstOrDefault(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase));
		}
		catch
		{
			return null;
		}
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
