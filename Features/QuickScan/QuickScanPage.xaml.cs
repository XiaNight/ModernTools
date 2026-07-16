using Base.Core;
using Base.Pages;
using Base.Protocol;
using Base.Services;
using Base.Services.Peripheral;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;

namespace QuickScan;

/// <summary>
/// QuickScan page: scenario picker, run / capture-baseline controls, and the scrollable
/// list of per-protocol rows. A XAML-backed <see cref="PageBase"/> (root is base:PageBase),
/// mirroring the pattern used by ASUSBusHoundPage / RelayControlPage.
///
/// Import/export and control-state helpers live in QuickScanPage.ImportExport.cs.
/// </summary>
[PageInfo("Quick Scan",
	Glyph = "\uE721",
	ShortName = "Quick Scan",
	Description = "Scan a model's Get protocols and report pass/fail.",
	Path = ["Keyboard"])]
public partial class QuickScanPage : PageBase
{
	private List<QuickScanScenario> scenarios = new();
	private QuickScanScenario currentScenario;
	private readonly List<QuickScanEntryControl> rows = new();
	private readonly Dictionary<QuickScanEntry, QuickScanEntryControl> rowByEntry = new();

	private PeripheralInterface activeInterface;
	private CancellationTokenSource scanCts;
	private bool isScanning;
	private bool suppressComboEvent;

	public QuickScanPage()
	{
		InitializeComponent();
	}

	// ---- page lifecycle ----

	protected override void OnEnable()
	{
		base.OnEnable();
		DeviceSelection.Instance.OnActiveDeviceConnected += OnDeviceChanged;
		DeviceSelection.Instance.OnActiveDeviceDisconnected += OnDeviceChanged;
		ReloadScenarios();
		UpdateControlState();
	}

	protected override void OnDisable()
	{
		base.OnDisable();
		DeviceSelection.Instance.OnActiveDeviceConnected -= OnDeviceChanged;
		DeviceSelection.Instance.OnActiveDeviceDisconnected -= OnDeviceChanged;
		CancelScan();
		activeInterface = null;
	}

	private void OnDeviceChanged()
	{
		activeInterface = null;
		ReloadScenarios();
		UpdateControlState();
	}

	// ---- scenario loading ----

	private void ReloadScenarios()
	{
		scenarios = QuickScanStore.GetAll();

		suppressComboEvent = true;
		ScenarioCombo.ItemsSource = scenarios;
		suppressComboEvent = false;

		QuickScanScenario select = null;
		if (ActiveDevice != null)
		{
			QuickScanScenario match = QuickScanStore.SelectForDevice(ActiveDevice.VID, ActiveDevice.PID, ActiveDevice.productName);
			if (match != null)
				select = scenarios.FirstOrDefault(s => s.Id == match.Id);
		}
		select ??= scenarios.FirstOrDefault();

		if (select != null)
		{
			suppressComboEvent = true;
			ScenarioCombo.SelectedItem = select;
			suppressComboEvent = false;
			LoadScenario(select);
		}
	}

	private void ScenarioCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (suppressComboEvent) return;
		if (ScenarioCombo.SelectedItem is QuickScanScenario scenario)
			LoadScenario(scenario);
	}

	private void LoadScenario(QuickScanScenario scenario)
	{
		currentScenario = scenario;
		BuildRows();
		SummaryText.Text = $"{scenario.Entries.Count} protocols · {scenario.Entries.Count(en => en.Enabled)} enabled";
		ProgressText.Text = "";
		UpdateControlState();
	}

	private void BuildRows()
	{
		ListPanel.Children.Clear();
		rows.Clear();
		rowByEntry.Clear();
		if (currentScenario == null) return;

		List<QuickScanEntry> entries = currentScenario.Entries;
		string lastGroup = null;
		int i = 0;

		while (i < entries.Count)
		{
			QuickScanEntry entry = entries[i];

			// Section header when the top-level group changes.
			if (entry.Group != lastGroup)
			{
				lastGroup = entry.Group;
				ListPanel.Children.Add(new TextBlock
				{
					Text = lastGroup,
					FontWeight = FontWeights.Bold,
					Margin = new Thickness(2, 12, 0, 4),
					Opacity = 0.9,
				});
			}

			if (!string.IsNullOrEmpty(entry.SubGroup))
			{
				// Consecutive entries sharing the sub-group render inside one group control.
				string sub = entry.SubGroup;
				QuickScanGroupControl group = new();
				group.SetHeader(sub);

				while (i < entries.Count && entries[i].Group == lastGroup && entries[i].SubGroup == sub)
				{
					group.AddRow(AddRow(entries[i]));
					i++;
				}

				group.SyncMaster();
				ListPanel.Children.Add(group);
			}
			else
			{
				ListPanel.Children.Add(AddRow(entry));
				i++;
			}
		}
	}

	private QuickScanEntryControl AddRow(QuickScanEntry entry)
	{
		QuickScanEntryControl row = new();
		row.Bind(entry);
		rows.Add(row);
		rowByEntry[entry] = row;
		return row;
	}

	// ---- scanning ----

	private async void Run_Click(object sender, RoutedEventArgs e) => await RunScanSafe(false);

	private async void Capture_Click(object sender, RoutedEventArgs e) => await RunScanSafe(true);

	private void Cancel_Click(object sender, RoutedEventArgs e) => CancelScan();

	private async Task RunScanSafe(bool captureBaseline)
	{
		try
		{
			await RunScan(captureBaseline);
		}
		catch (Exception ex)
		{
			Debug.Log($"[QuickScan] scan failed: {ex.Message}");
			Dispatcher.Invoke(() =>
			{
				SummaryText.Text = $"Error: {ex.Message}";
				isScanning = false;
				UpdateControlState();
			});
		}
	}

	private async Task RunScan(bool captureBaseline)
	{
		if (isScanning || currentScenario == null) return;
		if (!EnsureInterface())
		{
			SummaryText.Text = "No compatible device interface. Connect a device first.";
			return;
		}

		// Persist current edits (enable / mode / params) before running.
		QuickScanStore.Save(currentScenario);

		List<QuickScanEntry> enabled = currentScenario.Entries.Where(en => en.Enabled).ToList();
		if (enabled.Count == 0)
		{
			SummaryText.Text = "No entries enabled.";
			return;
		}

		foreach (QuickScanEntryControl row in rows) row.Reset();

		scanCts = new CancellationTokenSource();
		isScanning = true;
		UpdateControlState();

		QuickScanRunner runner = new();
		List<ScanResult> collected = new();
		runner.OnEntryStarted += (index, total, entry) => Dispatcher.Invoke(() =>
		{
			ProgressText.Text = $"Scanning {index + 1}/{total}: {entry.Name}";
			if (rowByEntry.TryGetValue(entry, out QuickScanEntryControl row)) row.ShowScanning();
		});
		runner.OnEntryCompleted += (index, result) => Dispatcher.Invoke(() =>
		{
			collected.Add(result);
			if (index >= 0 && index < enabled.Count && rowByEntry.TryGetValue(enabled[index], out QuickScanEntryControl row))
				row.ShowResult(result);
		});

		bool cancelled = false;
		string error = null;
		try
		{
			await runner.RunAsync(activeInterface, enabled, captureBaseline, scanCts.Token);
		}
		catch (OperationCanceledException)
		{
			cancelled = true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
		}
		finally
		{
			isScanning = false;
			scanCts?.Dispose();
			scanCts = null;

			if (captureBaseline)
				QuickScanStore.Save(currentScenario);

			Dispatcher.Invoke(() =>
			{
				if (captureBaseline)
				{
					foreach (QuickScanEntryControl row in rows) row.RefreshBaseline();
				}

				int pass = collected.Count(r => r.Verdict == ScanVerdict.Pass);
				int fail = collected.Count(r => r.Verdict == ScanVerdict.Fail);
				string action = captureBaseline ? "Baseline capture" : "Scan";
				string state = cancelled ? "cancelled" : error != null ? "failed" : "complete";
				SummaryText.Text = $"{action} {state} — {pass} pass · {fail} fail · {collected.Count} run";
				ProgressText.Text = error != null ? $"Error: {error}" : "";
				UpdateControlState();
			});
		}
	}

	private void CancelScan()
	{
		try { scanCts?.Cancel(); }
		catch { }
	}

	private bool EnsureInterface()
	{
		if (activeInterface != null && activeInterface.IsDeviceConnected) return true;
		if (ActiveDevice == null) return false;
		activeInterface = QuickScanRunner.ResolveVendorInterface(ActiveDevice);
		return activeInterface != null;
	}
	private void Import_Click(object sender, RoutedEventArgs e)
	{
		OpenFileDialog dialog = new()
		{
			Filter = "QuickScan scenario (*.json)|*.json|All files (*.*)|*.*",
			Title = "Import QuickScan scenario",
		};
		if (dialog.ShowDialog() != true) return;

		try
		{
			QuickScanScenario imported = QuickScanStore.Import(dialog.FileName);
			ReloadScenarios();
			QuickScanScenario match = scenarios.FirstOrDefault(s => s.Id == imported.Id);
			if (match != null)
			{
				suppressComboEvent = true;
				ScenarioCombo.SelectedItem = match;
				suppressComboEvent = false;
				LoadScenario(match);
			}
			SummaryText.Text = $"Imported '{imported.Name}'.";
		}
		catch (Exception ex)
		{
			SummaryText.Text = $"Import failed: {ex.Message}";
		}
	}

	private void Export_Click(object sender, RoutedEventArgs e)
	{
		if (currentScenario == null) return;

		SaveFileDialog dialog = new()
		{
			Filter = "QuickScan scenario (*.json)|*.json",
			Title = "Export QuickScan scenario",
			FileName = $"{currentScenario.Name}.json",
		};
		if (dialog.ShowDialog() != true) return;

		try
		{
			QuickScanStore.Save(currentScenario);
			QuickScanStore.Export(currentScenario, dialog.FileName);
			SummaryText.Text = $"Exported to {dialog.FileName}";
		}
		catch (Exception ex)
		{
			SummaryText.Text = $"Export failed: {ex.Message}";
		}
	}

	private void UpdateControlState()
	{
		bool hasDevice = ActiveDevice != null;
		bool hasScenario = currentScenario != null;

		RunButton.IsEnabled = hasDevice && hasScenario && !isScanning;
		CaptureButton.IsEnabled = hasDevice && hasScenario && !isScanning;
		CancelButton.IsEnabled = isScanning;
		ExportButton.IsEnabled = hasScenario;
		ImportButton.IsEnabled = !isScanning;
		ScenarioCombo.IsEnabled = !isScanning;
	}
}
