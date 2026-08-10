using Base.Components.Chart;
using Base.Core;
using Base.Services.APIService;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardHallSensor;

public abstract class MFGKeyboardStreamingPage : MFGKeyboardBasePage
{
    protected FastStripChartControl Chart { get; private set; }
    protected virtual bool CanRecord { get; set; } = false;
    protected override bool IsManualCmd { get; } = false;

    protected virtual bool CanAdjustMax { get; } = true;

    private Button resetMinMaxButton;

    /// <summary>
    /// Whether key displays show their min/max. Exposed as a Config toggle; the setter drives the
    /// live display and the visibility of the Reset button.
    /// </summary>
    [Config(Name = "Show Min Max", Header = "Min Max")]
    private bool ShowMinMax
    {
        get => showMinMax;
        set { showMinMax = value; SetMinMaxMode(value); }
    }
    private bool showMinMax;

    #region Record

    [Persist, Config(Name = "Key", Type = ConfigType.Hex, Header = "Record", Condition = nameof(CanRecord))]
    private byte targetKey = 0x29;

    private Button startButton;
    private Button stopButton;
    protected bool isRecording = false;

    [Persist, Config(Name = "Count Limit", Min = 0, Condition = nameof(CanRecord))]
    protected int recordCountLimit = 0;

    [Persist, Config(Name = "Time Limit (ms)", Min = 0, Condition = nameof(CanRecord))]
    protected int recordDurationLimitMs = 0;

    protected int recordedCount = 0;
    protected Stopwatch recordingStopwatch;
    private long startTick = 0;
    protected StreamWriter recordStream;
    protected string recordOutputPath;
    protected record RecordEntry(long microseconds, string value);

    #endregion

    public override void Awake()
    {
        base.Awake();

        // Settings (Max Value, Show Min Max, and the Record fields below) are exposed through the
        // Config dialog via [Config] attributes. Only action buttons remain in the property panel.
        resetMinMaxButton = AddButton("Reset Min Max", ResetMinMax);
        resetMinMaxButton.Visibility = Visibility.Collapsed;

        Chart = new FastStripChartControl
        {
            Width = 730,
            Height = 250,
            Margin = new Thickness(10, 10, 10, 10),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Canvas.SetLeft(Chart, -20);
        Canvas.SetTop(Chart, 320);
        Canvas.Children.Add(Chart);

        // Move the rogue keys grid to the right
        Canvas.SetLeft(RogueKeysGrid, 726);


        if (CanRecord)
        {
            startButton = AddButton("Start", StartRecording);

            stopButton = AddButton("Stop", () =>
            {
                StopRecording();
                MessageBox.Show("Recording stopped.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
            });
            stopButton.Visibility = Visibility.Collapsed;
        }
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        Chart.Start();
        Chart.MaxY = MaxValue;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        Chart.Stop();
        StopRecording();
    }

    #region Recording

    [POST("SetRecordKey", true,
        Summary = "Select which key to record.",
        Description = "Selects which key's analog stream is recorded. Body: { \"key\": integer } — the device " +
            "key code (matrix scan code, 0-255) of the single key to capture. Set this before StartRecording; " +
            "a value of 0 means \"no key\" and recording will not start.")]
    protected void SetRecordKey(byte key)
    {
        targetKey = key;
    }

    [POST("SetRecordDuration", true,
        Summary = "Set the recording time limit.",
        Description = "Sets the maximum recording duration. Body: { \"duration\": integer } in milliseconds. " +
            "Recording stops automatically once this many milliseconds have elapsed since StartRecording. " +
            "Use 0 for no time limit.")]
    protected void SetRecordDuration(int duration)
    {
        recordDurationLimitMs = duration;
    }

    [POST("SetRecordCount",
        Summary = "Set the recording sample-count limit.",
        Description = "Sets the maximum number of samples to record. Body: { \"count\": integer }. Recording " +
            "stops automatically once this many samples (\"Time,Value\" rows) have been captured. Use 0 for " +
            "no count limit.")]
    protected void SetRecordCount(int count)
    {
        recordCountLimit = count;
    }

    [POST("SetRecordPath",
        Summary = "Set the output CSV file path.",
        Description = "Sets the destination CSV file path for the next recording. Body: { \"path\": string } — " +
            "an absolute path to the output .csv file. If left unset, StartRecording prompts the user with a " +
            "save dialog instead.")]
    protected void SetRecordPath(string path)
    {
        recordOutputPath = path;
    }

    [POST("StartRecording",
        Summary = "Start recording the selected key.",
        Description = "Starts recording the selected key's analog values to the configured CSV file (with a " +
            "\"Time,Value\" header). Takes no parameters. No-op if a recording is already in progress, if no " +
            "record key has been set (see SetRecordKey), or if no device is connected. When no output path is " +
            "set, a save dialog is shown first.")]
    protected void StartRecording()
    {
        if (isRecording) return;
        if (targetKey == 0) return;
        if (ActiveInterface == null) return;

        if(string.IsNullOrEmpty(recordOutputPath))
        {
            if (!ShowSaveFileDialog(out string filepath)) return;
            recordOutputPath = filepath;
        }

        recordingStopwatch = Stopwatch.StartNew();

        recordStream = new StreamWriter(recordOutputPath);
        recordStream.WriteLine("Time,Value");

        startButton.Visibility = Visibility.Collapsed;
        stopButton.Visibility = Visibility.Visible;

        isRecording = true;
        startTick = DateTime.UtcNow.Ticks;
    }

    [POST("StopRecording",
        Summary = "Stop the current recording.",
        Description = "Stops the current recording and flushes and closes the CSV file. Takes no parameters. " +
            "Safe to call when no recording is in progress (no-op).")]
    protected void StopRecording()
    {
        if (!isRecording) return;
        startButton.Visibility = Visibility.Visible;
        stopButton.Visibility = Visibility.Collapsed;
        isRecording = false;

        recordingStopwatch.Stop();

        recordStream?.Close();
        recordStream = null;
    }

    protected bool ShowSaveFileDialog(out string filepath)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save Record Entries",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
            DefaultExt = "csv",
            FileName = "records.csv"
        };

        bool? result = dialog.ShowDialog();
        filepath = dialog.FileName;
        return result ?? false;
    }

    #endregion

    public override void Parse(ReadOnlyMemory<byte> bytes, DateTime time)
    {
        var span = bytes.Span;

        if (span.Length < 4 || span[1] != 0x04 || span[2] != 0x20 || span[3] != MfdCmdCode)
            return;

        int index = 4;

        while (index + MfgCmdPackageSize <= span.Length)
        {
            byte rowByte = span[4];
            byte keyCode = span[index];
            if (keyCode == 0) break;

            var values = bytes.Slice(index, MfgCmdPackageSize);
            ushort keyHash = (ushort)((rowByte << 8) + keyCode);

            if (!data.TryGetValue(keyHash, out Sample sample))
            {
                data[keyHash] = new() { keyCode = keyCode, values = values.ToArray(), dirtyCounter = 1 };
            }
            else
            {
                sample.values = values.ToArray();
                sample.dirtyCounter++;
            }

            if (keyCode == targetKey)
            {
                int value = ParseValue(values);
                if (isRecording)
                {
                    string timeStr = DateTime.Now.ToString("MM-dd HH:mm:ss.fff");

                    // Check recording duration ms
                    if (recordDurationLimitMs > 0 && recordingStopwatch.ElapsedMilliseconds >= recordDurationLimitMs)
                    {
                        Dispatcher.BeginInvoke(StopRecording);
                        return;
                    }

                    // Check recording count
                    if (recordCountLimit > 0 && recordedCount >= recordCountLimit)
                    {
                        Dispatcher.BeginInvoke(StopRecording);
                        return;
                    }
                    recordedCount++;
                    recordStream.WriteLine($"{timeStr},\"{value}\"");
                }
                Chart.AddSample(value, DateTime.Now.Ticks);
            }

            index += MfgCmdPackageSize;
        }
    }

    protected override void OnKeyDisplayClicked(byte keycode)
    {
        Chart.Clear();
        targetKey = keycode;
    }

    public void SetMinMaxMode(bool state)
    {
        foreach (var item in data)
        {
            item.Value.linkedKeyDisplay?.SetMinMaxDisplay(state);
        }
        resetMinMaxButton.Visibility = state ? Visibility.Visible : Visibility.Collapsed;
    }

    public new void ResetMinMax()
    {
        foreach (var item in data)
        {
            item.Value.linkedKeyDisplay?.ResetMinMax();
        }
    }

    protected override void ClearAll()
    {
        base.ClearAll();
        StopRecording();
    }
}
