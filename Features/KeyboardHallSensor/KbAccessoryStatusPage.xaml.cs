using Base.Core;
using Base.Pages;
using Base.Services;
using Base.Services.APIService;
using Base.Services.Peripheral;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace KeyboardHallSensor
{
    /// <summary>
    /// Displays the live state of the eight KB Accessory pins.
    /// Columns = the 8 accessory pins (index 00~07), rows = each data field.
    /// Protocol: Get State = FA 11 00 00 &lt;PIN&gt; (see Factory Mode command [22] -> [0]).
    /// Response payload (after the echoed PIN): Type, Switch, Encoder Delta (LE 16),
    /// Encoder Abs (LE 16).
    /// </summary>
    [PageInfo("Accessory", Glyph = "", ShortName = "ACC", NavOrder = 0, Path = ["Keyboard", "Accessory"])]
    public partial class KbAccessoryStatusPage : PageBase, IKeyboardPage
    {
        //- Get State command. The PIN is appended as a parameter per poll.
        private static readonly byte[] GetStateCmd = { 0xFA, 0x11, 0x00, 0x00 };

        //- The 8 accessory pins and their key labels (from the .bat pin map).
        private static readonly (byte Pin, string Label)[] Pins =
        {
            (0x00, "F9"),   (0x01, "F10"),  (0x02, "F11"),  (0x03, "F12"),
            (0x04, "INS"),  (0x05, "DEL"),  (0x06, "PGUP"), (0x07, "PGDN"),
        };

        //- Row (field) labels, top to bottom.
        private static readonly string[] Fields =
        {
            "Accessory Type",
            "Switch State",
            "Encoder Delta",
            "Encoder Abs",
        };

        [Config(Name = "Auto Refresh Interval (ms)",
                Description = "Polling interval applied to all keys.",
                Min = 0)]
        private int AutoRefreshIntervalMs { get; set; } = 250;

        protected PeripheralInterface ActiveInterface => KeyboardCommonProtocol.Instance.ActiveInterface;

        //- valueCells[field, pinColumn]
        private readonly TextBlock[,] valueCells = new TextBlock[4, 8];
        //- Latest raw bytes per pin: [type, switch, deltaLo, deltaHi, absLo, absHi]
        private readonly byte[][] latest = new byte[8][];

        private DispatcherTimer pollTimer;
        //- Number of this page's Get State commands queued but not yet sent.
        //- Guards against launching a new refresh while the previous one is still draining.
        private int outstandingPolls;

        public override void Awake()
        {
            InitializeComponent();
            base.Awake();
            BuildTable();
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            outstandingPolls = 0;
            ProtocolService.OnCmdSent += OnCmdSent;

            Enter();
            if (ActiveInterface == null) return;

            ActiveInterface.OnDataReceived += Parse;
            DeviceSelection.Instance.OnActiveDeviceDisconnected += ClearAll;

            pollTimer = new DispatcherTimer { Interval = CurrentInterval };
            pollTimer.Tick += OnPollTick;
            pollTimer.Start();
        }

        private TimeSpan CurrentInterval =>
            TimeSpan.FromMilliseconds(Math.Max(0, AutoRefreshIntervalMs));

        private void OnPollTick(object sender, EventArgs e)
        {
            //- Pick up live changes to the configured interval.
            if (pollTimer != null && pollTimer.Interval != CurrentInterval)
                pollTimer.Interval = CurrentInterval;

            if (AutoRefreshCheck.IsChecked == true) Poll();
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            ProtocolService.OnCmdSent -= OnCmdSent;

            pollTimer?.Stop();
            pollTimer = null;

            if (ActiveInterface == null) return;

            ActiveInterface.OnDataReceived -= Parse;
            DeviceSelection.Instance.OnActiveDeviceDisconnected -= ClearAll;
        }

        private void Enter()
        {
            KeyboardCommonProtocol.Instance.OnInterfaceDisconnected += Exit;
            Poll();
        }

        private void Exit()
        {
            KeyboardCommonProtocol.Instance.OnInterfaceDisconnected -= Exit;
            //- Get State is read-only; no exit command required.
        }

        [AppMenuItem("Refresh")]
        private void Poll()
        {
            var device = ActiveInterface;
            if (device == null) return;

            //- Skip if the previous refresh batch hasn't finished draining. Commands are
            //- sent with wait=true, so a fast interval could otherwise stack batches.
            if (Volatile.Read(ref outstandingPolls) > 0) return;

            Interlocked.Exchange(ref outstandingPolls, Pins.Length);
            foreach (var (pin, _) in Pins)
                ProtocolService.AppendCmd(device, GetStateCmd, true, pin);
        }

        //- Fired by the protocol worker after each command is sent (success or failure).
        //- Decrement only for this page's own Get State commands, identified by reference.
        private void OnCmdSent(ProtocolService.CmdData cmd)
        {
            if (ReferenceEquals(cmd.Cmd, GetStateCmd))
                Interlocked.Decrement(ref outstandingPolls);
        }

        public void Parse(ReadOnlyMemory<byte> bytes, DateTime time)
        {
            var span = bytes.Span;

            //- Layout: [reportId, FA, 11, idxLo, idxHi, PIN(echo), type, switch,
            //-          deltaLo, deltaHi, absLo, absHi]
            if (span.Length < 12) return;
            if (span[1] != 0xFA || span[2] != 0x11) return;

            byte pin = span[5];
            int col = PinColumn(pin);
            if (col < 0) return;

            latest[col] = new[]
            {
                span[6],  // Accessory Type
                span[7],  // Switch State
                span[8],  // Encoder Delta low
                span[9],  // Encoder Delta high
                span[10], // Encoder Abs low
                span[11], // Encoder Abs high
            };

            Dispatcher.Invoke(() => UpdateColumn(col));
        }

        private static int PinColumn(byte pin)
        {
            for (int i = 0; i < Pins.Length; i++)
                if (Pins[i].Pin == pin) return i;
            return -1;
        }

        private void UpdateColumn(int col)
        {
            var data = latest[col];
            if (data == null) return;

            byte type = data[0];
            int delta = (data[3] << 8) | data[2];
            int abs = (data[5] << 8) | data[4];

            valueCells[0, col].Text = $"0x{type:X2}  {TypeName(type)}";
            valueCells[1, col].Text = $"0x{data[1]:X2}";
            valueCells[2, col].Text = $"0x{delta:X4}";
            valueCells[3, col].Text = $"0x{abs:X4}";
        }

        private static string TypeName(byte type) => type switch
        {
            0x01 => "Toggle",
            0x02 => "Encoder",
            _ => "-",
        };

        private void BuildTable()
        {
            //- Column 0 = field labels, columns 1..8 = the 8 pins.
            TableHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            for (int c = 0; c < Pins.Length; c++)
                TableHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

            //- Row 0 = header, rows 1..4 = fields.
            for (int r = 0; r <= Fields.Length; r++)
                TableHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            //- Top-left corner.
            AddCell("Pin", 0, 0, header: true, alignLeft: true);

            //- Header row: pin label + index.
            for (int c = 0; c < Pins.Length; c++)
            {
                var (pin, label) = Pins[c];
                AddCell($"{label}\n(0x{pin:X2})", 0, c + 1, header: true);
            }

            //- Field rows.
            for (int f = 0; f < Fields.Length; f++)
            {
                AddCell(Fields[f], f + 1, 0, header: true, alignLeft: true);
                for (int c = 0; c < Pins.Length; c++)
                    valueCells[f, c] = AddCell("-", f + 1, c + 1);
            }
        }

        private TextBlock AddCell(string text, int row, int col, bool header = false, bool alignLeft = false)
        {
            var textBlock = new TextBlock
            {
                Text = text,
                FontSize = 14,
                TextAlignment = alignLeft ? TextAlignment.Left : TextAlignment.Center,
                HorizontalAlignment = alignLeft ? HorizontalAlignment.Left : HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal,
                Margin = new Thickness(8, 4, 8, 4),
            };
            textBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextControlForeground");

            var border = new Border
            {
                BorderThickness = new Thickness(0, 0, 1, 1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x88, 0x88, 0x88)),
                Child = textBlock,
            };
            if (header)
                border.SetResourceReference(Border.BackgroundProperty, "SystemControlBackgroundListLowBrush");

            Grid.SetRow(border, row);
            Grid.SetColumn(border, col);
            TableHost.Children.Add(border);
            return textBlock;
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e) => Poll();

        [GET("/ClearAll", true)]
        private void ClearAll()
        {
            //- Pending commands are dropped on disconnect without an OnCmdSent, so clear the guard.
            Interlocked.Exchange(ref outstandingPolls, 0);

            for (int c = 0; c < 8; c++)
            {
                latest[c] = null;
                for (int f = 0; f < 4; f++)
                    if (valueCells[f, c] != null) valueCells[f, c].Text = "-";
            }
        }
    }
}
