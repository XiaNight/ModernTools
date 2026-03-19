using Base.Components.Chart;
using Base.Services.APIService;
using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace KeyboardHallSensor;

public abstract class MFGKeyboardStreamingPage : MFGKeyboardBasePage
{
    protected FastStripChartControl Chart { get; private set; }
    protected virtual bool CanRecord { get; set; } = false;
    protected override bool IsManualCmd { get; } = false;

    protected virtual bool CanAdjustMax { get; } = true;
    private ToggleButton minMaxToggle;
    private Button resetMinMaxButton;
    private TextBox maxValueInputField;

    #region Record

    TextBox targetKeyInputField;
    TextBox recordTimeLimitInputField;
    TextBox recordCountLimitInputField;
    private byte targetKey = 0x29;

    private Button startButton;
    private Button stopButton;
    protected bool isRecording = false;
    protected int recordCountLimit = 0;
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

        Header("Min Max");
        if (CanAdjustMax)
        {
            maxValueInputField = AddTextBox("Max Value", MaxValue.ToString(), handler: SetMaxValue);
        }

        minMaxToggle = AddToggle("Show Min Max", SetMinMaxMode);
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
            Header("Record");
            targetKeyInputField = AddTextBox("Key", handler: ParseRecordKey);
            recordTimeLimitInputField = AddTextBox("Time Limit(ms)", handler: ParseRecordDuration);
            recordCountLimitInputField = AddTextBox("Count Limit", handler: ParseRecordCount);
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

    [POST("SetRecordKey", true)]
    protected void SetRecordKey(byte key)
    {
        targetKey = key;
        targetKeyInputField.Text = key.ToString("X2");
    }

    protected bool ParseRecordKey(string key)
    {
        if(string.IsNullOrEmpty(key))
        {
            return true;
        }
        if (byte.TryParse(key, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte result))
        {
            targetKey = result;
            return true;
        }
        return false;
    }

    [POST("SetRecordDuration", true)]
    protected void SetRecordDuration(int duration)
    {
        recordDurationLimitMs = duration;
        recordTimeLimitInputField.Text = duration.ToString();
    }

    protected bool ParseRecordDuration(string duration)
    {
        if(string.IsNullOrEmpty(duration))
        {
            recordDurationLimitMs = 0;
            return true;
        }
        if (int.TryParse(duration, out int result))
        {
            recordDurationLimitMs = result;
            return true;
        }
        return false;
    }

    [POST("SetRecordCount")]
    protected void SetRecordCount(int count)
    {
        recordCountLimit = count;
        recordCountLimitInputField.Text = count.ToString();
    }

    protected bool ParseRecordCount(string count)
    {
        if (string.IsNullOrEmpty(count))
        {
            recordCountLimit = 0;
            return true;
        }
        if (int.TryParse(count, out int result))
        {
            recordCountLimit = result;
            return true;
        }
        return false;
    }

    [POST("SetRecordPath")]
    protected void SetRecordPath(string path)
    {
        recordOutputPath = path;
    }

    [POST("StartRecording")]
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

        targetKeyInputField.IsEnabled = false;
        recordTimeLimitInputField.IsEnabled = false;
        recordCountLimitInputField.IsEnabled = false;
    }

    [POST("StopRecording")]
    protected void StopRecording()
    {
        if (!isRecording) return;
        startButton.Visibility = Visibility.Visible;
        stopButton.Visibility = Visibility.Collapsed;
        isRecording = false;

        recordingStopwatch.Stop();

        targetKeyInputField.IsEnabled = true;
        recordTimeLimitInputField.IsEnabled = true;
        recordCountLimitInputField.IsEnabled = true;

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
        targetKeyInputField.Text = keycode.ToString("X2");
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

    private bool SetMaxValue(string value)
    {
        if (int.TryParse(value, out int result))
        {
            MaxValue = result;
            return true;
        }
        else
        {
            maxValueInputField.Text = MaxValue.ToString();
            return false;
        }
    }

    protected override void ClearAll()
    {
        base.ClearAll();
        StopRecording();
    }
}
