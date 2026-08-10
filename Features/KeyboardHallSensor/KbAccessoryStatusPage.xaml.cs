using Base.Core;
using Base.Pages;
using Base.Services;
using Base.Services.APIService;
using Base.Services.Peripheral;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using static Base.Services.DeviceSelection;

namespace KeyboardHallSensor
{
    /// <summary>
    /// Displays the live state of the eight KB Accessory pins.
    /// Columns = the 8 accessory pins (index 00~07), rows = each data field.
    /// Protocol: Get State = FA 11 00 00 &lt;PIN&gt; (see Factory Mode command [22] -> [0]).
    /// Response payload (after the echoed PIN): Type, Switch, Encoder Delta (LE 16),
    /// Encoder Abs (LE 16).
    /// </summary>
    [PageInfo("Accessory", Glyph = "\uE765", ShortName = "ACC", NavOrder = 0, Path = ["Keyboard", "Armoury"])]
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
                Description = "Polling interval between all keys.",
                Min = 0)]
        private int AutoRefreshIntervalMs { get; set; } = 250;

        [Config(Name = "Individual Refresh Interval (ms)",
                Description = "Delay between each key's Get State command.",
                Min = 0)]
        private int IndividualRefreshIntervalMs { get; set; } = 10;

        protected PeripheralInterface ActiveInterface => KeyboardCommonProtocol.Instance.ActiveInterface;

        //- valueCells[field, pinColumn]
        private readonly TextBlock[,] valueCells = new TextBlock[5, 8];
        //- Per-pin "poll this key" checkboxes in the header row. Unchecked pins are skipped,
        //- so the sweep iterates faster when only a few keys are selected.
        private readonly CheckBox[] pinEnabledChecks = new CheckBox[8];
        //- Latest raw bytes per pin: [type, switch, deltaLo, deltaHi, absLo, absHi]
        private readonly byte[][] latest = new byte[8][];

        private DispatcherTimer pollTimer;
        //- Number of this page's Get State commands queued but not yet sent.
        //- Guards against launching a new refresh while the previous one is still draining.
        private int outstandingPolls;
        private IEnumerator commandEnumerator;
        //- The interface Parse is currently subscribed to. Tracked so we can rewire when the
        //- shared keyboard interface is reconnected while this page runs in the background.
        private PeripheralInterface subscribedInterface;

        private int testModeRow;
        private KeyData[] keyDatas;
        private TestMode currentTestMode;

        public override void Awake()
        {
            InitializeComponent();
            base.Awake();
            BuildTable();

            TestModeDropdown.SelectionChanged += TestModeDropdown_SelectionChanged;
        }

        private void TestModeDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            TestMode selectedMode = (TestMode)TestModeDropdown.SelectedIndex;

            switch (selectedMode)
            {
                case TestMode.Off:
                    TableHost.RowDefinitions[testModeRow + 1].Height = new GridLength(0);
                    foreach(var keyData in keyDatas)
                    {
                        keyData.ResetFailCount();
                    }
                    break;
                default:
                    foreach(var keyData in keyDatas)
                    {
                        keyData.Lock();
                    }
                    TableHost.RowDefinitions[testModeRow + 1].Height = GridLength.Auto;
                    break;
            }
            currentTestMode = selectedMode;
        }

        //- Runs once (first time the page is shown). Everything set up here keeps running in the
        //- background while the user is on other pages; it is only torn down in OnDestroy.
        public override void Start()
        {
            base.Start();

            outstandingPolls = 0;
            ProtocolService.OnCmdSent += OnCmdSent;
            DeviceSelection.Instance.OnActiveDeviceDisconnected += ClearAll;

            EnsureParseSubscription();

            pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(IndividualRefreshIntervalMs) };
            pollTimer.Tick += OnPollTick;
            pollTimer.Start();

            Poll();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            //- Page brought to the foreground. Polling already runs in the background; just make
            //- sure we are wired to the current interface and kick an immediate refresh.
            EnsureParseSubscription();
            Poll();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            //- Intentionally does NOT stop polling. The page keeps running in the background.
            //- All teardown happens in OnDestroy (true kill).
        }

        public override void OnDestroy()
        {
            base.OnDestroy();

            pollTimer?.Stop();
            if (pollTimer != null) pollTimer.Tick -= OnPollTick;
            pollTimer = null;

            ProtocolService.OnCmdSent -= OnCmdSent;
            DeviceSelection.Instance.OnActiveDeviceDisconnected -= ClearAll;

            if (subscribedInterface != null)
            {
                subscribedInterface.OnDataReceived -= Parse;
                subscribedInterface = null;
            }
        }

        //- Keep Parse bound to whatever interface is currently active. Cheap no-op when unchanged.
        //- Called every tick so a background reconnect (leaving and re-entering keyboard pages)
        //- automatically rewires without depending on the ambiguous OnInterfaceDisconnected event.
        private void EnsureParseSubscription()
        {
            var current = ActiveInterface;
            if (ReferenceEquals(current, subscribedInterface)) return;

            if (subscribedInterface != null)
                subscribedInterface.OnDataReceived -= Parse;

            subscribedInterface = current;

            if (subscribedInterface != null)
                subscribedInterface.OnDataReceived += Parse;
        }

        private void OnPollTick(object sender, EventArgs e)
        {
            EnsureParseSubscription();

            if (AutoRefreshCheck.IsChecked == false) return;

            var device = ActiveInterface;
            if (device == null) return;

            //- Skip if the previous refresh batch hasn't finished draining. Commands are
            //- sent with wait=true, so a fast interval could otherwise stack batches.
            if (Volatile.Read(ref outstandingPolls) > 0) return;

            commandEnumerator ??= CommandEnumerator();

            if (commandEnumerator.MoveNext())
            {
                pollTimer.Interval = TimeSpan.FromMilliseconds(IndividualRefreshIntervalMs);
            }
            else
            {
                commandEnumerator = null;
                pollTimer.Interval = TimeSpan.FromMilliseconds(AutoRefreshIntervalMs);
            }
        }

        [AppMenuItem("Refresh")]
        private async void Poll()
        {
            //- Skip if the previous refresh batch hasn't finished draining. Commands are
            //- sent with wait=true, so a fast interval could otherwise stack batches.
            if (Volatile.Read(ref outstandingPolls) > 0) return;

            //- One-shot sweep. Space each polled key's Get State by IndividualRefreshIntervalMs,
            //- mirroring the per-key pacing OnPollTick applies during auto refresh. Unchecked
            //- pins are skipped entirely (no command, no delay).
            bool firstPolled = true;
            for (int i = 0; i < Pins.Length; i++)
            {
                if (!IsPinEnabled(i)) continue;

                if (!firstPolled)
                    await Task.Delay(Math.Max(0, IndividualRefreshIntervalMs));
                firstPolled = false;

                var device = ActiveInterface;
                if (device == null) return;

                Interlocked.Increment(ref outstandingPolls);
                ProtocolService.AppendCmd(device, GetStateCmd, true, Pins[i].Pin);
            }
        }

        //- Fired by the protocol worker after each command is sent (success or failure).
        //- Decrement only for this page's own Get State commands, identified by reference.
        private void OnCmdSent(ProtocolService.CmdData cmd)
        {
            if (ReferenceEquals(cmd.Cmd, GetStateCmd))
            {
                var remaining = Interlocked.Decrement(ref outstandingPolls);

                if (remaining < 0)
                    Interlocked.Exchange(ref outstandingPolls, 0);
            }
        }

        private IEnumerator CommandEnumerator()
        {
            for (int i = 0; i < Pins.Length; i++)
            {
                //- Skip unchecked pins without consuming a tick, so the sweep is faster.
                if (!IsPinEnabled(i)) continue;

                Interlocked.Increment(ref outstandingPolls);
                ProtocolService.AppendCmd(ActiveInterface, GetStateCmd, true, Pins[i].Pin);

                yield return null;
            }
        }

        public void Parse(ReadOnlyMemory<byte> bytes, DateTime time)
        {
            var span = bytes.Span[1..];

            if (span.Length < 2) return;

            bool isError = false;
            if (span[0] == 0xFF && span[1] == 0xAA)
            {
                isError = true;
                span = span[2..];
            }

            //- Layout: [FA, 11, idxLo, idxHi, PIN(echo), type, switch,
            //-          deltaLo, deltaHi, absLo, absHi]
            if (span.Length < 11) return;
            if (span[0] != 0xFA || span[1] != 0x11) return;

            byte pin = span[4];
            int col = PinColumn(pin);
            if (col < 0) return;

            latest[col] = new[]
            {
                span[5],  // Accessory Type
                span[6],  // Switch State
                span[7],  // Encoder Delta low
                span[8],  // Encoder Delta high
                span[9], // Encoder Abs low
                span[10], // Encoder Abs high
            };

            keyDatas[col].SetAccessoryType(span[5]);
            keyDatas[col].SetSwitchState(span[6]);
            keyDatas[col].SetEncoderDelta(span[7], span[8]);
            keyDatas[col].SetEncoderAbs(span[9], span[10]);

            if(currentTestMode != TestMode.Off)
            {
                keyDatas[col].CheckLock(currentTestMode);
            }

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

            valueCells[0, col].Text = $"0x{type:X2}\n{TypeName(type)}";
            valueCells[1, col].Text = $"0x{data[1]:X2}";
            valueCells[2, col].Text = $"0x{delta:X4}";
            valueCells[3, col].Text = $"0x{abs:X4}";
            valueCells[4, col].Text = keyDatas[col].FailCount.ToString();
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
                TableHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

            //- Row 0 = header, rows 1..4 = fields + test mode.
            for (int r = 0; r <= Fields.Length; r++)
                TableHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TableHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });

            //- Top-left corner.
            AddCell("Pin", 0, 0, header: true, alignLeft: true);

            //- Header row: poll checkbox + pin label + index.
            for (int c = 0; c < Pins.Length; c++)
            {
                var (pin, label) = Pins[c];
                AddPinHeaderCell(c + 1, pin, label);
            }

            //- Field rows.
            int f;
            for (f = 0; f < Fields.Length; f++)
            {
                AddCell(Fields[f], f + 1, 0, header: true, alignLeft: true);
                for (int c = 0; c < Pins.Length; c++)
                    valueCells[f, c] = AddCell("-", f + 1, c + 1);
            }

            //- Test mode fail count row.
            testModeRow = f++;
            keyDatas = new KeyData[Pins.Length];
            AddCell("Test Mode\nFail Count", f, 0, header: true, alignLeft: true);
            for (int c = 0; c < Pins.Length; c++)
            {
                valueCells[testModeRow, c] = AddCell("0", f, c + 1);
                keyDatas[c] = new(c);
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

        //- Header cell for a pin: a "poll this key" checkbox on top of the key label.
        private void AddPinHeaderCell(int col, byte pin, string label)
        {
            var check = new CheckBox
            {
                IsChecked = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2),
            };
            pinEnabledChecks[col - 1] = check;

            var text = new TextBlock
            {
                Text = $"{label}\n(0x{pin:X2})",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextControlForeground");

            var stack = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 4, 8, 4),
            };
            stack.Children.Add(check);
            stack.Children.Add(text);

            Grid.SetRow(stack, 0);
            Grid.SetColumn(stack, col);

            TableHost.Children.Add(stack);
        }

        //- Whether the pin at the given column index should be polled.
        private bool IsPinEnabled(int index) => pinEnabledChecks[index]?.IsChecked == true;

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

        private class KeyData
        {
            public int Pin { get; }
            public byte AccessoryType { get; private set; }
            public byte SwitchState { get; private set;}
            public short EncoderDelta { get; private set;}
            public short EncoderAbs { get; private set;}

            private byte lockedAccessoryType;
            private byte lockedSwitchState;
            private short lockedEncoderDelta;
            private short lockedEncoderAbs;

            public int FailCount { get; private set; }

            public KeyData(int pin)
            {
                Pin = pin;
            }

            public void SetAccessoryType(byte value) => AccessoryType = value;
            public void SetSwitchState(byte value) => SwitchState = value;
            public void SetEncoderDelta(byte lowByte, byte highByte) => EncoderDelta = (short)((highByte << 8) | lowByte);
            public void SetEncoderAbs(byte lowByte, byte highByte) => EncoderAbs = (short)((highByte << 8) | lowByte);


            public void Lock()
            {
                lockedAccessoryType = AccessoryType;
                lockedSwitchState = SwitchState;
                lockedEncoderDelta = EncoderDelta;
                lockedEncoderAbs = EncoderAbs;
            }

            public bool CheckLock(TestMode mode)
            {
                bool match = mode switch
                {
                    TestMode.AccessoryType => AccessoryType == lockedAccessoryType,
                    TestMode.All => AccessoryType == lockedAccessoryType &&
                                       SwitchState == lockedSwitchState &&
                                       EncoderDelta == lockedEncoderDelta &&
                                       EncoderAbs == lockedEncoderAbs,
                    _ => true,
                };

                if (!match) FailCount++;

                return match;
            }

            public void ResetFailCount()
            {
                FailCount = 0;
            }
        }

        private enum TestMode
        {
            Off = 0,
            AccessoryType = 1,
            All = 2,
        }
    }
}
