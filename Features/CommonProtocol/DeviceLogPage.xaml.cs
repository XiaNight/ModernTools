using Base.Core;
using Base.Pages;
using Base.Services;
using Base.Services.Peripheral;
using ModernWpf.Controls;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

        [Persist, Config("Polling Rate",
            Header = "Timing",
            Hint = "How often the device is polled, in milliseconds.",
            HelpBox = "Lower values increase responsiveness but use more CPU.",
            Min = 1,
            Changed = nameof(UpdateInterval))]
        private long IntervalMs = 1000;

        private enum CommandPipeMode { Interrupt, ControlOutput }

        [Persist, Config("Command Pipe",
            Header = "Transport",
            Hint = "How log commands are sent to the device.",
            HelpBox = "Interrupt = interrupt OUT endpoint. Control (Output/Feature) send via HID SET_REPORT over the control pipe (EP0); replies are still read from the interrupt IN stream. ASUS vendor collections usually need Feature.",
            Changed = nameof(ApplyPipeMode))]
        private readonly CommandPipeMode CommandPipe = CommandPipeMode.Interrupt;

        [Persist, Config("Log Target Keys",
            Header = "Logging",
            HelpBox = "e.g. \"A0, A1, A2\" for B701 dongle / left / right.",
            Changed = nameof(OnLogKeysChanged))]
        private readonly List<LogKeyInfo> logKeys = [];

        [Persist, Config("Timestamp Each Line",
            Header = "Logging",
            Hint = "Prefix every line with a [HH:mm:ss.fff] timestamp before the key tag.")]
        private readonly bool ShowTimestamp = false;

        private readonly Dictionary<byte, System.Text.StringBuilder> lineBuffers = new();
        private int cycleIndex;

        [Persist, Config("Report ID",
            Header = "Logging",
            Hint = "HID report id placed in byte 0 of the request. 0 = device default.",
            HelpBox = "B701 uses 0xCC. Leave 0 to use the built-in per-device value.",
            Type = ConfigType.Hex,
            Changed = nameof(ApplyPipeMode))]
        private readonly byte LogReportId = 0x00;

        //- Start / Pause
        private bool isLogEnabled = true;
        private Button logStartBtn;
        private Button logPauseBtn;

        //- Logging
        private volatile bool commandPending;
        private byte lastKey = 0x00;

        private void OnLogKeysChanged()
        {
            // Target set changed: previous lines no longer apply, so start clean.
            lineBuffers.Clear();
            cycleIndex = 0;
            LogPanel?.Clear();

            foreach (var keyInfo in logKeys)
            {
                if (keyInfo.Brush is SolidColorBrush solidBrush)
                {
                    solidBrush.Color = keyInfo.Color;
                }
            }
        }

        private void ApplyPipeMode()
        {
            if (activeInterface == null) return;

            activeInterface.ReportIdOverride = LogReportId == 0x00 ? -1 : LogReportId;

            // Replies always arrive on the interrupt IN stream (matches the B701 tool), so only
            // the write direction switches to the control pipe.
            switch (CommandPipe)
            {
                case CommandPipeMode.ControlOutput:
                    activeInterface.TxPipe = PeripheralPipe.Control;
                    activeInterface.ControlKind = ControlReportKind.Output;
                    activeInterface.RxPipe = PeripheralPipe.Interrupt;
                    break;
                default:
                    activeInterface.TxPipe = PeripheralPipe.Interrupt;
                    activeInterface.RxPipe = PeripheralPipe.Interrupt;
                    break;
            }
        }

        public DeviceLogPage()
        {
            InitializeComponent();
            logStartBtn = LogPanel.GetAdditionalControlByTag<Button>("LogStartBtn");
            logPauseBtn = LogPanel.GetAdditionalControlByTag<Button>("LogPauseBtn");
        }

        public override void Awake()
        {
            base.Awake();

            timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(IntervalMs),
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
            if (activeInterface == null) return;
            if (!isLogEnabled) return;
            if (commandPending) return;
            if (logKeys.Count == 0) return;

            LogKeyInfo keyInfo = logKeys[cycleIndex % logKeys.Count];
            AppendLogQuerry(keyInfo.Key);

            // Cycle through the configured targets, one request per tick.
            cycleIndex = (cycleIndex + 1) % logKeys.Count;
        }

        private void AppendLogQuerry(byte key)
        {
            commandPending = true;
            ProtocolService.AppendCmd(activeInterface, [0xFD, key, 0x00, 0x00], true);
        }

        private void UpdateInterval()
        {
            timer.Interval = TimeSpan.FromMilliseconds(IntervalMs);
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
                ApplyPipeMode();

                lineBuffers.Clear();
                cycleIndex = 0;

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
            if (activeInterface.IsDeviceConnected)
            {
                activeInterface.OnDataReceived -= Parse;
            }

            timer?.Stop();

            activeInterface = null;
        }

        private void Parse(ReadOnlyMemory<byte> arg1, DateTime arg2)
        {
            commandPending = false;
            ReadOnlySpan<byte> span = arg1.Span;

            // Reply layout: [reportId] FD <key> <idx> <idx> <ascii...>. Match FD and a key we poll.
            if (span.Length < 6 || span[1] != 0xFD) return;

            // Find key and key info.
            byte key = span[2];
            LogKeyInfo keyInfo = logKeys.Find(info => info.Key == key);
            if (keyInfo == null) return;

            // Slice header off the data.
            ReadOnlySpan<byte> data = span.Slice(5);
            int end = data.IndexOf((byte)0);
            if (end < 0) end = data.Length;
            if (end == 0) return;

            string chunk = System.Text.Encoding.ASCII.GetString(data.Slice(0, end));

            // Accumulate per key so a log line split across packets is emitted whole and tagged once.
            if (!lineBuffers.TryGetValue(key, out var sb))
            {
                sb = new System.Text.StringBuilder();
                lineBuffers[key] = sb;
            }
            sb.Append(chunk);

            List<string> lines = [];
            string buffered = sb.ToString();
            int nl;
            while ((nl = buffered.IndexOf('\n')) >= 0)
            {
                string line = buffered[..nl].TrimEnd('\r');
                buffered = buffered[(nl + 1)..];
                if (line.Trim().Length == 0) continue; // ignore empty lines
                lines.Add(FormatLine(keyInfo.KeyName, line, arg2));
            }
            sb.Clear();

            if(buffered.Trim().Length > 0)
            {
                lines.Add(FormatLine(keyInfo.KeyName, buffered, arg2));
            }

            if (lines.Count == 0) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (string l in lines) LogPanel.AppendLog(l, true, colorBrush: keyInfo.Brush);
            });

            if (end >= 58)
            {
                AppendLogQuerry(key);
            }
        }

        private string FormatLine(string key, string line, DateTime timeUtc)
        {
            string ts = ShowTimestamp ? $"[{timeUtc.ToLocalTime():HH:mm:ss.fff}]" : string.Empty;
            return $"{ts}[{key}] {line}";
        }

        private void OnStartButtonClick(object sender, RoutedEventArgs e)
        {
            isLogEnabled = true;
            logStartBtn.Visibility = Visibility.Collapsed;
            logPauseBtn.Visibility = Visibility.Visible;
        }

        private void OnPauseButtonClick(object sender, RoutedEventArgs e)
        {
            isLogEnabled = false;
            logPauseBtn.Visibility = Visibility.Collapsed;
            logStartBtn.Visibility = Visibility.Visible;
        }

        private void OnExportButtonClick(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.SaveFileDialog dialog = new()
            {
                Filter = "Text Files (*.txt)|*.txt|Log Files (*.log)|*.log|All Files (*.*)|*.*",
                DefaultExt = ".txt",
                FileName = $"DeviceLog_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (dialog.ShowDialog() == true)
            {
                string text = LogPanel.GetAllText();
                System.IO.File.WriteAllText(dialog.FileName, text);
            }
        }

        private class LogKeyInfo
        {
            [Config(Type = ConfigType.Hex)]
            public byte Key { get; set; }
            [Config]
            public string KeyName { get; set; } = "Key Name";
            [Config(Type = ConfigType.Hex_RGB)]
            public Color Color { get; set; } = Colors.Gray;

            [JsonIgnore]
            private Brush brush;
            [JsonIgnore]
            public Brush Brush
            {
                get
                {
                    if (brush == null)
                    {
                        brush = new SolidColorBrush(Color);
                    }
                    return brush;
                }
            }
        }
    }
}
