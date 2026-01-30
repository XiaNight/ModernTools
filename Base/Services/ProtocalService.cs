using System.Collections.Concurrent;

namespace Base.Services
{
	using Pages;

	public class ProtocalService
	{
		public static Dictionary<string, byte[]> CommandDictionary = new()
		{
			{ "factory_enter", [ 0xFA, 0x00, 0xD3, 0xA5 ] },
			{ "factory_exit", [ 0xFA, 0x00, 0x00, 0x00 ] },
			{ "hall_sensor_enter", [0xFA, 0x10, 0x00, 0x00, 0x01 ] },
			{ "hall_sensor_exit", [0xFA, 0x10, 0x00, 0x00, 0x00 ] },
			{ "hall_prod_test_enter", [ 0x04, 0x04 ] },
			{ "hall_prod_test_exit", [ 0x04, 0x05 ] },
			{ "hall_raw", [ 0x04, 0x17, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 ] },
			{ "hall_segment", [ 0x04, 0x17, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 ] },
			{ "hall_analog", [ 0x04, 0x17, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04 ] },
			{ "hall_gain", [ 0x04, 0x17, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x05 ] },
			{ "hall_bottom_average", [ 0x04, 0x17, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x07 ] },
			{ "hall_baseline_top", [ 0x04, 0x17, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0D ] },
			{ "hall_baseline_bottom", [ 0x04, 0x17, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0E ] },
			{ "power_battery_information", [ 0xfa, 0x30, 0x01, 0x00] },
			{ "get_charger_info", [ 0xfa, 0x0D, 0x00, 0x00, 0x00] },
			{ "get_raw_multi_calibration", [ 0xfa, 0x10, 0x06, 0x00 ] },
			{ "get_analog_multi_calibration", [ 0xfa, 0x10, 0x07, 0x00 ] },
        };

		public static void EnterFactory(Peripheral.PeripheralInterface device)
			=> AppendCmd(device, "factory_enter", true);
		public static void ExitFactory(Peripheral.PeripheralInterface device)
			=> AppendCmd(device, "factory_exit", true);
		public static void EnterHallSensor(Peripheral.PeripheralInterface device)
			=> AppendCmd(device, "hall_sensor_enter", true);
		public static void ExitHallSensor(Peripheral.PeripheralInterface device)
			=> AppendCmd(device, "hall_sensor_exit", true);
		public static void EnterHallProdTest(Peripheral.PeripheralInterface device, bool blockEvent = true)
			=> AppendCmd(device, "hall_prod_test_enter", false, blockEvent ? (byte)0x00 : (byte)0x01);
		public static void ExitHallProdTest(Peripheral.PeripheralInterface device)
			=> AppendCmd(device, "hall_prod_test_exit", false);

		public static event Action<CmdData> OnCmdSent;
		public static event Action<CmdData> OnCmdQueued;

		private static readonly ConcurrentQueue<CmdData> pendingCommands = new();
		private static Task writeTask = null;
		private static readonly object writeLock = new();

		public static int PendingCmdCount => pendingCommands.Count;

		public static void AppendCmd(Peripheral.PeripheralInterface device, string cmd, bool wait = false, params byte[] parameter)
		{
			try
			{
				pendingCommands.Enqueue(new(device, cmd, wait, parameter));
				OnCmdQueued?.Invoke(new(device, cmd, wait, parameter));

				lock (writeLock)
				{
					if (writeTask == null || writeTask.IsCompleted)
					{
						writeTask = PendingCmdParser();
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[HID] Failed to append command: {ex.Message}");
			}
		}

		public static void ClearCmd()
		{
			pendingCommands.Clear();
		}

		public static void WaitForFinish()
		{
			var sw = System.Diagnostics.Stopwatch.StartNew();
			while (!pendingCommands.IsEmpty)
			{
				Thread.Sleep(100);
				if (sw.ElapsedMilliseconds > 3000) break; // 3s timeout }
			}
		}

		private static Task PendingCmdParser()
		{
			return Task.Run(async () =>
			{
				try
				{
					while (!pendingCommands.IsEmpty)
					{
						if (!pendingCommands.TryDequeue(out CmdData cmd)) continue;
						await WriteCmdAsync(cmd.Device, cmd.Cmd, cmd.Wait, cmd.Parameter);
						OnCmdSent?.Invoke(cmd);
					}
					writeTask = null;
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[HID] Failed to process command queue: {ex.Message}");
				}
				finally
				{
					lock (writeLock)
					{
						writeTask = null;
					}
				}
			});
		}

		private static async Task WriteCmdAsync(Peripheral.PeripheralInterface device, string cmd, bool wait = false, params byte[] parameter)
		{
			if (device == null) return;
			if (!CommandDictionary.TryGetValue(cmd, out var data))
			{
				Debug.Log($"[HID] Command not found: {cmd}");
				return;
			}

			//- Combine command data with parameters
			byte[] combinedCmd = new byte[data.Length + parameter.Length];
			Array.Copy(data, combinedCmd, data.Length);
			Array.Copy(parameter, 0, combinedCmd, data.Length, parameter.Length);

			try
			{
				if (wait) device.ReadFlag = false;
				await device.Write(combinedCmd);
				Debug.Log($"[HID] Sent: {BitConverter.ToString(combinedCmd)}");
				if (wait)
				{
					await Task.Run(async () =>
					{
						var sw = System.Diagnostics.Stopwatch.StartNew();
						while (!device.ReadFlag)
						{
							await Task.Delay(10); // Yield control
							if (sw.ElapsedMilliseconds > 3000) break; // 3s timeout }
						}
					});
				}
				else
				{
					await Task.Delay(300);
				}
			}
			catch (Exception ex)
			{
				Debug.Log($"[HID] Failed to send data: {ex.Message}");
			}
		}

		public readonly struct CmdData(Peripheral.PeripheralInterface device, string cmd, bool wait, params byte[] parameter)
		{
			public readonly Peripheral.PeripheralInterface Device { get; } = device;
			public readonly string Cmd { get; } = cmd;
			public readonly byte[] Parameter { get; } = parameter;
			public readonly bool Wait { get; } = wait;
		}
	}
}