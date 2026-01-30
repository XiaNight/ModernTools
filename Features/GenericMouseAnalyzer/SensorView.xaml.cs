using Base.Core;
using Base.Pages;
using Base.Services;
using Base.Services.APIService;
using Base.Services.Peripheral;
using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GenericMouseAnalyzer
{
    /// <summary>
    /// Interaction logic for SensorView.xaml
    /// </summary>
    public partial class SensorView : UserControl
    {
        public SensorView()
        {
            InitializeComponent();
        }
    }

    public class SensorViewPage : PageBase
    {
        public override string PageName => "Sensor View";
        private SensorView page;
        protected PeripheralInterface ActiveInterface { get; private set; }

        private byte[,] sensorData = new byte[36, 36];
        private bool dirty = false;

        private readonly ConcurrentQueue<byte[]> cmdQueue = new();
        private volatile bool isWriteReady = true;
        private Task pngExportTask = null;

        // Data Timer 1s
        System.Timers.Timer timer;

        // loop
        private int dataCounter = 0;
        private Task writeTask = null;
        private CancellationTokenSource writeCts = null;
        private Phase phase = Phase.Idle;
        private enum Phase
        {
            Idle,
            EnteringFactory,
            FactoryReady,
            EnteringStopMotion,
            StopMotionReady,
            WaitingRawReady,
            RawReady,
            ReadingData,
            DataReady,
            Error,
        }

        // Data
        private readonly CounterValue<int> mot = new();
        private readonly CounterValue<int> lift_stat = new();
        private readonly CounterValue<int> op_mode = new();
        private readonly CounterValue<string> op_state = new();
        private readonly MinMaxValue<int> squal = new();
        private readonly MinMaxValue<int> raw_data_sum = new();
        private readonly MinMaxValue<int> shutter_lower = new();
        private readonly MinMaxValue<int> shutter_upper = new();


        public override void Awake()
        {
            base.Awake();

            page = new();

            DeviceSelection.Instance.OnActiveDeviceConnected += ConnectToInterface;

            DeviceSelection.Instance.OnActiveDeviceDisconnected += DisconnectInterface;

            page.StartButton.Click += (_, _) => Start();
            page.StopButton.Click += (_, _) => ExitTesting();

            root.Children.Add(page);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            phase = Phase.Idle;

            StartLoop(600);

            timer = new System.Timers.Timer(500);
            timer.AutoReset = true;
            timer.Elapsed += (_, _) => ReadOtherData();

            for (int x = 0; x < 36; x++)
            {
                for (int y = 0; y < 36; y++)
                {
                    // Random Color
                    page.PNG.SetPixel(x, y, Colors.Black);
                }
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            StopLoop();
        }

        protected override void Update()
        {
            base.Update();

            switch (phase)
            {
                case Phase.FactoryReady:
                    StopMotion();
                    break;
                case Phase.StopMotionReady:
                case Phase.DataReady:
                    OutputRawData();
                    break;
                case Phase.RawReady:
                    ReadRawData();
                    break;
                case Phase.ReadingData:
                    if (dataCounter >= 36)
                    {
                        phase = Phase.DataReady;
                        dirty = true;
                    }
                    break;
                case Phase.Error:
                    ExitTesting();
                    break;
            }

            if (dirty)
            {
                dirty = false;
                for (int x = 0; x < 36; x++)
                    for (int y = 0; y < 36; y++)
                    {
                        byte color = sensorData[y, x];
                        DrawPixel(x, y, color);
                    }
                page.PNG.Flush();
            }

            page.MOT.Text = mot.ToString();
            page.Lift_Stat.Text = lift_stat.ToString();
            page.OP_Mode.Text = op_mode.ToString();
            page.OP_State.Text = op_state.ToString();
            page.SQUAL.Text = squal.ToString();
            page.Raw_Data_Sum.Text = raw_data_sum.ToString();
            page.Shutter_Lower.Text = shutter_lower.ToString();
            page.Shutter_Upper.Text = shutter_upper.ToString();
        }


        /// <summary>
        /// Export PNG file directly to user's download folder without asking with timestamp.
        /// </summary>
        [AppMenuItem("Export PNG", Key = System.Windows.Input.Key.F3)]
        [GET("export_png")]
        private void ExportPNG()
        {
            if (pngExportTask != null && !pngExportTask.IsCompleted)
                return; // still exporting

            string downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            string fileName = $"SensorData_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            string filePath = Path.Combine(downloadsPath, fileName);
            //pngExportTask =
            page.PNG.ExportPng(filePath);
        }

        private void StartWriteTask()
        {
            StopWriteTask();

            writeCts = new CancellationTokenSource();
            writeTask = WriteTask(writeCts.Token);
            isWriteReady = true;
        }

        private void StopWriteTask()
        {
            if (writeCts != null)
            {
                writeCts.Cancel();
                writeCts = null;
                writeTask = null;
            }
        }

        private async Task WriteTask(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                if (ActiveInterface == null)
                {
                    await Task.Delay(1, ct);
                    continue;
                }

                if (isWriteReady && cmdQueue.TryDequeue(out var cmd))
                {
                    isWriteReady = false;
                    _ = ActiveInterface.Write(cmd);
                }
                else
                {
                    await Task.Delay(1, ct);
                }
            }
        }

        private void DrawPixel(int x, int y, byte color)
        {
            // rotate 90 degree anticlockwise
            int rx = y;
            int ry = 35 - x;
            page.PNG.SetPixel(rx, ry, Color.FromRgb(color, color, color));
        }

        private void ConnectToInterface()
        {
            DisconnectInterface();
            ClearQueue();

            int index = ActiveDevice.interfaces.FindIndex(x => x.UsagePage == 0xFF01 && x.Usage == 0x01);
            if (index < 0) return;

            ActiveInterface = ActiveDevice.interfaces[index].Connect(true);
            ActiveInterface.OnDataReceived += (data) =>
            {
                TryParseData(data);
                TryParseFactory(data);
                TryParseStopMotion(data);
                TryParseOutputRaw(data);
                TryParseOtherData(data);
                TryParseFail(data);

                // Mark ready after processing a packet
                isWriteReady = true;
            };

            isWriteReady = true;
        }

        private void DisconnectInterface()
        {
            if (ActiveInterface == null) return;

            StopWriteTask();
            ClearQueue();
            phase = Phase.Idle;
            dataCounter = 0;
            ActiveInterface.Close();
            ActiveInterface = null;
        }

        private void TryParseData(ReadOnlyMemory<byte> data)
        {
            data = data.Slice(1); // skip report ID
            byte[] cmd = [0xFA, 0x0C, 0x0D, 0x00];
            if (data.Length < cmd.Length + 3) return;// at least 3 more bytes for X, Y, SQUAL
            if (!data.Span.Slice(0, cmd.Length).SequenceEqual(cmd)) return;

            byte low = data.Span[4];
            byte high = data.Span[5];
            byte dataLength = data.Span[6];

            var package = data.Slice(cmd.Length + 3, dataLength).ToArray();

            int index = high << 8 | low;

            for (int i = 0; i < dataLength; i++)
            {
                int x = ((index + i) % 36);
                int y = ((index + i) / 36);
                byte color = package[i];
                sensorData[y, x] = color;
            }
            dataCounter++;
        }

        private void TryParseFactory(ReadOnlyMemory<byte> data)
        {
            data = data.Slice(1); // skip report ID
            byte[] cmd = [0xFA, 0x00, 0xD3, 0xA5];
            if (data.Length < cmd.Length) return;
            if (!data.Span.Slice(0, cmd.Length).SequenceEqual(cmd)) return;
            phase = Phase.FactoryReady;
        }

        private void TryParseStopMotion(ReadOnlyMemory<byte> data)
        {
            data = data.Slice(1); // skip report ID
            byte[] cmd = [0xFA, 0x0C, 0x07];
            if (data.Length < cmd.Length) return;
            if (!data.Span.Slice(0, cmd.Length).SequenceEqual(cmd)) return;
            phase = Phase.StopMotionReady;
        }

        private void TryParseOutputRaw(ReadOnlyMemory<byte> data)
        {
            data = data.Slice(1); // skip report ID
            byte[] cmd = [0xFA, 0x0C, 0x0C];
            if (data.Length < cmd.Length) return;
            if (!data.Span.Slice(0, cmd.Length).SequenceEqual(cmd)) return;
            phase = Phase.RawReady;
        }

        private void TryParseOtherData(ReadOnlyMemory<byte> data)
        {
            data = data.Slice(1);
            byte[] cmd = [0xFA, 0x0C, 0x09];
            if (data.Length < cmd.Length) return;
            if (!data.Span.Slice(0, cmd.Length).SequenceEqual(cmd)) return;

            byte type = data.Span[4];
            var payload = data.Span[5];

            Application.Current.Dispatcher.Invoke(() =>
            {
                switch (type)
                {
                    case 0x02: // SQUAL
                        mot.Value = (payload >> 7 & 1);
                        lift_stat.Value = (payload >> 3 & 1);
                        op_mode.Value = (payload >> 2 & 1);
                        op_state.Value = $"{payload >> 1 & 1}{payload >> 0 & 1}";
                        break;
                    case 0x07: // SQUAL
                        squal.Value = payload;
                        break;
                    case 0x08: //Raw_Data_Sum
                        raw_data_sum.Value = payload;
                        break;
                    case 0x0B: // Shutter_Lower
                        shutter_lower.Value = payload;
                        break;
                    case 0x0C: // Shutter_Upper
                        shutter_upper.Value = payload;
                        break;
                }
            });
        }

        private void TryParseFail(ReadOnlyMemory<byte> data)
        {
            data = data.Slice(1);
            byte[] cmd = [0xFF, 0xAA];
            if (data.Length < cmd.Length) return;
            if (!data.Span.Slice(0, cmd.Length).SequenceEqual(cmd)) return;
            phase = Phase.Error;
            dataCounter = 0;
            timer.Stop();
        }

        [AppMenuItem("Start")]
        private void Start()
        {
            ClearQueue();
            phase = Phase.Idle;
            dataCounter = 0;
            EnterFactory();
            StartWriteTask();

            mot.Reset();
            lift_stat.Reset();
            op_mode.Reset();
            op_state.Reset();
            squal.Reset();
            raw_data_sum.Reset();
            shutter_lower.Reset();
            shutter_upper.Reset();
        }

        private void EnterFactory()
        {
            if (ActiveInterface == null) return;
            QueueCommand([0xFA, 0x00, 0xD3, 0xA5, 0x00, 0x00]);
            phase = Phase.EnteringFactory;
        }

        private void StopMotion()
        {
            QueueCommand([0xFA, 0x0C, 0x07, 0x00, 0x01, 0x00]);
            phase = Phase.EnteringStopMotion;
            timer.Start();
        }

        private void OutputRawData()
        {
            QueueCommand([0xFA, 0x0C, 0x0C, 0x00, 0x00, 0x00]);
            phase = Phase.WaitingRawReady;
        }

        private void ReadRawData()
        {
            phase = Phase.ReadingData;
            dataCounter = 0;

            for (byte col = 0; col < 36; col++)
            {
                int index = col * 36;
                byte low = (byte)(index & 0xFF);
                byte high = (byte)((index >> 8) & 0xFF);

                QueueCommand([0xFA, 0x0C, 0x0D, 0x00, low, high, 0x24]);
            }
        }

        byte[][] datacmd = new byte[][]
        {
            [0xFA, 0x0C, 0x09, 0x00, 0x02],
            [0xFA, 0x0C, 0x09, 0x00, 0x07],
            [0xFA, 0x0C, 0x09, 0x00, 0x08],
            [0xFA, 0x0C, 0x09, 0x00, 0x0B],
            [0xFA, 0x0C, 0x09, 0x00, 0x0C],
        };
        // other data
        private void ReadOtherData()
        {
            if (ActiveInterface == null) return;
            for (byte col = 0; col < datacmd.Length; col++)
            {
                QueueCommand(datacmd[col]);
            }
        }

        // clean up
        [AppMenuItem("Stop")]
        private void ExitTesting()
        {
            timer.Stop();
            StopWriteTask();
            phase = Phase.Idle;
            if (ActiveInterface == null) return;
            QueueCommand([0xFA, 0x0C, 0x07, 0x00, 0x00, 0x00]);
            QueueCommand([0xFA, 0x00, 0x00, 0x00, 0x00, 0x00]);
        }

        private void ClearQueue()
        {
            cmdQueue.Clear();
        }

        private void QueueCommand(byte[] cmd)
        {
            cmdQueue.Enqueue(cmd);
        }

        private class MinMaxValue<T>() where T : IComparable<T>
        {
            public T Min { get; private set; }
            public T Max { get; private set; }

            private bool hasValue = false;

            private T value;
            public T Value
            {
                get { return value; }
                set
                {
                    if (!hasValue)
                    {
                        Min = value;
                        Max = value;
                        hasValue = true;
                    }
                    else
                    {
                        if (value.CompareTo(Min) < 0) Min = value;
                        if (value.CompareTo(Max) > 0) Max = value;
                    }
                    this.value = value;
                }
            }

            public void Reset()
            {
                Value = default;
                hasValue = false;
                Min = default;
                Max = default;
            }

            public override string ToString()
            {
                if (hasValue)
                    return $"{Value} (Min: {Min}, Max: {Max})";
                else
                    return "N/A";
            }
        }

        public class CounterValue<T>() where T : IEquatable<T>
        {
            public int Counter { get; private set; } = 0;
            private bool hasValue = false;
            private T value = default;
            public T Value
            {
                get { return value; }
                set
                {
                    if (!hasValue)
                    {
                        this.value = value;
                        hasValue = true;
                    }
                    else if (!value?.Equals(this.value) ?? false)
                    {
                        this.value = value;
                        Counter++;
                    }
                }
            }

            public void Reset()
            {
                Value = default;
                Counter = 0;
                value = default;
                hasValue = false;
            }

            public override string ToString()
            {
                if (hasValue)
                    return $"{Value} (Count: {Counter})";
                else
                    return "N/A";
            }
        }
    }
}
