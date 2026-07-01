using Base.Components.Chart;
using Base.Core;
using Base.Pages;
using Base.Services;
using Base.Services.Peripheral;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CommonProtocol;

[PageInfo("Battery Logger", Glyph = "", Description = "Log battery level of the connected device over time.")]
public partial class BatteryLogPage : PageBase
{
    private PeripheralInterface _interface;
    private DispatcherTimer _pollTimer;
    private FastStripChartControl _chart;
    private StreamWriter _logWriter;
    private int _sampleCount;
    private DateTime _startTime;
    private bool _isLogging;
    private string _logFileName;

    public BatteryLogPage()
    {
        InitializeComponent();
    }

    public override void Awake()
    {
        base.Awake();

        _pollTimer = new DispatcherTimer();
        _pollTimer.Tick += OnPollTick;

        try
        {
            _chart = new FastStripChartControl
            {
                MinY = 0,
                MaxY = 100,
                TimeWindow = 600_000,
                AxisYLabelCount = 5,
            };
            _chart.SetResourceReference(FastStripChartControl.LineBrushProperty, "Accent1Brush");
            _chart.SetResourceReference(FastStripChartControl.LabelBrushProperty, "TextPrimaryBrush");
            //_chart.SetResourceReference(FastStripChartControl.BackgroundProperty, "SurfaceAltBrush");
            ChartBorder.Child = _chart;
            _chart.Start();
        }
        catch (Exception ex)
        {
            ChartBorder.Child = new TextBlock
            {
                Text = $"Chart unavailable: {ex.Message}",
                Margin = new Thickness(8),
                Foreground = System.Windows.Media.Brushes.OrangeRed,
            };
        }
    }

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    protected override void OnEnable()
    {
        base.OnEnable();
        DeviceSelection.Instance.OnActiveDeviceConnected += OnDeviceConnected;
        DeviceSelection.Instance.OnActiveDeviceDisconnected += OnDeviceDisconnected;

        if (_interface == null && ActiveDevice != null)
            ConnectToDevice();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        DeviceSelection.Instance.OnActiveDeviceConnected -= OnDeviceConnected;
        DeviceSelection.Instance.OnActiveDeviceDisconnected -= OnDeviceDisconnected;

        if (_isLogging) StopLogging();
        DisconnectDevice();
    }

    // ─── Device connection ────────────────────────────────────────────────────

    private void OnDeviceConnected() => Dispatcher.Invoke(ConnectToDevice);

    private void OnDeviceDisconnected() => Dispatcher.Invoke(() =>
    {
        if (_isLogging) StopLogging();
        DisconnectDevice();
    });

    private void ConnectToDevice()
    {
        DisconnectDevice();
        var device = DeviceSelection.Instance.ActiveDevice;
        if (device == null || device.interfaces.Count == 0) return;

        try
        {
            int usagePage = device.PID == 0x1ACE ? 0xFF02 : 0xFF00;
            if (device.PID == 0x1C64 || device.PID == 0x1C65) usagePage = 0xFF03;

            var detail = device.interfaces.FirstOrDefault(
                i => i.UsagePage == usagePage && i.Usage == 1,
                device.interfaces[0]);

            _interface = detail.Connect(true);
            _interface.OnDataReceived += OnDataReceived;
        }
        catch (Exception ex)
        {
            Debug.Log($"[BatteryLogPage] Connect failed: {ex.Message}");
        }
    }

    private void DisconnectDevice()
    {
        if (_interface == null) return;
        _interface.OnDataReceived -= OnDataReceived;
        _interface = null;
    }

    // ─── Start / Stop ─────────────────────────────────────────────────────────

    private void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isLogging) StopLogging();
        else StartLogging();
    }

    private void StartLogging()
    {
        if (_isLogging) return;

        if (_interface == null || !_interface.IsDeviceConnected)
        {
            StatusText.Text = "No device connected.";
            return;
        }

        if (!int.TryParse(IntervalBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int secs) || secs < 1)
        {
            StatusText.Text = "Invalid interval — enter a positive integer.";
            return;
        }

        string docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _logFileName = $"BatteryLog_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        string filePath = Path.Combine(docsPath, _logFileName);

        try
        {
            _logWriter = new StreamWriter(filePath, append: false) { AutoFlush = true };
            _logWriter.WriteLine("Timestamp,RSOC_%,Voltage_mV");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Cannot create log file: {ex.Message}";
            return;
        }

        _sampleCount = 0;
        _startTime = DateTime.Now;
        _isLogging = true;

        _pollTimer.Interval = TimeSpan.FromSeconds(secs);
        _pollTimer.Start();

        StartStopButton.Content = "Stop";
        IntervalBox.IsEnabled = false;
        _chart?.Clear();

        ProtocolService.EnterFactory(_interface);
        StatusText.Text = $"Logging to {_logFileName}";
    }

    private void StopLogging()
    {
        if (!_isLogging) return;
        _isLogging = false;

        _pollTimer.Stop();
        StartStopButton.Content = "Start";
        IntervalBox.IsEnabled = true;
        StatusText.Text = $"Stopped. {_sampleCount} samples saved to {_logFileName}";

        if (_interface?.IsDeviceConnected == true)
            ProtocolService.ExitFactory(_interface);

        _logWriter?.Dispose();
        _logWriter = null;
    }

    // ─── Poll tick ────────────────────────────────────────────────────────────

    private void OnPollTick(object sender, EventArgs e)
    {
        if (_interface?.IsDeviceConnected != true)
        {
            StopLogging();
            return;
        }
        ProtocolService.AppendCmd(_interface, "power_battery_information");
    }

    // ─── Response parsing ─────────────────────────────────────────────────────

    private void OnDataReceived(ReadOnlyMemory<byte> data, DateTime timestamp)
    {
        ReadOnlySpan<byte> span = data.Span;

        if (!ProtocolService.IsCmdMatch([0xFA, 0x30], span)) return;
        if (span.Length < 8) return;

        int rsoc = span[5];
        int voltageMv = (span[7] << 8) | span[6];

        if (rsoc > 100) return;

        Application.Current.Dispatcher.Invoke(() => ApplySample(rsoc, voltageMv, timestamp));
    }

    private void ApplySample(int rsoc, int voltageMv, DateTime timestamp)
    {
        if (!_isLogging) return;

        _sampleCount++;
        _chart?.AddSample(rsoc, timestamp.Ticks);

        try
        {
            _logWriter?.WriteLine($"{timestamp:yyyy-MM-dd HH:mm:ss},{rsoc},{voltageMv}");
        }
        catch (Exception ex)
        {
            Debug.Log($"[BatteryLogPage] Log write failed: {ex.Message}");
        }

        TimeSpan elapsed = timestamp - _startTime;
        RsocText.Text = $"RSOC: {rsoc}%";
        VoltageText.Text = $"Voltage: {voltageMv} mV";
        SampleText.Text = $"Samples: {_sampleCount}";
        StatusText.Text = $"Logging... {elapsed:hh\\:mm\\:ss} elapsed · {_logFileName}";
    }
}
