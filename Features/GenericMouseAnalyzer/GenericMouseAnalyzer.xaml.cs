using Base.Services;
using Base.Services.Peripheral;
using System.Collections.Concurrent;
using System.Data;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GenericMouseAnalyzer
{
    /// <summary>
    /// Interaction logic for GenericMouseAnalyzer.xaml
    /// </summary>
    public partial class GenericMouseAnalyzer : UserControl
    {
        public GenericMouseAnalyzer()
        {
            InitializeComponent();
        }
    }

    public class GenericMouseAnalyzerPage : Base.Pages.PageBase
    {
        public override string PageName => "Genric Mouse Analyzer";

        protected GenericMouseAnalyzer page;
        protected PeripheralInterface ActiveInterface { get; private set; }


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

        public override void Awake()
        {
            base.Awake();
            page = new GenericMouseAnalyzer();

            WriteableBitmap bmp = new WriteableBitmap(1024, 1024, 96, 96, PixelFormats.Bgra32, null);
            Image img = new Image { Source = bmp };
            root.Children.Add(img);

            root.Children.Add(page);
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            StartLoop();
            DeviceSelection.Instance.OnActiveDeviceConnected += ConnectToInterface;
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            StopLoop();
            DeviceSelection.Instance.OnActiveDeviceConnected -= ConnectToInterface;
        }

        protected override void Update()
        {
            base.Update();

            // Report-rate window
            float windowSeconds = 1f;
            long window = stopwatch.ElapsedTicks - TimeSpan.FromSeconds(windowSeconds).Ticks;
            while (timestamps.TryPeek(out long ts) && ts < window)
                timestamps.TryDequeue(out _);

            reportRateSmoothed = timestamps.Count / 1f;

            page.ReportRateText.Text = $"{reportRateSmoothed}";
        }

        private void ConnectToInterface()
        {
            DisconnectInterface();

            var info = DeviceSelection.Instance.ActiveDevice;

            if (ActiveDevice.interfaces.Count == 0) return;

            foreach(var interf in info.interfaces)
            {
                var conn = interf.Connect(true);
                conn.OnDataReceived += Parse;
            }

            //var usagePage = info.PID == 0x1ACE ? 0xFF01 : 0x0C;
            //if (info.interfaces.Count == 0) return;

            //var deviceInterface = info.interfaces.FirstOrDefault(@interface =>
            //    (@interface.UsagePage == usagePage) && (@interface.Usage == 1),
            //    info.interfaces[0]
            //);
            //if (deviceInterface == null) return;

            //ActiveInterface = deviceInterface.Connect(true);
            //ActiveInterface.OnDataReceived += Parse;

            stopwatch.Restart();
        }

        private void DisconnectInterface()
        {
            if (ActiveInterface == null) return;
            ActiveInterface.Close();
            ActiveInterface = null;

            stopwatch.Stop();
            timestamps.Clear();
        }

        private void Parse(ReadOnlyMemory<byte> readOnlyByte)
        {
            var data = readOnlyByte.Span;

            long tick = stopwatch.ElapsedTicks;
            timestamps.Enqueue(tick);
        }
    }
}
