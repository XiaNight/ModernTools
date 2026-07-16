using System.Diagnostics;
using Base.Protocol;
using Base.Services;
using Base.Services.Peripheral;

namespace QuickScan;

/// <summary>
/// Executes a scenario against a device: builds each request, sends it through the
/// serialized command queue (<see cref="ProtocolService.SendAsync"/>), validates the
/// reply, and (optionally) captures baselines. Raises per-entry progress events.
/// </summary>
public sealed class QuickScanRunner
{
	// index, total, entry — raised before a command is sent.
	public event Action<int, int, QuickScanEntry> OnEntryStarted;
	// index, result — raised after a command has been validated.
	public event Action<int, ScanResult> OnEntryCompleted;

	public async Task<List<ScanResult>> RunAsync(
		PeripheralInterface device,
		IReadOnlyList<QuickScanEntry> entries,
		bool captureBaseline,
		CancellationToken ct)
	{
		List<ScanResult> results = new();
		if (device == null || entries == null) return results;

		int total = entries.Count;
		for (int i = 0; i < total; i++)
		{
			ct.ThrowIfCancellationRequested();

			QuickScanEntry entry = entries[i];
			OnEntryStarted?.Invoke(i, total, entry);

			byte[] frame = entry.BuildRequest();
			Stopwatch sw = Stopwatch.StartNew();
			byte[] response = null;
			ScanResult result;

			try
			{
				response = await ProtocolService.SendAsync(device, frame, entry.TimeoutMs, ct).ConfigureAwait(false);
				sw.Stop();

				if (captureBaseline && response is { Length: > 0 })
				{
					// Only record a baseline from a structurally-valid reply.
					ScanResult ack = ProtocolValidator.Validate(
						AsStructural(entry), frame, response, sw.Elapsed.TotalMilliseconds);
					if (ack.Verdict == ScanVerdict.Pass)
					{
						entry.Baseline = ProtocolValidator.CaptureBaseline(response, entry.CompareLength);
						entry.BaselineCapturedUtc = DateTime.UtcNow;
					}
				}

				result = ProtocolValidator.Validate(entry.ToCheck(), frame, response, sw.Elapsed.TotalMilliseconds);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				sw.Stop();
				result = new ScanResult
				{
					Name = entry.Name,
					Group = entry.Group,
					Mode = entry.Mode,
					Request = frame,
					Response = response,
					DurationMs = sw.Elapsed.TotalMilliseconds,
					Verdict = ScanVerdict.Fail,
					Failure = ScanFailure.Exception,
					Message = ex.Message,
				};
			}

			results.Add(result);
			OnEntryCompleted?.Invoke(i, result);
		}

		return results;
	}

	private static ProtocolCheck AsStructural(QuickScanEntry entry)
	{
		ProtocolCheck check = entry.ToCheck();
		check.Mode = ValidationMode.Structural;
		return check;
	}

	/// <summary>
	/// Opens the device's vendor HID interface (async-read) for request/response commands,
	/// mirroring how CommonProtocol selects it. Returns null if none is available.
	/// </summary>
	public static PeripheralInterface ResolveVendorInterface(DeviceSelection.Device device)
	{
		if (device == null || device.interfaces.Count == 0) return null;

		int usagePage = device.PID == 0x1ACE ? 0xFF02 : 0xFF00;
		if (device.PID == 0x1C64 || device.PID == 0x1C65) usagePage = 0xFF03;

		IPeripheralDetail detail = device.interfaces.FirstOrDefault(
			@interface => @interface.UsagePage == usagePage && @interface.Usage == 1,
			device.interfaces[0]);

		return detail?.Connect(true);
	}
}
