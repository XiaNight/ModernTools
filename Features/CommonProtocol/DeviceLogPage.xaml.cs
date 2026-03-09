using Base.Pages;
using Base.Services;
using Base.Services.Peripheral;
using System.Windows;
using System.Windows.Threading;

namespace CommonProtocol
{
    /// <summary>
    /// Interaction logic for DeviceLogPage.xaml
    /// </summary>
    public partial class DeviceLogPage : PageBase
    {
        public override string PageName => "Device Log";
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
