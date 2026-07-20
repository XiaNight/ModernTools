namespace Base.UI.Pages;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Base.Core;
using Base.Services;
using Base.Services.Peripheral;
using CommonProtocol;

/// <summary>
/// Tests panel for the Bus Hound page. Each test sends a request frame and then reads packets
/// one at a time, comparing every received packet against its expected line (strict byte match,
/// with <c>X</c> as a nibble wildcard). Reads share a single total-timeout budget. Tests are
/// persisted via <see cref="ProtocolTestStore"/>.
/// </summary>
public partial class ASUSBusHoundPage
{
	// Bus Hound's capture log shows IN packets with the leading HID report-ID byte stripped
	// (data[1..]). Matching against that same view lets expected bytes be copied straight from
	// the log. Set to false to compare against the raw report instead.
	private const bool StripReportIdOnMatch = true;

	private List<ProtocolTest> testsList = new();
	private readonly Dictionary<ProtocolTest, ProtocolTestEntry> testRows = new();
	private bool testsRunning;

	/// <summary>Wires the Tests panel controls and loads persisted tests. Called from Awake.</summary>
	private void InitTestsPanel()
	{
		TestsBtn.Click += (_, _) => ShowTestsPanel();
		TestsAddBtn.Click += (_, _) => AddTest();
		TestsRunAllBtn.Click += (_, _) => RunAllTests();
		LoadTests();
	}

	private void ShowTestsPanel()
	{
		devicePanelVisible = false;
		DevicePanel.Visibility = Visibility.Collapsed;
		CapturePanel.Visibility = Visibility.Collapsed;
		TestsPanel.Visibility = Visibility.Visible;
	}

	// ---- loading / persistence ----

	private void LoadTests()
	{
		testsList = ProtocolTestStore.GetAll();
		BuildTestRows();
	}

	private void SaveTests() => ProtocolTestStore.SaveAll(testsList);

	private void BuildTestRows()
	{
		TestsListPanel.Children.Clear();
		testRows.Clear();

		foreach (ProtocolTest test in testsList)
		{
			ProtocolTestEntry row = new();
			row.Bind(test);
			row.SendRequested += (sender, _) => RunSingleTest(sender as ProtocolTestEntry);
			row.EditRequested += (sender, _) => EditTest(sender as ProtocolTestEntry);
			row.DeleteRequested += (sender, _) => DeleteTest(sender as ProtocolTestEntry);
			row.Changed += (_, _) => SaveTests();

			testRows[test] = row;
			TestsListPanel.Children.Add(row);
		}

		UpdateTestsSummary();
	}

	private void UpdateTestsSummary()
	{
		int count = testsList.Count;
		TestsSummaryText.Text = count == 0 ? string.Empty : $"{count} test(s)";
		TestsEmptyText.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
		TestsRunAllBtn.IsEnabled = count > 0 && !testsRunning;
	}

	// ---- add / edit / delete ----

	private async void AddTest()
	{
		ProtocolTest test = new();
		ProtocolTestEditDialog dialog = new();
		if (await dialog.EditAsync(test))
		{
			testsList.Add(test);
			SaveTests();
			BuildTestRows();
		}
	}

	private async void EditTest(ProtocolTestEntry row)
	{
		if (row?.Test == null) return;

		ProtocolTestEditDialog dialog = new();
		if (await dialog.EditAsync(row.Test))
		{
			SaveTests();
			row.Bind(row.Test);
			UpdateTestsSummary();
		}
	}

	private void DeleteTest(ProtocolTestEntry row)
	{
		if (row?.Test == null) return;

		testsList.Remove(row.Test);
		SaveTests();
		BuildTestRows();
	}

	// ---- running ----

	private async void RunSingleTest(ProtocolTestEntry row)
	{
		if (testsRunning || row?.Test == null) return;

		testsRunning = true;
		SetTestsBusy(true);
		try
		{
			await RunTestAsync(row.Test, row);
		}
		finally
		{
			testsRunning = false;
			SetTestsBusy(false);
		}
	}

	private async void RunAllTests()
	{
		if (testsRunning || testsList.Count == 0) return;

		testsRunning = true;
		SetTestsBusy(true);
		try
		{
			foreach (ProtocolTest test in testsList)
			{
				if (testRows.TryGetValue(test, out ProtocolTestEntry row))
					await RunTestAsync(test, row);
			}
		}
		finally
		{
			testsRunning = false;
			SetTestsBusy(false);
		}
	}

	private void SetTestsBusy(bool busy)
	{
		TestsAddBtn.IsEnabled = !busy;
		TestsRunAllBtn.IsEnabled = !busy && testsList.Count > 0;
	}

	/// <summary>
	/// Sends the request, then reads and matches one packet per expected line. All reads share
	/// the test's total timeout budget; the first mismatch, missing packet, or timeout fails.
	/// </summary>
	private async Task RunTestAsync(ProtocolTest test, ProtocolTestEntry row)
	{
		if (test == null || row == null) return;

		byte[] request = ParseCommand(test.RequestHex);
		if (request == null || request.Length == 0)
		{
			row.ShowResult(false, "Invalid or empty request bytes.");
			return;
		}

		List<ExpectedPacket> expected = new();
		for (int i = 0; i < test.ExpectedLines.Count; i++)
		{
			if (!ExpectedPacket.TryParse(test.ExpectedLines[i], out ExpectedPacket packet, out string parseError))
			{
				row.ShowResult(false, $"Expected line {i + 1}: {parseError}");
				return;
			}

			expected.Add(packet);
		}

		if (expected.Count == 0)
		{
			row.ShowResult(false, "No expected packets defined.");
			return;
		}

		PeripheralInterface targetInterface = connectedInterfaces?.FirstOrDefault();
		if (targetInterface == null)
		{
			row.ShowResult(false, "Not connected — start capturing a device first.");
			return;
		}

		row.ShowRunning();

		using CancellationTokenSource cts = new(Math.Max(1, test.TotalTimeoutMs));
		try
		{
			targetInterface.ClearPendingReports();
			await targetInterface.WriteAsync(request, cts.Token);

			for (int i = 0; i < expected.Count; i++)
			{
				ReadOnlyMemory<byte> report = await targetInterface.WaitForNextReportAsync(cts.Token);
				byte[] actual = report.ToArray();
				byte[] compare = StripReportIdOnMatch && actual.Length > 0 ? actual[1..] : actual;

				if (!expected[i].Matches(compare))
				{
					row.ShowResult(false,
						$"Packet {i + 1} mismatch.\nExpected: {expected[i]}\nGot:      {ByteToString(compare, false)}");
					return;
				}
			}

			row.ShowResult(true, $"{expected.Count} packet(s) matched.");
		}
		catch (OperationCanceledException)
		{
			row.ShowResult(false, $"Timed out after {test.TotalTimeoutMs} ms before all packets matched.");
		}
		catch (Exception ex)
		{
			row.ShowResult(false, ex.Message);
			Debug.Log($"[BusHound.Tests] '{test.Name}' failed: {ex.Message}");
		}
	}
}