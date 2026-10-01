using Base.Core;
using Base.Helpers;
using Base.Pages;
using Base.Services.APIService;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Debug = Base.Services.Debug; // Base logger; avoids clashing with System.Diagnostics.Debug.

namespace CommonTestTools;

/// <summary>
/// Lists the Device Firmware Update packages found in a developer-chosen folder. Each package may be
/// a zip, an unzipped folder, or both; the page offers unzip, per-target update (device / dongle /
/// FOTA) and zip cleanup, driven by the batch files inside each unzipped package.
/// </summary>
[PageInfo("Firmware Update",
	Glyph = "\uE777",           // UpdateRestore (Segoe Fluent Icons).
	Description = "Browse and run Device Firmware Update packages.",
	ShowDeviceSelection = false,
	NavOrder = -1)]
public partial class FirmwarePage : PageBase, INotifyPropertyChanged
{
	private string searchText = string.Empty;

	private readonly ObservableCollection<FirmwarePackage> packages = [];

	public ICollectionView PackagesView { get; }
	 
	public ICommand UnzipCommand { get; }
	public ICommand UpdateDeviceCommand { get; }
	public ICommand UpdateDongleCommand { get; }
	public ICommand UpdateFotaCommand { get; }
	public ICommand DeleteZipCommand { get; }
	public ICommand OpenFolderCommand { get; }

	public event PropertyChangedEventHandler PropertyChanged;

	[Persist, Config(Name = "7Zip path", Type = ConfigType.File, FileExtentions = ["exe"])]
	private readonly string SevenZipPath = @"C:\Program Files\7-Zip\7z.exe";

	[Persist, Config]
	private readonly string password = "ASUS";

	public FirmwarePage()
	{
		InitializeComponent();
		DataContext = this;

		PackagesView = CollectionViewSource.GetDefaultView(packages);
		PackagesView.Filter = FilterPackage;
		ApplySort(0);

		RescanBtn.Click += (_, _) => Rescan();

		UnzipCommand = new RelayCommand<FirmwarePackage>(Unzip);
		UpdateDeviceCommand = new RelayCommand<FirmwarePackage>(p => RunBat(p, p?.DeviceBatPath));
		UpdateDongleCommand = new RelayCommand<FirmwarePackage>(p => RunBat(p, p?.DongleBatPath));
		UpdateFotaCommand = new RelayCommand<FirmwarePackage>(p => RunBat(p, p?.FotaBatPath));
		DeleteZipCommand = new RelayCommand<FirmwarePackage>(DeleteZip);
		OpenFolderCommand = new RelayCommand<FirmwarePackage>(OpenFolder);
	}

	[Persist]
	[Config("Firmware Folder Path", Type = ConfigType.Folder, Changed = nameof(Rescan))]
	public string FolderPath;

	public string SearchText
	{
		get => searchText;
		set
		{
			string next = value ?? string.Empty;
			if (string.Equals(searchText, next, StringComparison.Ordinal))
				return;

			searchText = next;
			OnPropertyChanged();
			PackagesView.Refresh();
			UpdateEmptyState();
		}
	}

	public override void Awake()
	{
		base.Awake();
	}

	protected override void OnEnable()
	{
		base.OnEnable();

		Rescan();
	}

	#region ---- Folder / list plumbing ----

	private void Sort_Changed(object sender, SelectionChangedEventArgs e)
	{
		if (PackagesView == null)
			return;

		ApplySort(SortBox.SelectedIndex);
	}

	[GET(requireMainThread: true), AppMenuItem("Rescan")]
	private void Rescan()
	{
		List<FirmwarePackage> found = FirmwareScanner.Scan(FolderPath);

		packages.Clear();
		foreach (FirmwarePackage package in found)
			packages.Add(package);

		ApplySort(SortBox?.SelectedIndex ?? 0);
		PackagesView?.Refresh();
		UpdateEmptyState();
	}

	private void ApplySort(int index)
	{
		if (PackagesView == null)
			return;

		PackagesView.SortDescriptions.Clear();

		switch (index)
		{
			case 1:
				PackagesView.SortDescriptions.Add(new SortDescription(nameof(FirmwarePackage.Modified), ListSortDirection.Ascending));
				break;
			case 2:
				PackagesView.SortDescriptions.Add(new SortDescription(nameof(FirmwarePackage.Created), ListSortDirection.Descending));
				break;
			case 3:
				PackagesView.SortDescriptions.Add(new SortDescription(nameof(FirmwarePackage.DeviceName), ListSortDirection.Ascending));
				break;
			case 4:
				PackagesView.SortDescriptions.Add(new SortDescription(nameof(FirmwarePackage.Version), ListSortDirection.Ascending));
				break;
			default:
				// Default: most recently modified first (typically the freshest download).
				PackagesView.SortDescriptions.Add(new SortDescription(nameof(FirmwarePackage.Modified), ListSortDirection.Descending));
				break;
		}
	}

	#endregion

	#region --- Search / filter plumbing ---

	private FirmwarePackage FindPackage(string deviceName, string version)
	{
		if (string.IsNullOrWhiteSpace(deviceName) || string.IsNullOrWhiteSpace(version))
			return null;
		List<FirmwarePackage> list = [.. packages];
		FilterDeviceName(list, deviceName);
		FilterVersion(list, version);
		return list.FirstOrDefault();
	}

	[POST("FindPackage")]
	private ApiResponse FindPackageApi(string deviceName, string version)
	{
		FirmwarePackage package = FindPackage(deviceName, version);
		return package == null
			? new ApiResponse() { Status = 404, Data = "Package not found." }
			: new ApiResponse() { Status = 200, Data = package };
	}

	private static void FilterDeviceName(in List<FirmwarePackage> list, string query)
	{
		if (list == null) return;
		_ = list.RemoveAll(p => !Contains(p.DeviceName, query));
	}

	private static void FilterVersion(in List<FirmwarePackage> list, string query)
	{
		if (list == null) return;
		_ = list.RemoveAll(p => !Contains(p.Version, query));
	}

	private static void FilterFullName(in List<FirmwarePackage> list, string query)
	{
		if (list == null) return;
		_ = list.RemoveAll(p => !Contains(p.FullName, query));
	}

	private bool FilterPackage(object item)
	{
		if (string.IsNullOrWhiteSpace(searchText))
			return true;

		if (item is not FirmwarePackage package)
			return false;

		string query = searchText.Trim();

		return Contains(package.DeviceName, query)
			|| Contains(package.Version, query)
			|| Contains(package.FullName, query);
	}

	private static bool Contains(Base.Pages.Version version, string query)
	{
		bool success = Base.Pages.Version.TryParse(query, out Base.Pages.Version parsed);
		return success && version.Equals(parsed);
	}

	private static bool Contains(string source, string query)
	{
		return !string.IsNullOrEmpty(source)
			&& source.Contains(query, StringComparison.OrdinalIgnoreCase);
	}

	private void UpdateEmptyState()
	{
		if (EmptyPlaceholder == null)
			return;

		bool hasVisibleItems = PackagesView != null && PackagesView.Cast<object>().Any();
		EmptyPlaceholder.Visibility = hasVisibleItems ? Visibility.Collapsed : Visibility.Visible;
	}

	#endregion

	// ---- Actions ----

	[POST]
	private ApiResponse RunDeviceUpdate(string deviceName, string version)
	{
		FirmwarePackage package = FindPackage(deviceName, version);

		if (package == null) return new ApiResponse() { Status = 404, Data = "Package not found." };

		if (!string.IsNullOrEmpty(package.DeviceBatPath) && File.Exists(package.DeviceBatPath))
		{
			RunBat(package, package.DeviceBatPath);
			return new ApiResponse() { Status = 200, Data = "Device update started." };
		}
		else
		{
			return new ApiResponse() { Status = 404, Data = "Device update batch file not found." };
		}
	}

	private bool Is7ZipInstalled()
	{
		return File.Exists(SevenZipPath);
	}

	private void Unzip(FirmwarePackage package)
	{
		if (package == null || !package.HasZip || string.IsNullOrEmpty(package.ZipPath))
			return;

		if (!Is7ZipInstalled())
		{
			Debug.Log("Firmware unzip failed: 7-Zip not found.", SevenZipPath);

			_ = MessageBox.Show(
				$"7-Zip was not found.\n\nExpected location:\n{SevenZipPath}",
				"7-Zip Not Found",
				MessageBoxButton.OK,
				MessageBoxImage.Warning);

			return;
		}

		try
		{
			string parent = Path.GetDirectoryName(package.ZipPath);
			string target = Path.Combine(parent ?? FolderPath, package.BaseName);

			_ = Directory.CreateDirectory(target);

			ProcessStartInfo psi = new()
			{
				FileName = SevenZipPath,
				Arguments = $"x \"{package.ZipPath}\" -o\"{target}\" -p\"{password}\" -y",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};

			using Process process = Process.Start(psi)!;
			process.WaitForExit();

			if (process.ExitCode != 0)
				throw new InvalidOperationException(process.StandardError.ReadToEnd());

			Debug.Log("Firmware package extracted:", target);
		}
		catch (Exception ex)
		{
			Debug.Log("Firmware unzip failed:", ex.Message);

			_ = MessageBox.Show(
				$"Could not unzip the package.\n\n{ex.Message}",
				"Unzip failed",
				MessageBoxButton.OK,
				MessageBoxImage.Warning);
		}

		Rescan();
	}

	private void DeleteZip(FirmwarePackage package)
	{
		if (package == null || !package.HasZip || string.IsNullOrEmpty(package.ZipPath))
			return;

		MessageBoxResult confirm = MessageBox.Show(
			$"Delete the ZIP for \"{package.DeviceName}\"?\n\n{Path.GetFileName(package.ZipPath)}\n\nThe unzipped folder is kept.",
			"Delete ZIP", MessageBoxButton.YesNo, MessageBoxImage.Question);

		if (confirm != MessageBoxResult.Yes)
			return;

		try
		{
			File.Delete(package.ZipPath);
			Debug.Log("Firmware ZIP deleted:", package.ZipPath);
		}
		catch (Exception ex)
		{
			Debug.Log("Firmware ZIP delete failed:", ex.Message);
			_ = MessageBox.Show($"Could not delete the ZIP.\n\n{ex.Message}",
				"Delete failed", MessageBoxButton.OK, MessageBoxImage.Warning);
		}

		Rescan();
	}

	private async void RunBat(FirmwarePackage package, string batPath)
	{
		if (package == null || string.IsNullOrEmpty(batPath) || !File.Exists(batPath))
			return;

		package.IsBusy = true;
		try
		{
			int exitCode = await Task.Run(() => ExecuteBatch(batPath));
			Debug.Log("Firmware batch finished:", Path.GetFileName(batPath), $"exit={exitCode}");
		}
		catch (Exception ex)
		{
			Debug.Log("Firmware batch failed:", ex.Message);
			_ = MessageBox.Show($"Could not run the update.\n\n{ex.Message}",
				"Update failed", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
		finally
		{
			package.IsBusy = false;
		}
	}

	// Runs the batch hidden with its working directory set to the package folder. Output is captured
	// (so the child streams never block) but intentionally discarded.
	public static int ExecuteBatch(string batPath)
	{
		ProcessStartInfo psi = new()
		{
			FileName = "cmd.exe",
			Arguments = $"/c \"{batPath}\"",
			WorkingDirectory = Path.GetDirectoryName(batPath),
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};

		using Process process = Process.Start(psi);
		if (process == null)
			return -1;

		process.OutputDataReceived += (_, _) => { };
		process.ErrorDataReceived += (_, _) => { };
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();

		// Satisfy "Press any key to continue..."
		process.StandardInput.WriteLine();
		process.StandardInput.Flush();

		process.WaitForExit();

		return process.ExitCode;
	}

	private void OpenFolder(FirmwarePackage package)
	{
		if (package == null || string.IsNullOrEmpty(package.FolderPath) || !Directory.Exists(package.FolderPath))
			return;
		try
		{
			Process.Start(new ProcessStartInfo()
			{
				FileName = package.FolderPath,
				UseShellExecute = true,
				Verb = "open"
			});
		}
		catch (Exception ex)
		{
			Debug.Log("Open folder failed:", ex.Message);
			_ = MessageBox.Show($"Could not open the folder.\n\n{ex.Message}",
				"Open folder failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }


	private void OnPropertyChanged([CallerMemberName] string propertyName = null)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
