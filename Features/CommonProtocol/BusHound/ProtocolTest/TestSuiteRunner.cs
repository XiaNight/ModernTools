namespace CommonProtocol.BusHound.ProtocolTest;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Base.Services.Peripheral;
using Debug = Base.Services.Debug;

public sealed class TestSuiteRunner
{
	private const bool StripReportIdOnMatch = true;
	private const int TimeoutSafetySlackMs = 250;

	private readonly PeripheralInterface targetInterface;
	private readonly Func<byte[], CancellationToken, Task> writeAsync;

	public TestSuiteRunner(PeripheralInterface targetInterface)
	{
		this.targetInterface = targetInterface;
		writeAsync = (data, ct) => targetInterface.WriteAsync(data, ct);
	}

	public delegate void StepProgressCallback(int stepIndex, StepRunResult result);
	public delegate void PacketProgressCallback(int stepIndex, IReadOnlyList<byte[]> received);

	public async Task<SuiteRunResult> RunAsync(
		TestSuite suite,
		StepProgressCallback onStepDone = null,
		PacketProgressCallback onPackets = null,
		CancellationToken ct = default)
	{
		SuiteRunResult suiteResult = new();

		if (suite == null || suite.Steps.Count == 0)
		{
			suiteResult.Verdict = TestVerdict.Error;
			suiteResult.Message = "Suite is empty or null.";
			return suiteResult;
		}

		if (targetInterface == null)
		{
			suiteResult.Verdict = TestVerdict.Error;
			suiteResult.Message = "Not connected — start capturing a device first.";
			return suiteResult;
		}

		System.Diagnostics.Stopwatch totalWatch = System.Diagnostics.Stopwatch.StartNew();
		bool hasFailed = false;

		for (int i = 0; i < suite.Steps.Count; i++)
		{
			ct.ThrowIfCancellationRequested();

			TestStep step = suite.Steps[i];
			StepRunResult stepResult;

			if (hasFailed && suite.StopOnFirstFailure)
			{
				stepResult = new StepRunResult { Verdict = TestVerdict.Skipped, Message = "Skipped (prior step failed)." };
			}
			else
			{
				stepResult = step switch
				{
					SendStep send => await ExecuteSendAsync(send, ct),
					SendAndWaitStep wait => await ExecuteSendAndWaitAsync(wait, i, onPackets, ct),
					SendAndAssertStep assert => await ExecuteSendAndAssertAsync(assert, i, onPackets, ct),
					SleepStep sleep => await ExecuteSleepAsync(sleep, ct),
					_ => new StepRunResult { Verdict = TestVerdict.Error, Message = $"Unknown step type: {step.GetType().Name}" },
				};

				if (stepResult.Verdict != TestVerdict.Pass)
				{
					hasFailed = true;
				}
			}

			suiteResult.StepResults.Add(stepResult);
			onStepDone?.Invoke(i, stepResult);
		}

		totalWatch.Stop();
		suiteResult.TotalElapsedMs = totalWatch.Elapsed.TotalMilliseconds;

		if (!hasFailed)
		{
			suiteResult.Verdict = TestVerdict.Pass;
			suiteResult.Message = $"All {suite.Steps.Count} step(s) passed.";
		}
		else
		{
			StepRunResult firstFailure = suiteResult.StepResults.Find(r => r.Verdict != TestVerdict.Pass && r.Verdict != TestVerdict.Skipped);
			suiteResult.Verdict = firstFailure?.Verdict ?? TestVerdict.Error;
			suiteResult.Message = firstFailure?.Message ?? "Suite failed.";
		}

		return suiteResult;
	}

	private async Task<StepRunResult> ExecuteSendAsync(SendStep step, CancellationToken ct)
	{
		StepRunResult result = new();
		try
		{
			if (!HexBytes.TryParse(step.RequestHex, out byte[] request, out string parseError))
			{
				result.Verdict = TestVerdict.Error;
				result.Message = $"Invalid request bytes: {parseError}";
				return result;
			}

			targetInterface.ClearPendingReports();
			await writeAsync(request, ct);

			result.Verdict = TestVerdict.Pass;
			result.Message = "Sent.";
		}
		catch (Exception ex)
		{
			result.Verdict = TestVerdict.Error;
			result.Message = ex.Message;
			Debug.Log($"[BusHound.Tests] Send step '{step.Name}' failed: {ex.Message}");
		}

		return result;
	}

	private async Task<StepRunResult> ExecuteSendAndWaitAsync(
		SendAndWaitStep step, int stepIndex, PacketProgressCallback onPackets, CancellationToken ct)
	{
		StepRunResult result = new();
		List<byte[]> received = result.Received;

		if (!HexBytes.TryParse(step.RequestHex, out byte[] request, out string parseError))
		{
			result.Verdict = TestVerdict.Error;
			result.Message = $"Invalid request bytes: {parseError}";
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

		int budgetMs = Math.Max(1, step.TotalTimeoutMs);
		using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cts.CancelAfter(budgetMs + TimeoutSafetySlackMs);

		try
		{
			targetInterface.ClearPendingReports();
			targetInterface.OnDataSent += sentProbe;
			targetInterface.OnDataReceived += recvProbe;

			await writeAsync(request, cts.Token);

			for (int i = 0; i < step.ExpectedCount; i++)
			{
				await rxSignal.WaitAsync(cts.Token);
				rx.TryDequeue(out (byte[] data, DateTime time) item);

				byte[] compare = StripReportIdOnMatch && item.data.Length > 0 ? item.data[1..] : item.data;
				received.Add(compare);
				onPackets?.Invoke(stepIndex, received);

				if (sentTime != default && item.time >= sentTime)
					result.ElapsedMs = (item.time - sentTime).TotalMilliseconds;
			}

			result.Verdict = TestVerdict.Pass;
			result.Message = $"Received {step.ExpectedCount} packet(s).";
		}
		catch (OperationCanceledException) when (!ct.IsCancellationRequested)
		{
			result.Verdict = TestVerdict.Timeout;
			result.Message = $"Received {received.Count}/{step.ExpectedCount} packet(s) within {budgetMs}ms.";
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			result.Verdict = TestVerdict.Error;
			result.Message = ex.Message;
			Debug.Log($"[BusHound.Tests] SendAndWait step '{step.Name}' failed: {ex.Message}");
		}
		finally
		{
			targetInterface.OnDataSent -= sentProbe;
			targetInterface.OnDataReceived -= recvProbe;
		}

		return result;
	}

	private async Task<StepRunResult> ExecuteSendAndAssertAsync(
		SendAndAssertStep step, int stepIndex, PacketProgressCallback onPackets, CancellationToken ct)
	{
		StepRunResult result = new();
		List<byte[]> received = result.Received;

		if (!HexBytes.TryParse(step.RequestHex, out byte[] request, out string parseError))
		{
			result.Verdict = TestVerdict.Error;
			result.Message = $"Invalid request bytes: {parseError}";
			return result;
		}

		List<ExpectedPacket> expected = new();
		for (int i = 0; i < step.ExpectedLines.Count; i++)
		{
			if (!ExpectedPacket.TryParse(step.ExpectedLines[i], out ExpectedPacket packet, out string lineError))
			{
				result.Verdict = TestVerdict.Error;
				result.Message = $"Expected line {i + 1}: {lineError}";
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

		int budgetMs = Math.Max(1, step.TotalTimeoutMs);
		using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
		cts.CancelAfter(budgetMs + TimeoutSafetySlackMs);

		try
		{
			targetInterface.ClearPendingReports();
			targetInterface.OnDataSent += sentProbe;
			targetInterface.OnDataReceived += recvProbe;

			await writeAsync(request, cts.Token);

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
				onPackets?.Invoke(stepIndex, received);

				if (elapsed.TotalMilliseconds > budgetMs)
				{
					result.Verdict = TestVerdict.Timeout;
					result.Message = $"Reply arrived in {elapsed.TotalMilliseconds:0.###}ms, over the {budgetMs}ms budget.";
					return result;
				}

				if (!expected[i].Matches(compare, step.AllowTrailingWildcard))
				{
					result.Verdict = TestVerdict.Mismatch;
					result.Message = $"Packet {i + 1} mismatch. Expected {expected[i]}, got {HexFormat.FormatBrief(compare)}.";
					return result;
				}
			}

			result.Verdict = TestVerdict.Pass;
			result.Message = $"{expected.Count} packet(s) matched.";
			result.ElapsedMs = elapsed.TotalMilliseconds;
		}
		catch (OperationCanceledException) when (!ct.IsCancellationRequested)
		{
			result.Verdict = TestVerdict.Timeout;
			result.Message = $"No reply within {budgetMs}ms.";
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			result.Verdict = TestVerdict.Error;
			result.Message = ex.Message;
			Debug.Log($"[BusHound.Tests] Assert step '{step.Name}' failed: {ex.Message}");
		}
		finally
		{
			targetInterface.OnDataSent -= sentProbe;
			targetInterface.OnDataReceived -= recvProbe;
		}

		return result;
	}

	private static async Task<StepRunResult> ExecuteSleepAsync(SleepStep step, CancellationToken ct)
	{
		StepRunResult result = new();
		try
		{
			await Task.Delay(Math.Max(0, step.DurationMs), ct);
			result.Verdict = TestVerdict.Pass;
			result.Message = $"Slept {step.DurationMs}ms.";
			result.ElapsedMs = step.DurationMs;
		}
		catch (OperationCanceledException)
		{
			result.Verdict = TestVerdict.Error;
			result.Message = "Sleep cancelled.";
		}

		return result;
	}
}
