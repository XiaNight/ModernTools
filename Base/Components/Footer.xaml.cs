using Base.Core;
using Base.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ModernWpf;

namespace Base.Components
{
	public partial class Footer : UserControl
    {
		private readonly List<Entry> _leftItems = new();
		private readonly List<Entry> _rightItems = new();

        public Footer()
        {
            InitializeComponent();
        }

        public void Footer_Loaded(object sender, RoutedEventArgs e)
		{
			SetDeviceInfo((output) =>
			{
				Application.Current.Dispatcher.Invoke(() =>
				{
					var newItem = AddRight();
					var textBlock = new TextBlock
					{
						Text = output,
						VerticalAlignment = VerticalAlignment.Center,
					};
					newItem.Add(textBlock);
					textBlock.MouseLeftButtonDown += (s, e) =>
					{
						Clipboard.SetText(output);
						PopupInfo.Show("copied to clipboard");
					};
				});
			});
		}

		private async void SetDeviceInfo(Action<string> callback)
		{
			//BatchService.BatchExecution batchExecution = new BatchService.BatchExecution(Main.GetToolFolder("device-info.bat"));
			//var result = await batchExecution.StartAsync(10);
			//if (result.success)
			//{
			//	// get last line of output
			//	string[] lines = result.output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
			//	string lastLine = lines.Length > 0 ? lines[^1] : "No output";

			//	var splits = lastLine.Split('/');
			//	var deviceInfo = splits[0].Split("_")[0].Trim();
			//	var osInfo = splits[1].Replace("Microsoft Windows", "Win").Trim();
			//	var firmwareInfo = splits[2].Trim();

			//	var output = $"{deviceInfo} / {osInfo} OS: {firmwareInfo}";

			//	callback?.Invoke(output);
			//}
		}

		public Entry AddLeft()
		{
			return AddItem(_leftItems, Dock.Left);
		}

		public Entry AddRight()
		{
			return AddItem(_rightItems, Dock.Right);
		}

		public void Clear()
		{
			_leftItems.Clear();
			_rightItems.Clear();
		}

		private Entry AddItem(List<Entry> list, Dock dock)
		{
			Entry entry = new(dock);
			list.Add(entry);
            ContentPanel.Children.Add(entry.StackPanel);
			DockPanel.SetDock(entry.StackPanel, dock);
			return entry;
		}

		private bool RemoveItem(List<Entry> list, Entry entry)
		{
			if (list.Remove(entry))
			{
                ContentPanel.Children.Remove(entry.StackPanel);
				return true;
			}
			return false;
		}

		public class Entry
		{
			public StackPanel StackPanel { get; private set; }

			private readonly Dock dockSide;

			public Entry(Dock dockSide)
			{
				this.dockSide = dockSide;
				StackPanel = new()
				{
					Orientation = Orientation.Horizontal,
					VerticalAlignment = VerticalAlignment.Stretch,
					Margin = new Thickness(8, 0, 8, 0),
				};
			}

			public void Add(FrameworkElement element)
			{
				StackPanel.Children.Add(element);
			}
		}
	}
}
