using Base.Core;
using Base.Services;
using Base.Services.Peripheral;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GenericMouseAnalyzer;

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
    [Path("Mouse")]
    public override string PageName => "Genric Mouse Analyzer";

    protected GenericMouseAnalyzer page;
    protected PeripheralInterface ActiveInterface { get; private set; }

    // Report Rate
    private readonly System.Diagnostics.Stopwatch stopwatch = new();
    private readonly ConcurrentQueue<long> timestamps = new();
    private float reportRateSmoothed = 0;
    private readonly long startTime = DateTime.Now.Ticks;
    private readonly long lastTimestamp = DateTime.Now.Ticks;
    private readonly float reportRate = 0f;
    private readonly float reportRateRaw = 0f;
    private readonly float momentum = 0;
    private readonly int noDataCounter = 0;

    private const int WM_INPUT = 0x00FF;
    private const int RID_INPUT = 0x10000003;
    private const int RIM_TYPEMOUSE = 0;
    private const int RIDEV_INPUTSINK = 0x00000100;

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWMOUSE
    {
        public ushort usFlags;
        public uint ulButtons;
        public ushort usButtonFlags;
        public ushort usButtonData;
        public uint ulRawButtons;
        public int lLastX;
        public int lLastY;
        public uint ulExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUT
    {
        public RAWINPUTHEADER header;
        public RAWMOUSE mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices([In] RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

    public override void Awake()
    {
        base.Awake();
        page = new GenericMouseAnalyzer();

        WriteableBitmap bmp = new(1024, 1024, 96, 96, PixelFormats.Bgra32, null);
        Image img = new() { Source = bmp };
        root.Children.Add(img);
        root.Children.Add(page);
    }

    public override void Start()
    {
        base.Start();
        RegisterRawMouseDevice();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        ConnectToInterface();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        DisconnectInterface();
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
        timestamps.Clear();
        stopwatch.Restart();

        Main.WindowMessageReceived += OnWndProc;
    }

    private void DisconnectInterface()
    {
        if (ActiveInterface == null) return;
        ActiveInterface = null;

        stopwatch.Stop();
        timestamps.Clear();

        Main.WindowMessageReceived -= OnWndProc;
    }

    protected void OnWndProc(nint hwnd, int msg, nint wParam, nint lParam, bool handled)
    {
        if (msg == WM_INPUT)
        {
            uint dwSize = 0;
            GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref dwSize, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER)));
            if (dwSize > 0)
            {
                IntPtr buffer = Marshal.AllocHGlobal((int)dwSize);
                try
                {
                    if (GetRawInputData(lParam, RID_INPUT, buffer, ref dwSize, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER))) == dwSize)
                    {
                        RAWINPUT raw = Marshal.PtrToStructure<RAWINPUT>(buffer);
                        if ((raw.header.dwType == RIM_TYPEMOUSE) &&
                            (raw.mouse.lLastX != 0 || raw.mouse.lLastY != 0))
                        {
                            long tick = stopwatch.ElapsedTicks;
                            timestamps.Enqueue(tick);
                        }
                    }
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
    }
    private void RegisterRawMouseDevice()
    {
        var rid = new RAWINPUTDEVICE[1];
        rid[0].usUsagePage = 0x01;
        rid[0].usUsage = 0x02;
        rid[0].dwFlags = RIDEV_INPUTSINK;
        rid[0].hwndTarget = Main.Handle;

        if (!RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
        {
            int err = Marshal.GetLastWin32Error();
            Debug.Log($"RegisterRawInputDevices failed. Error {err}");
        }
    }
}
