namespace Base.Services
{
	public static class BatteryIndicator
	{
		public static event Action<bool> OnBatteryChargingStatusChanged;

		/// <summary>
		/// Event triggered when the battery level updated.
		/// each byte represents a battery level in percentage (0-100) ie. 0x00 = 0%, 0x64 = 100%
		/// No need for conversion.
		/// </summary>
		public static event Action<byte[]> OnBatteryLevelChanged;

		public static void SetBatteryStatus(bool isCharging)
		{
			OnBatteryChargingStatusChanged?.Invoke(isCharging);
		}

		public static void SetBatteryLevel(byte[] level)
		{
			OnBatteryLevelChanged?.Invoke(level);
		}

		public static void ClearBatteryLevel()
		{

		}

		public static void GetBatteryLevel()
		{
			if (DeviceSelection.Instance.ActiveInterface == null) return;
			DeviceSelection.Instance.ActiveInterface.OnDataReceived += OnDataReceived;
			ProtocalService.AppendCmd(DeviceSelection.Instance.ActiveInterface, "power_battery_information", true);

			void OnDataReceived(ReadOnlyMemory<byte> readOnlyByte)
			{
				var data = readOnlyByte.Span;
				if (data.Length < 3) return;
				if (data[1] != 0xFA || data[2] != 0x30 || data[3] != 0x01) return;
				DeviceSelection.Instance.ActiveInterface.OnDataReceived -= OnDataReceived;

				List<byte> batteryLevels = new();
				int parserIndex = 5;
				int batteryIndex = 0;

				while (parserIndex + 2 < data.Length)
				{
					byte rsoc = data[parserIndex];
					if (rsoc == 0x00) break;

					batteryLevels.Add(rsoc);

					batteryIndex++;
					parserIndex += 3;
				}
				SetBatteryLevel(batteryLevels.ToArray());
			}
		}
	}
}