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
    [PageInfo("Device Log", Glyph = "\uF714", Description = "View the log of the active device.")]
    public partial class DeviceLogPage : PageBase
    {

        private PeripheralInterface activeInterface;
        private DispatcherTimer timer;

        [Config("Polling Rate",
            Header = "Timing",
            Hint = "How often the device is polled, in milliseconds.",
            HelpBox = "Lower values increase responsiveness but use more CPU.",
            Min = 1)]
        private long IntervalMs
        {
            get { return intervalMs; }
            set
            {
                timer.Interval = TimeSpan.FromMilliseconds(value);
                intervalMs = value;
            }
        }
        private long intervalMs = 1000;

        public DeviceLogPage()
        {
            InitializeComponent();
        }

        public override void Awake()
        {
            base.Awake();

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
