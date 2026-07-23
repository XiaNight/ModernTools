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

        [Persist, Config("Polling Rate",
            Header = "Timing",
            Hint = "How often the device is polled, in milliseconds.",
            HelpBox = "Lower values increase responsiveness but use more CPU.",
            Min = 1,
            Changed = nameof(UpdateInterval))]
        private long IntervalMs = 1000;

        private enum CommandPipeMode { Interrupt, ControlOutput, ControlFeature }

        [Persist, Config("Command Pipe",
            Header = "Transport",
            Hint = "How log commands are sent to the device.",
            HelpBox = "Interrupt = interrupt OUT endpoint. Control (Output/Feature) send via HID SET_REPORT over the control pipe (EP0); replies are still read from the interrupt IN stream. ASUS vendor collections usually need Feature.",
            Changed = nameof(ApplyPipeMode))]
        private readonly CommandPipeMode CommandPipe = CommandPipeMode.Interrupt;

        [Persist, Config("Log Target Keys",
            Header = "Logging",
            Hint = "One or more target keys in hex, comma-separated. The log cycles through them.",
            HelpBox = "e.g. \"A0, A1, A2\" for B701 dongle / left / right. Each line is tagged with its key.",
            Changed = nameof(OnLogKeysChanged))]
        private readonly string LogKeys = "A0";

        [Persist, Config("Timestamp Each Line",
            Header = "Logging",
            Hint = "Prefix every line with a [HH:mm:ss.fff] timestamp before the key tag.")]
        private readonly bool ShowTimestamp = false;

        // Parsed form of LogKeys, plus per-key partial-line assembly and round-robin state.
        private readonly List<byte> logKeys = new();
        private readonly Dictionary<byte, System.Text.StringBuilder> lineBuffers = new();
        private int cycleIndex;

        [Persist, Config("Report ID",
            Header = "Logging",
            Hint = "HID report id placed in byte 0 of the request. 0 = device default.",
            HelpBox = "B701 uses 0xCC. Leave 0 to use the built-in per-device value.",
            Type = ConfigType.Hex,
            Changed = nameof(ApplyPipeMode))]
        private readonly byte LogReportId = 0x00;

        private void OnLogKeysChanged()
        {
            // Target set changed: previous lines no longer apply, so start clean.
            ParseLogKeys();
            lineBuffers.Clear();
            cycleIndex = 0;
            LogPanel?.Clear();
        }

        private void ParseLogKeys()
        {
            logKeys.Clear();
            if (string.IsNullOrWhiteSpace(LogKeys)) return;

            foreach (string tok in LogKeys.Split(new[] { ',', ' ', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string s = tok.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? tok[2..] : tok;
                if (byte.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                    logKeys.Add(b);
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
                case CommandPipeMode.ControlFeature:
                    activeInterface.TxPipe = PeripheralPipe.Control;
                    activeInterface.ControlKind = ControlReportKind.Feature;
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
        }

        public override void Awake()
        {
            base.Awake();

            timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            timer.Tick += OnTick;

            ParseLogKeys();
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
            if (logKeys.Count == 0) ParseLogKeys();
            if (logKeys.Count == 0) return;

            // Cycle through the configured targets, one request per tick.
            byte key = logKeys[cycleIndex % logKeys.Count];
            cycleIndex = (cycleIndex + 1) % logKeys.Count;

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
            if (!activeInterface.IsDeviceConnected) return;
            activeInterface.OnDataReceived -= Parse;

            timer?.Stop();

            activeInterface = null;
        }

        private void Parse(ReadOnlyMemory<byte> arg1, DateTime arg2)
        {
            ReadOnlySpan<byte> span = arg1.Span;

            // Reply layout: [reportId] FD <key> <idx> <idx> <ascii...>. Match FD and a key we poll.
            if (span.Length < 6 || span[1] != 0xFD) return;

            byte key = span[2];
            if (!logKeys.Contains(key)) return;

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

            List<string> lines = new();
            string buffered = sb.ToString();
            int nl;
            while ((nl = buffered.IndexOf('\n')) >= 0)
            {
                string line = buffered[..nl].TrimEnd('\r');
                buffered = buffered[(nl + 1)..];
                if (line.Trim().Length == 0) continue; // ignore empty lines
                lines.Add(FormatLine(key, line, arg2));
            }
            sb.Clear();

            if(buffered.Trim().Length > 0)
            {
                lines.Add(FormatLine(key, buffered, arg2));
            }

            if (lines.Count == 0) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (string l in lines) LogPanel.AppendLog(l, true);
            });
        }

        private string FormatLine(byte key, string line, DateTime timeUtc)
        {
            string ts = ShowTimestamp ? $"[{timeUtc.ToLocalTime():HH:mm:ss.fff}]" : string.Empty;
            return $"{ts}[{key:X2}] {line}";
        }
    }
}
