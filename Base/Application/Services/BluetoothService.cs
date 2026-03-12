// using Windows.Devices.Radios;

using System.IO;
using System.Windows;

namespace Base.Services
{
	public static class BluetoothService
	{
		private static Task<int> currentTask;

        private static MainWindow Main = Application.Current.MainWindow as MainWindow
                ?? throw new InvalidOperationException("MainWindow not found or invalid.");

        public static async Task<int> ToggleBluetoothAsync(bool state)
		{
			if (currentTask != null && !currentTask.IsCompleted)
			{
				Console.WriteLine("Bluetooth toggle already in progress.");
				return -1;
			}

			currentTask = SetBluetoothStateAsync(state);
			int successCount = await currentTask;
			currentTask = null;

			return successCount;
		}

		private static async Task<int> SetBluetoothStateAsync(bool state)
		{
			string path = Main.GetToolFolder("Bluetooth");
			string file = state switch
			{
				true => "BluetoothOn.bat",
				false => "BluetoothOff.bat"
			};
			string filePath = Path.Combine(path, file);

			BatchService.BatchExecution batch = new(filePath);
			var result = await batch.StartAsync(10);

			return result.exitCode;
		}
	}
}