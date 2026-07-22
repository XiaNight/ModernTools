using AiComposer.Model;

namespace AiComposer.Runtime;

/// <summary>
/// The working sample generated page shipped on first run. It exercises the whole contract end to
/// end without an external AI: loose XAML bound (via {Binding} / Command) to a compiled
/// IGeneratedLogic object, DynamicResource theme brushes for live light/dark, and a read of the
/// curated host API (device status + logging). Editing or deleting it drives the same
/// persist → register → materialize → destroy loop as any generated page.
/// </summary>
internal static class BuiltInSample
{
	/// <summary>Fixed id so the sample is seeded at most once and is recognisable on disk.</summary>
	public const string SampleId = "a1b2c3d4e5f6407182930a4b5c6d7e8f";

	public static GeneratedPageDefinition Create() => new()
	{
		Id = SampleId,
		Title = "Sample Page",
		Glyph = "",
		Group = "Generated",
		Order = 0,
		Xaml = SampleXaml,
		Csharp = SampleCsharp,
	};

	private const string SampleXaml = """
		<ScrollViewer VerticalScrollBarVisibility="Auto">
			<StackPanel Margin="24" MaxWidth="560" HorizontalAlignment="Left">
				<TextBlock Text="AI Composer — Sample Page" FontSize="24" FontWeight="SemiBold"
						   Foreground="{DynamicResource SystemControlForegroundBaseHighBrush}"/>
				<TextBlock TextWrapping="Wrap" Margin="0,4,0,16"
						   Foreground="{DynamicResource SystemControlForegroundBaseMediumBrush}"
						   Text="This page was generated from XAML + C# strings, compiled at runtime, and persisted to disk. It reloads on app restart."/>
				<Border Background="{DynamicResource SystemControlBackgroundChromeMediumLowBrush}"
						BorderBrush="{DynamicResource SystemControlForegroundBaseLowBrush}"
						BorderThickness="1" CornerRadius="6" Padding="16">
					<StackPanel>
						<TextBlock Text="{Binding Message}" FontSize="18"
								   Foreground="{DynamicResource SystemControlForegroundBaseHighBrush}"/>
						<StackPanel Orientation="Horizontal" Margin="0,12,0,0">
							<Button Content="Increment" Command="{Binding IncrementCommand}" Padding="16,6"/>
							<Button Content="Reset" Command="{Binding ResetCommand}" Margin="8,0,0,0" Padding="16,6"/>
						</StackPanel>
						<TextBlock Text="{Binding DeviceStatus}" Margin="0,16,0,0" TextWrapping="Wrap"
								   Foreground="{DynamicResource SystemControlForegroundBaseMediumBrush}"/>
					</StackPanel>
				</Border>
			</StackPanel>
		</ScrollViewer>
		""";

	private const string SampleCsharp = """
		using System.ComponentModel;

		// Implements the host contract. The instance is set as the XAML DataContext, so its bindable
		// properties and ICommand members drive the UI. No code-behind or event handlers are allowed.
		public sealed class SampleLogic : IGeneratedLogic, INotifyPropertyChanged
		{
			private IHostApi host;
			private int count;

			public int Count
			{
				get => count;
				set { count = value; Raise(nameof(Count)); Raise(nameof(Message)); }
			}

			public string Message => $"You clicked {Count} time(s).";

			public string DeviceStatus => host != null && host.HasActiveDevice
				? $"Connected device: {host.ActiveDevice.Name} (VID {host.ActiveDevice.Vid:X4}, PID {host.ActiveDevice.Pid:X4})"
				: "No device connected.";

			public ICommand IncrementCommand { get; private set; }
			public ICommand ResetCommand { get; private set; }

			public void Initialize(IHostApi host, FrameworkElement root)
			{
				this.host = host;
				IncrementCommand = new RelayCommand<object>(_ =>
				{
					Count++;
					host.Log($"Count is now {Count}.");
				});
				ResetCommand = new RelayCommand<object>(_ => Count = 0);

				host.ActiveDeviceChanged += () => host.RunOnUi(() => Raise(nameof(DeviceStatus)));
				Raise(nameof(DeviceStatus));
			}

			public event PropertyChangedEventHandler PropertyChanged;
			private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
		}
		""";
}
