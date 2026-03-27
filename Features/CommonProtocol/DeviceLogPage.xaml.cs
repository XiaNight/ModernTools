using Base.Core;
using Base.Pages;
using Base.Services;
using Base.Services.Peripheral;
using ModernWpf;
using ModernWpf.Controls;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CommonProtocol
{
    /// <summary>
    /// Interaction logic for DeviceLogPage.xaml
    /// </summary>
    public partial class DeviceLogPage : PageBase
    {
        public override string PageName => "Device Log";
        public override string Glyph => "\uF714";
        public override string Description => "View the log of the active device.";

        private PeripheralInterface activeInterface;

        private DispatcherTimer testPrintTimer;
        private DispatcherTimer timer;

        public DeviceLogPage()
        {
            InitializeComponent();
        }

        public override void Awake()
        {
            base.Awake();
            //LogPanel.AppendLog("Device Log Initialized.");

            //// Test logs
            //for (int i = 0; i < 100; i++)
            //{
            //    LogPanel.AppendLog($"TTTTTTTTTTTTTTTTTTTTT");
            //}

            //testPrintTimer = new DispatcherTimer
            //{
            //    Interval = TimeSpan.FromMilliseconds(250)
            //};

            //testPrintTimer.Tick += (s, e) =>
            //{
            //    LogPanel.AppendLog($"Test log at {DateTime.Now:HH:mm:ss.fff}");
            //};

            timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            timer.Tick += OnTick;
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            DeviceSelection.Instance.OnActiveDeviceConnected += ConnectToInterface;
            DeviceSelection.Instance.OnActiveDeviceDisconnected += DisconnectInterface;

            if(activeInterface == null && ActiveDevice != null)
            {
                ConnectToInterface();
            }

            timer.Start();
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            DeviceSelection.Instance.OnActiveDeviceConnected -= ConnectToInterface;
            DeviceSelection.Instance.OnActiveDeviceDisconnected -= DisconnectInterface;
            timer.Stop();
        }

        private void OnTick(object sender, EventArgs e)
        {
            ProtocolService.AppendCmd(activeInterface, "get_log", true);
        }

        [AppMenuItem("Set Interval")]
        private async void IntervalPopup()
        {
            var textBox = new TextBox
            {
                MinWidth = 220,
                Margin = new Thickness(0, 12, 0, 0),
                Text = "1000"
            };

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = "Enter interval (ms):"
            });
            panel.Children.Add(textBox);

            var dialog = new ContentDialog
            {
                Title = "Interval",
                Content = panel,
                PrimaryButtonText = "OK",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            if (int.TryParse(textBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int interval) && interval > 0)
            {
                Dispatcher.Invoke(() => timer.Interval = TimeSpan.FromMilliseconds(interval));
            }
        }


        private void ConnectToInterface()
        {
            if (timer.IsEnabled) return;
            var device = DeviceSelection.Instance.ActiveDevice;
            try
            {
                var usagePage = device.PID == 0x1ACE ? 0xFF02 : 0xFF00;
                if(device.PID == 0x1C64 || device.PID == 0x1C65) usagePage = 0xFF03;

                if (device.interfaces.Count == 0) return;

                var deviceInterface = device.interfaces.FirstOrDefault(@interface =>
                    (@interface.UsagePage == usagePage) && (@interface.Usage == 1),
                    device.interfaces[0]
                );
                if (deviceInterface == null)
                {
                    return;
                }

                activeInterface = deviceInterface.Connect(true);
                activeInterface.OnDataReceived += Parse;

                timer.Start();
            }
            catch (Exception ex)
            {
                Debug.Log("[DeviceLogPage] Failed to open HID device: " + ex.Message);
                return;
            }
        }

        private void DisconnectInterface()
        {
            if (activeInterface == null) return;
            if (!activeInterface.IsDeviceConnected) return;
            activeInterface.OnDataReceived -= Parse;

            timer?.Stop();

            activeInterface = null;
        }

        private void Parse(ReadOnlyMemory<byte> arg1, DateTime arg2)
        {
            ReadOnlySpan<byte> span = arg1.Span;

            if (!ProtocolService.IsCmdMatch([0xFD, 0xA0], span)) return;

            ReadOnlySpan<byte> data = span.Slice(5);

            int length = data.IndexOf((byte)0);
            if (length < 0)
            {
                length = data.Length;
            }
            if(length == 0) return;

            string message = System.Text.Encoding.ASCII.GetString(data.Slice(0, length));

            Application.Current.Dispatcher.Invoke(() => LogPanel.AppendLog(message, false));
        }
    }
}
