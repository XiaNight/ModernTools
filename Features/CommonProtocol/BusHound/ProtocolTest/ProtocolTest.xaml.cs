
using Base.Core;
using Base.Services;
using Base.Services.Peripheral;
using CommonProtocol;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using static Base.Services.DeviceSelection;

using CommonProtocol.BusHound.ProtocolTest;

namespace CommonProtocol.BusHound;
using Debug = Base.Services.Debug;

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

	// Extra wall-clock time, beyond the test's budget, that the read is allowed to block before it
	// gives up waiting for a reply. This only guards against a silent device — the pass/fail timeout
	// is judged from packet arrival timestamps, so UI/await scheduling latency never fails a reply
	// that actually arrived within budget.
	private const int TimeoutSafetySlackMs = 250;

	private List<TestProtocol> testsList = new();
	private readonly Dictionary<TestProtocol, ProtocolTestEntry> testRows = new();
	private bool testsRunning;

	// Recording state: an active edit dialog capturing live packets into its expected field.
	private bool recording;
	private ProtocolTestEditDialog recordDialog;
	private PeripheralInterface recordingInterface;
	private Action<ReadOnlyMemory<byte>, DateTime> recordHandler;

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

		foreach (TestProtocol test in testsList)
		{
			ProtocolTestEntry row = new();
			row.Bind(test);
			row.SendRequested += (sender, _) => RunSingleTest(sender as ProtocolTestEntry);
			row.EditRequested += (sender, _) => EditTest(sender as ProtocolTestEntry);
			row.ViewRequested += (sender, _) => ViewTest(sender as ProtocolTestEntry);
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
		TestProtocol test = new();
		if (await OpenEditor(test))
		{
			testsList.Add(test);
			SaveTests();
			BuildTestRows();
		}
	}

	private async void EditTest(ProtocolTestEntry row)
	{
		if (row?.Test == null) return;

		if (await OpenEditor(row.Test))
		{
			SaveTests();
			row.Bind(row.Test);
			UpdateTestsSummary();
		}
	}

	/// <summary>Opens the editor, wiring its Record button to the live capture, and always stops recording on close.</summary>
	private async Task<bool> OpenEditor(TestProtocol test)
	{
		ProtocolTestEditDialog dialog = new();
		dialog.RecordStartRequested += (_, _) => StartRecording(dialog);
		dialog.RecordStopRequested += (_, _) => StopRecording();
		try
		{
			return await dialog.EditAsync(test);
		}
		finally
		{
			StopRecording();
		}
	}

	private async void ViewTest(ProtocolTestEntry row)
	{
		if (row?.Test == null) return;

		ProtocolTestViewDialog dialog = new();
		await dialog.ShowForAsync(row.Test, row.LastReceived);
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
			foreach (TestProtocol test in testsList)
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
	/// Runs a test and reflects the outcome on its row (running state, received packets, verdict).
	/// The actual send/match work lives in <see cref="ExecuteTestAsync"/>; this just drives the UI.
	/// Returns the result so callers (including the API) can report it.
	/// </summary>
	private async Task<TestRunResult> RunTestAsync(TestProtocol test, ProtocolTestEntry row)
	{
		if (test == null)
			return new TestRunResult { Verdict = TestVerdict.Error, Message = "No test." };

		row?.ShowRunning();

		TestRunResult result = await ExecuteTestAsync(test, packets => row?.ShowReceived(packets));

		if (row != null)
		{
			row.ShowReceived(result.Received);
			row.ShowResult(result.Verdict, result.Message, TimeSpan.FromMilliseconds(result.ElapsedMs));
		}

		return result;
	}

	/// <summary>
	/// Sends the request, then reads and matches one packet per expected line, with no UI dependency.
	/// All reads share the test's total timeout budget; the first mismatch, over-budget reply, or
	/// silent device fails. Received packets are reported through <paramref name="onProgress"/> as they
	/// arrive. Timing is taken from the packet I/O events (OnDataSent / OnDataReceived) captured on the
	/// device thread — never wall-clock around the awaits — so scheduling latency can't skew it.
	/// </summary>
	private async Task<TestRunResult> ExecuteTestAsync(TestProtocol test, Action<IReadOnlyList<byte[]>> onProgress = null)
	{
		TestRunResult result = new();
		List<byte[]> received = result.Received;

		byte[] request = ParseCommand(test.RequestHex);
		if (request == null || request.Length == 0)
		{
			result.Verdict = TestVerdict.Error;
			result.Message = "Invalid or empty request bytes.";
			return result;
		}

		List<ExpectedPacket> expected = new();
		for (int i = 0; i < test.ExpectedLines.Count; i++)
		{
			if (!ExpectedPacket.TryParse(test.ExpectedLines[i], out ExpectedPacket packet, out string parseError))
			{
				result.Verdict = TestVerdict.Error;
				result.Message = $"Expected line {i + 1}: {parseError}";
				return result;
			}

			expected.Add(packet);
		}

		if (expected.Count == 0)
		{
			result.Verdict = TestVerdict.Error;
			result.Message = "No expected packets defined.";
			return result;
		}

		PeripheralInterface targetInterface = ResolveTestInterface();
		if (targetInterface == null)
		{
			result.Verdict = TestVerdict.Error;
			result.Message = "Not connected — start capturing a device first.";
			return result;
		}

		ConcurrentQueue<(byte[] data, DateTime time)> rx = new();
		SemaphoreSlim rxSignal = new(0);
		DateTime sentTime = default;

		Action<ReadOnlyMemory<byte>, DateTime> sentProbe = (_, t) =>
		{
			if (sentTime == default) sentTime = t;
		};
		Action<ReadOnlyMemory<byte>, DateTime> recvProbe = (data, t) =>
		{
			rx.Enqueue((data.ToArray(), t));
			rxSignal.Release();
		};

		int budgetMs = Math.Max(1, test.TotalTimeoutMs);

		// The token is a generous safety cap (budget + slack) so it only trips when the device is
		// genuinely silent. The real pass/fail timeout is judged from arrival timestamps below.
		using CancellationTokenSource cts = new(budgetMs + TimeoutSafetySlackMs);
		try
		{
			// Drain any stale reports, then start listening for this request's replies only.
			targetInterface.ClearPendingReports();
			targetInterface.OnDataSent += sentProbe;
			targetInterface.OnDataReceived += recvProbe;

			await targetInterface.WriteAsync(request, cts.Token);

			TimeSpan elapsed = TimeSpan.Zero;
			for (int i = 0; i < expected.Count; i++)
			{
				await rxSignal.WaitAsync(cts.Token);
				rx.TryDequeue(out (byte[] data, DateTime time) item);

				byte[] compare = StripReportIdOnMatch && item.data.Length > 0 ? item.data[1..] : item.data;
				if (sentTime != default && item.time >= sentTime)
					elapsed = item.time - sentTime;

				received.Add(compare);
				result.ElapsedMs = elapsed.TotalMilliseconds;
				onProgress?.Invoke(received);

				// Judge the timeout on real device latency (send → arrival), not wall-clock around
				// the awaits — a reply that landed within budget must never be failed as a timeout.
				if (elapsed.TotalMilliseconds > budgetMs)
				{
					result.Verdict = TestVerdict.Timeout;
					result.Message = $"Reply arrived in {elapsed.TotalMilliseconds:0.###} ms, over the {budgetMs} ms budget.";
					return result;
				}

				if (!expected[i].Matches(compare, test.AllowTrailingWildcard))
				{
					result.Verdict = TestVerdict.Mismatch;
					result.Message = $"Packet {i + 1} mismatch. Expected {expected[i]}, got {ByteToString(compare, false)}.";
					return result;
				}
			}

			result.Verdict = TestVerdict.Pass;
			result.Message = $"{expected.Count} packet(s) matched.";
			result.ElapsedMs = elapsed.TotalMilliseconds;
		}
		catch (OperationCanceledException)
		{
			result.Verdict = TestVerdict.Timeout;
			result.Message = $"No reply within {budgetMs} ms.";
		}
		catch (Exception ex)
		{
			result.Verdict = TestVerdict.Error;
			result.Message = ex.Message;
			Debug.Log($"[BusHound.Tests] '{test.Name}' failed: {ex.Message}");
		}
		finally
		{
			targetInterface.OnDataSent -= sentProbe;
			targetInterface.OnDataReceived -= recvProbe;
		}

		return result;
	}

	/// <summary>
	/// Resolves and opens the vendor interface for the active device, matching the page's normal
	/// selection (usage page 0xFF02 for PID 0x1ACE, otherwise 0xFF00; top-level usage 1).
	/// </summary>
	private PeripheralInterface ResolveTestInterface()
	{
		Device device = DeviceSelection.Instance.ActiveDevice;
		if (device == null || device.interfaces.Count == 0)
			return null;

		int usagePage = device.PID == 0x1ACE ? 0xFF02 : 0xFF00;
		IPeripheralDetail deviceInterface = device.interfaces.FirstOrDefault(
			@interface => @interface.UsagePage == usagePage && @interface.Usage == 1,
			device.interfaces[0]);
		if (deviceInterface == null)
			return null;

		return deviceInterface.Connect(true);
	}

	// ---- recording into the edit dialog ----

	/// <summary>
	/// Starts a recording session for the given editor: opens the interface, subscribes to received
	/// packets (appending each into the expected field), and fires the request once.
	/// </summary>
	private void StartRecording(ProtocolTestEditDialog dialog)
	{
		if (recording) return;

		PeripheralInterface targetInterface = ResolveTestInterface();
		if (targetInterface == null)
		{
			dialog.NotifyRecordingStopped("Not connected — start capturing a device first.");
			return;
		}

		recordDialog = dialog;
		recordingInterface = targetInterface;
		recording = true;
		dialog.ClearExpectedForRecording();

		recordHandler = (data, time) =>
		{
			byte[] actual = data.ToArray();
			byte[] line = StripReportIdOnMatch && actual.Length > 0 ? actual[1..] : actual;
			string text = ByteToString(line, false);
			Dispatcher.InvokeAsync(() => recordDialog?.AppendRecordedPacket(text));
		};
		targetInterface.OnDataReceived += recordHandler;

		byte[] request = ParseCommand(dialog.CurrentRequestHex);
		if (request != null && request.Length > 0)
			_ = targetInterface.WriteAsync(request, CancellationToken.None);
	}

	private void StopRecording()
	{
		if (!recording) return;
		recording = false;

		if (recordingInterface != null && recordHandler != null)
			recordingInterface.OnDataReceived -= recordHandler;

		recordHandler = null;
		recordingInterface = null;
		recordDialog?.NotifyRecordingStopped("Recording stopped.");
		recordDialog = null;
	}
}