using Base;
using Base.Components.Chart;
using Base.Core;
using Base.Pages;
using Base.Services;
using Base.Services.Peripheral;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace Gamepad
{
    public partial class GamepadFeature : UserControl
    {
        public GamepadFeature() { InitializeComponent(); }
    }

    public class GamepadPage : PageBase
    {
        public override string PageName => "Gamepad";
        public override string Glyph => "\uE7FC";
        public override int NavOrder => 0;
        public override string Description => "Windows HID over GATT does not reflect the true BLE report rate, as the platform abstracts or fakes updates.";
        private GamepadFeature page;
        protected PeripheralInterface ActiveInterface { get; private set; }

        public int X { get; private set; } = 32768;
        public int Y { get; private set; } = 32768;
        public int RX { get; private set; } = 32768;
        public int RY { get; private set; } = 32768;
        public byte LT { get; private set; } = 0;
        public byte RT { get; private set; } = 0;

        public short Xf => NormalizeUnsigned(X);
        public short Yf => NormalizeUnsigned(Y);
        public short RXf => NormalizeUnsigned(RX);
        public short RYf => NormalizeUnsigned(RY);
        public byte LTf => LT;
        public byte RTf => RT;

        // Current state of the buttons (0/1)
        private const int BUTTON_COUNT = 32;
        public bool[] ButtonStates { get; private set; } = new bool[BUTTON_COUNT];
        private readonly int[] buttonCounter = new int[BUTTON_COUNT];
        private int zCounter = 0;
        private int rZCounter = 0;
        private int lastGamepadIndex = -1;

        private readonly ConcurrentQueue<Data> unprocessedDatas = new();

        // Report Rate
        private readonly System.Diagnostics.Stopwatch stopwatch = new();
        private readonly ConcurrentQueue<long> timestamps = new();
        private float reportRateSmoothed = 0;
        private long startTime = DateTime.Now.Ticks;
        private long lastTimestamp = DateTime.Now.Ticks;
        private float reportRate = 0f;
        private float reportRateRaw = 0f;
        private float momentum = 0;
        private int noDataCounter = 0;

        // Chart
        private Action<long> appendChartData = (_) => { }; // Add data point to chart
        private Action<long> tickChartData;
        private int chartType = 0;
        private ChartRenderMode renderMode = ChartRenderMode.Combined;
        private bool isChartPaused = false;
        private FullWindowChart fullWindowChart = null;
        private ScatterChartControl XYChart;
        private FastStripChartControl StripChart;

        // Mapping
        private bool mappingReady = false;
        private readonly List<(ushort usagePage, ushort usage, Action<int> setter)> axisMap = [];
        private bool hasHat = false;

        // Recording
        private bool isRecording = false;
        private delegate string AddRecordDataDelegate(long tick);
        private AddRecordDataDelegate addRecordData;
        private StreamWriter recordFileStream;

        public enum ButtonMap
        {
            A = 0,
            B = 1,
            XBtn = 2,
            YBtn = 3,
            LB = 4,
            RB = 5,
            Menu = 6,
            Start = 7,
            LeftStickKnob = 8,
            RightStickKnob = 9,
            DpadUp = 10,
            DpadDown = 11,
            DpadLeft = 12,
            DpadRight = 13,
        }

        public override void Awake()
        {
            base.Awake();
            page = new();

            XYChart = page.XYChart;
            StripChart = page.StripChart;

            DeviceSelection.Instance.OnActiveDeviceConnected += ConnectToInterface;

            page.StripChart.Start();
            page.StripChart.MaxY = 1200;
            page.StripChart.Capacity = 10000;

            DeviceSelection.Instance.OnActiveDeviceDisconnected += DisconnectInterface;

            page.ResetCharts.Click += (_, _) =>
            {
                Clear();
            };

            page.AutoFit.Click += (_, _) =>
            {
                bool enabled = page.AutoFit.IsChecked ?? false;
                XYChart.AutoFit = enabled;
                StripChart.AutoFit = enabled;
            };

            page.PauseButton.Click += (_, _) =>
            {
                isChartPaused = page.PauseButton.IsChecked ?? false;
            };

            page.RecordButton.Click += (_, _) =>
            {
                bool recording = page.RecordButton.IsChecked ?? false;
                if (recording) StartRecording();
                else StopRecording();
            };

            page.HeavyVibrationButton.Click += (_, _) =>
            {
                SetToVibrate(65535, 0, 1000);
            };

            page.LightVibrationButton.Click += (_, _) =>
            {
                SetToVibrate(0, 65535, 1000);
            };

            page.RenderModeButton.Click += (_, _) =>
            {
                // Cycle through render modes
                renderMode = renderMode switch
                {
                    ChartRenderMode.Line => ChartRenderMode.Dot,
                    ChartRenderMode.Dot => ChartRenderMode.Combined,
                    ChartRenderMode.Combined => ChartRenderMode.Line,
                    _ => ChartRenderMode.Combined,
                };

                SetRenderMode(renderMode);
            };

            page.FullWindowChart.Click += (_, _) =>
            {
                if (fullWindowChart != null)
                {
                    fullWindowChart.Focus();
                    page.FullWindowChart.IsChecked = true;
                    return;
                }

                // Set chart to other monitor by getting the current monitor index of this window and adding 1 (next monitor)
                int targetMonitorIndex = 1;
                var hwnd = new WindowInteropHelper(Main).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    var screen = System.Windows.Forms.Screen.FromHandle(hwnd);
                    targetMonitorIndex = Array.IndexOf(System.Windows.Forms.Screen.AllScreens, screen) + 1;
                    targetMonitorIndex = targetMonitorIndex % System.Windows.Forms.Screen.AllScreens.Length;
                }

                fullWindowChart = new FullWindowChart(targetMonitorIndex);
                fullWindowChart.Show();

                // Assign update chart target
                XYChart = fullWindowChart.XYChart;
                StripChart = fullWindowChart.StripChart;
                SetChartType(chartType);
                SetRenderMode(renderMode);

                page.StripChartContainer.Visibility = Visibility.Collapsed;
                page.XYChartContainer.Visibility = Visibility.Collapsed;
                page.FullWindowText.Visibility = Visibility.Visible;

                fullWindowChart.Loaded += (_, _) =>
                {
                    SetChartType(chartType);
                };

                fullWindowChart.OnClosedEvent += () =>
                {
                    fullWindowChart = null;

                    page.FullWindowChart.IsChecked = false;
                    page.FullWindowText.Visibility = Visibility.Collapsed;

                    // Assign update chart target
                    XYChart = page.XYChart;
                    StripChart = page.StripChart;
                    SetChartType(chartType);
                    SetRenderMode(renderMode);
                };
            };

            page.ChartType.SelectionChanged += (_, _) => SetChartType(page.ChartType.SelectedIndex);
            SetChartType(page.ChartType.SelectedIndex);
            tickChartData = (t) => StripChart.Tick(t);

            root.Children.Add(page);
        }

        public override void ThemeChanged()
        {
            base.ThemeChanged();
            UpdateGamepadController();
            page.GamepadController.UpdateAllVisuals();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            Clear();
            StartLoop();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            StopLoop();

            if (isRecording) StopRecording();
        }

        protected override void Update()
        {
            base.Update();

            page.TimestampText.Text = ((lastTimestamp - startTime) / 10000).ToString("0.#");

            if (ActiveInterface != null && ActiveInterface.IsDeviceConnected)
            {
                bool hasData = DecodeBytes();

                page.ReportRateText.Text = reportRateSmoothed.ToString("0.#");

                if (hasData)
                {
                    UpdateGamepadController();
                }
                UpdateButtons();
                if (ButtonStates[(int)ButtonMap.Menu] && ButtonStates[(int)ButtonMap.Start])
                {
                    Clear();
                }
            }
        }

        private void SetChartType(int chartIndex)
        {
            chartType = chartIndex;
            page.XYChartContainer.Visibility = chartType > 6 ? Visibility.Visible : Visibility.Collapsed;
            page.StripChartContainer.Visibility = chartType <= 6 ? Visibility.Visible : Visibility.Collapsed;
            page.ChartsCanvas.Visibility = fullWindowChart == null ? Visibility.Visible : Visibility.Collapsed;

            if (fullWindowChart != null)
            {
                fullWindowChart.XYChartContainer.Visibility = chartType > 6 ? Visibility.Visible : Visibility.Collapsed;
                fullWindowChart.StripChartContainer.Visibility = chartType <= 6 ? Visibility.Visible : Visibility.Collapsed;
            }

            page.RecordButton.IsEnabled = chartType <= 6;

            StripChart.Clear();
            XYChart.Clear();

            appendChartData = chartType switch
            {
                0 => (l) => StripChart.AddSample(reportRate, l),
                1 => (l) => StripChart.AddSample(Xf, l),
                2 => (l) => StripChart.AddSample(Yf, l),
                3 => (l) => StripChart.AddSample(RXf, l),
                4 => (l) => StripChart.AddSample(RYf, l),
                5 => (l) => StripChart.AddSample(LTf, l),
                6 => (l) => StripChart.AddSample(RTf, l),
                7 => (_) => XYChart.AddSample(new(X, -Y)),
                8 => (_) => XYChart.AddSample(new(RX, -RY)),
                _ => (_) => { }
            };

            addRecordData = chartType switch
            {
                0 => (long tick) => $"{tick},{reportRate}",
                1 => (long tick) => $"{tick},{Xf}",
                2 => (long tick) => $"{tick},{Yf}",
                3 => (long tick) => $"{tick},{RXf}",
                4 => (long tick) => $"{tick},{RYf}",
                5 => (long tick) => $"{tick},{LTf}",
                6 => (long tick) => $"{tick},{RTf}",
                _ => (long tick) => ""
            };

            switch (chartType)
            {
                case 0:
                    StripChart.MaxY = 1200; StripChart.MinY = 0; break;
                case 1:
                case 2:
                case 3:
                case 4:
                    StripChart.MaxY = 32768; StripChart.MinY = -32768; break;
                case 5:
                case 6:
                    StripChart.MaxY = 256; StripChart.MinY = 0; break;
            }
        }

        private void SetRenderMode(ChartRenderMode mode)
        {
            renderMode = mode;
            StripChart.RenderMode = mode;
            XYChart.RenderMode = mode;
        }

        private bool DecodeBytes()
        {
            Data rawData = default;
            bool hasData = false;

            // Report-rate window
            float windowSeconds = 1f;
            long window = stopwatch.ElapsedTicks - TimeSpan.FromSeconds(windowSeconds).Ticks;
            while (timestamps.TryPeek(out long ts) && ts < window)
                timestamps.TryDequeue(out _);

            // Parse all queued reports
            while (unprocessedDatas.TryDequeue(out Data newData))
            {
                rawData = newData;
                hasData = true;
                timestamps.Enqueue(rawData.tick);

                float atomicReportRate = 10_000_000f / newData.interval;

                float sensitivity = 0.1f;
                float delta = atomicReportRate - reportRate;
                if (atomicReportRate > 2000)
                {
                    sensitivity = 0.1f;
                }
                if (momentum == 0) momentum = delta * sensitivity;
                else if (delta * momentum < 0) momentum = 0;
                else
                {
                    reportRate += momentum;
                    momentum += delta * sensitivity;
                }
                reportRateRaw = atomicReportRate;

                var data = newData.data;

                if (!mappingReady)
                    BuildMapping(data);

                if (mappingReady)
                {
                    // Axes/values
                    foreach (var (up, u, setter) in axisMap)
                    {
                        if (ActiveInterface.TryGetUsageValue(data, up, u, out int v))
                            setter(v);
                    }

                    // Latest button states
                    bool[] latestButtonStates = new bool[32];

                    // Set D-Pad states to latest button states
                    if (hasHat && ActiveInterface.TryGetUsageValue(data, 0x01, 0x39, out int hat))
                        DecodeHatToDpad(hat, latestButtonStates);

                    // Set other buttons to latest button states
                    var pressedRaw = ActiveInterface.GetPressedButtons(data, 0x09);
                    foreach (var p in pressedRaw)
                    {
                        int idx = p - 1; // usages start at 1
                        if (idx >= 0 && idx < ButtonStates.Length) latestButtonStates[idx] = true;
                    }

                    // Detect button down events
                    bool[] onPressed = new bool[BUTTON_COUNT];
                    for (int i = 0; i < BUTTON_COUNT; i++)
                    {
                        onPressed[i] = latestButtonStates[i] && !ButtonStates[i];
                    }

                    // Apply button states and advance counters
                    Array.Fill(ButtonStates, false);
                    for (int i = 0; i < BUTTON_COUNT; i++) ButtonStates[i] = latestButtonStates[i];
                    for (int i = 0; i < BUTTON_COUNT; i++) buttonCounter[i] += onPressed[i] ? 1 : 0;

                    // Get Standalone LT RT from XInput
                    byte left = newData.xinput_state.Gamepad.bLeftTrigger;
                    if (left != LT) zCounter++;
                    LT = left;
                    byte right = newData.xinput_state.Gamepad.bRightTrigger;
                    if (right != RT) rZCounter++;
                    RT = right;
                }

                if (!isChartPaused)
                {
                    appendChartData.Invoke(newData.tick);
                    if (isRecording) WriteRecord(newData.tick);
                }
            }

            reportRateSmoothed = timestamps.Count / 1f;

            if (!hasData)
            {
                _ = XInput.XInputGetState(lastGamepadIndex, out XInput.XINPUT_STATE xinput_state);
                LT = xinput_state.Gamepad.bLeftTrigger;
                RT = xinput_state.Gamepad.bRightTrigger;

                if (!isChartPaused && chartType <= 6)
                {
                    if (chartType == 0 && ++noDataCounter > 5)
                    {
                        reportRate = 0;
                        appendChartData.Invoke(stopwatch.ElapsedTicks);
                    }
                    else tickChartData(stopwatch.ElapsedTicks);
                    if (isRecording) WriteRecord(stopwatch.ElapsedTicks);
                }
            }
            else
            {
                noDataCounter = 0;
            }
            return hasData;
        }

        private void UpdateGamepadController()
        {
            page.LeftJoyStick.SetStick(Xf, Yf);
            page.RightJoyStick.SetStick(RXf, RYf);

            page.GamepadController.LeftStickX = Xf;
            page.GamepadController.LeftStickY = Yf;
            page.GamepadController.RightStickX = RXf;
            page.GamepadController.RightStickY = RYf;

            page.GamepadController.LT = LTf / 255f;
            page.GamepadController.RT = RTf / 255f;

            page.GamepadController.A = ButtonStates[(int)ButtonMap.A];
            page.GamepadController.B = ButtonStates[(int)ButtonMap.B];
            page.GamepadController.X = ButtonStates[(int)ButtonMap.XBtn];
            page.GamepadController.Y = ButtonStates[(int)ButtonMap.YBtn];

            page.GamepadController.DpadDown = ButtonStates[(int)ButtonMap.DpadDown];
            page.GamepadController.DpadRight = ButtonStates[(int)ButtonMap.DpadRight];
            page.GamepadController.DpadLeft = ButtonStates[(int)ButtonMap.DpadLeft];
            page.GamepadController.DpadUp = ButtonStates[(int)ButtonMap.DpadUp];

            page.GamepadController.LB = ButtonStates[(int)ButtonMap.LB];
            page.GamepadController.RB = ButtonStates[(int)ButtonMap.RB];
            page.GamepadController.View = ButtonStates[(int)ButtonMap.Menu];
            page.GamepadController.Menu = ButtonStates[(int)ButtonMap.Start];
            page.GamepadController.LeftStickKnob = ButtonStates[(int)ButtonMap.LeftStickKnob];
            page.GamepadController.RightStickKnob = ButtonStates[(int)ButtonMap.RightStickKnob];
        }

        private void UpdateButtons()
        {
            // Counters
            page.B0.SetCounter(buttonCounter[(int)ButtonMap.A]);
            page.B1.SetCounter(buttonCounter[(int)ButtonMap.B]);
            page.B2.SetCounter(buttonCounter[(int)ButtonMap.XBtn]);
            page.B3.SetCounter(buttonCounter[(int)ButtonMap.YBtn]);
            page.B4.SetCounter(buttonCounter[(int)ButtonMap.LB]);
            page.B5.SetCounter(buttonCounter[(int)ButtonMap.RB]);
            page.B6.SetCounter(buttonCounter[(int)ButtonMap.Menu]);
            page.B9.SetCounter(buttonCounter[(int)ButtonMap.Start]);
            page.B10.SetCounter(buttonCounter[(int)ButtonMap.LeftStickKnob]);
            page.B11.SetCounter(buttonCounter[(int)ButtonMap.RightStickKnob]);
            page.B12.SetCounter(buttonCounter[(int)ButtonMap.DpadUp]);
            page.B13.SetCounter(buttonCounter[(int)ButtonMap.DpadDown]);
            page.B14.SetCounter(buttonCounter[(int)ButtonMap.DpadLeft]);
            page.B15.SetCounter(buttonCounter[(int)ButtonMap.DpadRight]);

            // States
            page.B0.SetValue(ButtonStates[(int)ButtonMap.A]);
            page.B1.SetValue(ButtonStates[(int)ButtonMap.B]);
            page.B2.SetValue(ButtonStates[(int)ButtonMap.XBtn]);
            page.B3.SetValue(ButtonStates[(int)ButtonMap.YBtn]);
            page.B4.SetValue(ButtonStates[(int)ButtonMap.LB]);
            page.B5.SetValue(ButtonStates[(int)ButtonMap.RB]);
            page.B6.SetValue(ButtonStates[(int)ButtonMap.Menu]);
            page.B9.SetValue(ButtonStates[(int)ButtonMap.Start]);
            page.B10.SetValue(ButtonStates[(int)ButtonMap.LeftStickKnob]);
            page.B11.SetValue(ButtonStates[(int)ButtonMap.RightStickKnob]);
            page.B12.SetValue(ButtonStates[(int)ButtonMap.DpadUp]);
            page.B13.SetValue(ButtonStates[(int)ButtonMap.DpadDown]);
            page.B14.SetValue(ButtonStates[(int)ButtonMap.DpadLeft]);
            page.B15.SetValue(ButtonStates[(int)ButtonMap.DpadRight]);
            page.LeftTrigger.SetValue(LT);
            page.RightTrigger.SetValue(RT);

            page.LeftTrigger.SetCounter(zCounter);
            page.RightTrigger.SetCounter(rZCounter);
        }

        [AppMenuItem("Vibration/Both", true)]
        [AppMenuItem("Vibration/Heavy", true, 65535)]
        [AppMenuItem("Vibration/Light", true, 0, 65535)]
        [AppMenuItem("Vibration/Stop", true, 0, 0)]
        private static async void SetToVibrate(ushort heavy = 65535, ushort light = 65535, int durationMs = 1000)
        {
            var vibration = new XInput.XINPUT_VIBRATION
            {
                wLeftMotorSpeed = heavy,
                wRightMotorSpeed = light
            };

            // Start vibration
            _ = XInput.XInputSetState(0, ref vibration);

            if (durationMs > 0)
            {
                await Task.Delay(durationMs);

                // Stop vibration
                var stop = new XInput.XINPUT_VIBRATION(); // both 0
                _ = XInput.XInputSetState(0, ref stop);
            }
        }

        private void ConnectToInterface()
        {
            DisconnectInterface();

            if (ActiveDevice.interfaces.Count == 0) return;
            int index = ActiveDevice.interfaces.FindIndex(x => x.UsagePage == 0x01 && x.Usage == 0x05); // Gamepad
            if (index < 0)
            {
                index = ActiveDevice.interfaces.FindIndex(x => x.UsagePage == 0x1800); // Joystick
            }
            if (index < 0) index = 0;

            Clear();

            ActiveInterface = ActiveDevice.interfaces[index].Connect(true);
            ActiveInterface.OnDataReceived += Parse;

            // reset mapping state
            mappingReady = false;
            axisMap.Clear();
            hasHat = false;
            lastGamepadIndex = FindGamepadIndex();

            page.ConnectedText.Text = "Yes";
            page.RecordButton.IsEnabled = true;
            page.HeavyVibrationButton.IsEnabled = true;
            page.LightVibrationButton.IsEnabled = true;
        }

        private void DisconnectInterface()
        {
            if (ActiveInterface == null) return;
            ActiveInterface.Close();
            ActiveInterface = null;

            page.ConnectedText.Text = "No";
            page.RecordButton.IsEnabled = false;
            page.HeavyVibrationButton.IsEnabled = false;
            page.LightVibrationButton.IsEnabled = false;
        }

        private void Parse(ReadOnlyMemory<byte> data)
        {
            long tick = stopwatch.ElapsedTicks;
            if (data.IsEmpty) return;
            if (unprocessedDatas.Count > 1000) return;

            long interval = tick - lastTimestamp;
            lastTimestamp = tick;

            _ = XInput.XInputGetState(lastGamepadIndex, out XInput.XINPUT_STATE xinput_state);

            // Retain a stable copy for deferred processing
            byte[] owned = data.ToArray();
            unprocessedDatas.Enqueue(new(owned, tick, interval, xinput_state));
        }

        private int FindGamepadIndex()
        {
            for (int i = 0; i < 4; i++)
            {
                int result = XInput.XInputGetKeystroke(i, 0, out XInput.XINPUT_KEYSTROKE keystroke);
                if (result != XInput.ERROR_DEVICE_NOT_CONNECTED)
                {
                    _ = XInput.XInputGetState(i, out XInput.XINPUT_STATE xinput_state);

                    // Check input values, if none 0, return gamepad index
                    if (xinput_state.dwPacketNumber > 0) return keystroke.UserIndex;
                }
            }
            return -1;
        }

        private void BuildMapping(byte[] firstReport)
        {
            if (ActiveInterface == null) return;

            // Preferred axis order: X,Y,Rx,Ry,Z,Rz,Slider,Dial
            var candidates = new (ushort up, ushort u, Action<int> set)[]
            {
                (0x01, 0x30, v => X  = (ushort)v), // 48 X 
                (0x01, 0x31, v => Y  = (ushort)v), // 49 Y
                (0x01, 0x33, v => RX = (ushort)v), // 51 Rx
                (0x01, 0x34, v => RY = (ushort)v), // 52 Ry
                (0x01, 0x32, v => LT = (byte)v), // 50 Z (or LT/RT depending on device)
                (0x01, 0x35, v => RT = (byte)v), // 53 RZ
                (0x01, 0x36, v => { /* Slider */ }), // 54
                (0x01, 0x37, v => { /* Dial */ }), // 55
            };

            axisMap.Clear();
            foreach (var (up, u, set) in candidates)
            {
                if (ActiveInterface.TryGetUsageValue(firstReport, up, u, out int v))
                {
                    axisMap.Add((up, u, set));
                    set(v); // seed initial value
                }
            }

            // Hat switch?
            hasHat = ActiveInterface.TryGetUsageValue(firstReport, 0x01, 0x39, out _);

            mappingReady = true;
        }

        [AppMenuItem("Clear")]
        private void Clear()
        {
            stopwatch.Restart();
            timestamps.Clear();
            lastTimestamp = stopwatch.ElapsedTicks;
            startTime = stopwatch.ElapsedTicks;

            StripChart.Clear();
            XYChart.Clear();
            page.LeftJoyStick.Clear();
            page.RightJoyStick.Clear();
            page.RightTrigger.Clear();
            page.LeftTrigger.Clear();
            for (int i = 0; i < buttonCounter.Length; i++) buttonCounter[i] = 0;
            zCounter = 0;
            rZCounter = 0;
        }

        private static void DecodeHatToDpad(int hat, bool[] buttons)
        {
            // HID hat convention: 1..8 for directions, 0 = null (released) — but check device logical min/max.
            bool up = false, down = false, left = false, right = false;
            switch (hat)
            {
                case 1: up = true; break;
                case 2: up = true; right = true; break;
                case 3: right = true; break;
                case 4: right = true; down = true; break;
                case 5: down = true; break;
                case 6: down = true; left = true; break;
                case 7: left = true; break;
                case 8: left = true; up = true; break;
                default: break; // 0 or out-of-range -> released
            }
            buttons[(int)ButtonMap.DpadUp] = up;
            buttons[(int)ButtonMap.DpadDown] = down;
            buttons[(int)ButtonMap.DpadLeft] = left;
            buttons[(int)ButtonMap.DpadRight] = right;
        }

        #region Recording

        private void StartRecording()
        {
            StopRecording();

            if (recordFileStream != null)
            {
                try { recordFileStream.Close(); } catch { }
                recordFileStream = null;
            }

            try
            {
                string directory = MainWindow.GetOutputFolder();
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                string filePath = Path.Combine(directory, $"GamepadRecord_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);

                recordFileStream = new StreamWriter(stream);
                recordFileStream.WriteLine("Tick,Value");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting recording: {ex.Message}");
                return;
            }

            isRecording = true;
            page.ChartType.IsEnabled = false;
            page.AutoFit.IsEnabled = false;
            page.PauseButton.IsEnabled = false;
        }

        private void WriteRecord(long tick)
        {
            string data = addRecordData?.Invoke(tick) ?? string.Empty;
            if (string.IsNullOrEmpty(data)) return;

            try
            {
                recordFileStream?.WriteLine(data);
                recordFileStream?.Flush();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error writing record: {ex.Message}");
                StopRecording();
            }
        }

        private void StopRecording()
        {
            if (!isRecording) return;

            try
            {
                recordFileStream?.Close();
                recordFileStream = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping recording: {ex.Message}");
            }

            page.ChartType.IsEnabled = true;
            page.AutoFit.IsEnabled = true;
            page.PauseButton.IsEnabled = true;

            isRecording = false;
        }

        #endregion

        private static short NormalizeUnsigned(int v) => (short)(v + short.MinValue);

        public struct Data
        {
            public readonly byte[] data;
            public readonly long tick;
            public readonly long interval;
            public readonly XInput.XINPUT_STATE xinput_state;

            public Data(byte[] data, long tick, long interval, XInput.XINPUT_STATE xinput_state)
            {
                if (data == null) { this = default; return; }
                this.tick = tick;
                this.interval = interval;
                this.data = data;
                this.xinput_state = xinput_state;
            }
        }
    }
}
