using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Base.Services
{
	public static class ProtocolService
	{
		public static readonly Dictionary<string, byte[]> CommandDictionary =
			new(StringComparer.OrdinalIgnoreCase)
			{
				{ "factory_enter", new byte[] { 0xFA, 0x00, 0xD3, 0xA5 } },
				{ "factory_exit", new byte[] { 0xFA, 0x00, 0x00, 0x00 } },
				{ "hall_sensor_enter", new byte[] { 0xFA, 0x10, 0x00, 0x00, 0x01 } },
				{ "hall_sensor_exit", new byte[] { 0xFA, 0x10, 0x00, 0x00, 0x00 } },
				{ "hall_prod_test_enter", new byte[] { 0x04, 0x04 } },
				{ "hall_prod_test_exit", new byte[] { 0x04, 0x05 } },
				{ "hall_raw", new byte[] { 0x04, 0x17, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 } },
				{ "hall_segment", new byte[] { 0x04, 0x17, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 } },
				{ "hall_analog", new byte[] { 0x04, 0x17, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04 } },
				{ "hall_gain", new byte[] { 0x04, 0x17, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x05 } },
				{ "hall_bottom_average", new byte[] { 0x04, 0x17, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x07 } },
				{ "hall_baseline_top", new byte[] { 0x04, 0x17, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0D } },
				{ "hall_baseline_bottom", new byte[] { 0x04, 0x17, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0E } },
				{ "power_battery_information", new byte[] { 0xFA, 0x30, 0x01, 0x00 } },
				{ "get_charger_info", new byte[] { 0xFA, 0x0D, 0x00, 0x00, 0x00 } },
				{ "get_raw_multi_calibration", new byte[] { 0xFA, 0x10, 0x06, 0x00 } },
				{ "get_analog_multi_calibration", new byte[] { 0xFA, 0x10, 0x07, 0x00 } },
				{ "get_log", new byte[] { 0xFD, 0xA0, 0x00, 0x00 } },
			};

		public static event Action<CmdData> OnCmdSent;
		public static event Action<CmdData> OnCmdQueued;

		private static readonly ConcurrentQueue<CmdData> pendingCommands = new();
		private static readonly SemaphoreSlim queueSignal = new(0, int.MaxValue);

		private static readonly object workerLock = new();
		private static CancellationTokenSource workerCts;
		private static Task workerTask;

		private const int DefaultWaitTimeoutMs = 250;
		private const int DefaultInterCommandDelayMs = 100;

		public static int PendingCmdCount => pendingCommands.Count;

		public static void Start()
		{
			lock (workerLock)
			{
				if (workerTask != null && !workerTask.IsCompleted) return;

				workerCts = new CancellationTokenSource();
				workerTask = Task.Run(() => WorkerLoopAsync(workerCts.Token));
			}
		}

		public static async Task StopAsync()
		{
			CancellationTokenSource cts;
			Task task;

			lock (workerLock)
			{
				cts = workerCts;
				task = workerTask;
				workerCts = null;
				workerTask = null;
			}

			if (cts == null) return;

			try
			{
				cts.Cancel();
			}
			finally
			{
				cts.Dispose();
			}

			if (task != null)
			{
				try { await task.ConfigureAwait(false); }
				catch (OperationCanceledException) { }
				catch { }
			}
		} 

		public static void AppendCmd(Peripheral.PeripheralInterface device, string cmdName, bool wait = false, params byte[] parameter)
		{
			if (device == null) return;
			if (string.IsNullOrWhiteSpace(cmdName)) return;

			Start();

			if (!CommandDictionary.TryGetValue(cmdName, out var cmd))
			{
				Log($"[HID] Command not found: {cmdName}");
				return;
			}

			AppendCmd(device, cmd, wait, parameter);
		}

		public static void AppendCmd(Peripheral.PeripheralInterface device, byte[] cmd, bool wait = false, params byte[] parameter)
		{
			if (device == null) return;
			if (cmd == null || cmd.Length == 0) return;

			Start();

			byte[] payload = parameter is null || parameter.Length == 0 ? [] : (byte[])parameter.Clone();
			CmdData item = new(device, cmd, wait, payload);
			pendingCommands.Enqueue(item);
			OnCmdQueued?.Invoke(item);
			queueSignal.Release();
		}

		public static void ClearCmd()
		{
			while (pendingCommands.TryDequeue(out _)) { }

			while (queueSignal.CurrentCount > 0)
			{
				try { queueSignal.Wait(0); }
				catch { break; }
			}
		}

		public static void EnterFactory(Peripheral.PeripheralInterface device) => AppendCmd(device, "factory_enter", wait: true);
		public static void ExitFactory(Peripheral.PeripheralInterface device) => AppendCmd(device, "factory_exit", wait: true);
		public static void EnterHallSensor(Peripheral.PeripheralInterface device) => AppendCmd(device, "hall_sensor_enter", wait: true);
		public static void ExitHallSensor(Peripheral.PeripheralInterface device) => AppendCmd(device, "hall_sensor_exit", wait: true);
		public static void EnterHallProdTest(Peripheral.PeripheralInterface device, bool blockEvent = true)
			=> AppendCmd(device, "hall_prod_test_enter", wait: false, blockEvent ? (byte)0x00 : (byte)0x01);
		public static void ExitHallProdTest(Peripheral.PeripheralInterface device) => AppendCmd(device, "hall_prod_test_exit", wait: false);

		private static async Task WorkerLoopAsync(CancellationToken ct)
		{
			while (!ct.IsCancellationRequested)
			{
				try
				{
					await queueSignal.WaitAsync(ct).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					break;
				}

				while (pendingCommands.TryDequeue(out var cmd))
				{
					try
					{
						await WriteCmdAsync(cmd.Device, cmd.Cmd, cmd.Wait, cmd.Parameter, ct).ConfigureAwait(false);
					}
					catch (OperationCanceledException)
					{
                        continue;
					}
					catch (Exception ex)
					{
						Log($"[HID] Failed to send command '{cmd.Cmd}': {ex.Message}");
					}
					finally
					{
						OnCmdSent?.Invoke(cmd);
					}
				}
			}
		}

		private static async Task WriteCmdAsync(
			Peripheral.PeripheralInterface device,
			byte[] cmd,
			bool wait,
			byte[] parameter,
			CancellationToken ct)
		{
			if (device == null) return;

			var param = parameter ?? [];
			var combined = new byte[cmd.Length + param.Length];
			Buffer.BlockCopy(cmd, 0, combined, 0, cmd.Length);
			if (param.Length > 0)
				Buffer.BlockCopy(param, 0, combined, cmd.Length, param.Length);

			try
			{
				if(wait)
				{
					CancellationTokenSource writeCancelationToken = CancellationTokenSource.CreateLinkedTokenSource(ct);
					writeCancelationToken.CancelAfter(DefaultWaitTimeoutMs);
					await device.WriteAndReadAsync(combined, writeCancelationToken.Token).ConfigureAwait(false);
					Log($"[HID] Sent: {BitConverter.ToString(combined)}");
				}
				else
				{
					await device.Write(combined).ConfigureAwait(false);
					Log($"[HID] Sent: {BitConverter.ToString(combined)}");
					await Task.Delay(DefaultInterCommandDelayMs, ct).ConfigureAwait(false);
				}
			}
			catch (OperationCanceledException) { throw; }
			catch (Exception ex)
			{
				Log($"[HID] Failed to send data: {ex.Message}");
			}
		}

		private static void Log(string message)
		{
			try
			{
				if (Debugger.IsAttached)
					Debug.Log(message);
				else
					Console.WriteLine(message);
			}
			catch { }
		}

		public static bool IsCmdMatch(ReadOnlySpan<byte> cmd, ReadOnlySpan<byte> data)
		{
			if (data.Length <= 1) return false;
			return data.Slice(1).StartsWith(cmd);
		}

		public readonly struct CmdData
		{
			public CmdData(Peripheral.PeripheralInterface device, byte[] cmd, bool wait, byte[] parameter)
			{
				Device = device;
				Cmd = cmd;
				Wait = wait;
				Parameter = parameter ?? Array.Empty<byte>();
			}

			public Peripheral.PeripheralInterface Device { get; }
			public byte[] Cmd { get; }
			public byte[] Parameter { get; }
			public bool Wait { get; }
		}
	}
}