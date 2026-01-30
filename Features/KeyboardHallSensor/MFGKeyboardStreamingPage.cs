using Base.Components.Chart;
using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace KeyboardHallSensor
{
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
        private byte targetKey = 0x29;

        private Button startButton;
        private Button stopButton;
        protected bool isRecording = false;
        private long startTick = 0;
        protected StreamWriter recordStream;
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
                targetKeyInputField = AddTextBox("Key", handler: SetRecordKey);
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
            StartLoop(60);
            Chart.MaxY = MaxValue;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            StopLoop();
            Chart.Stop();
            StopRecording();
        }

        #region Recording
        private bool SetRecordKey(string key)
        {
            if (byte.TryParse(key, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte result))
            {
                SetTargetKey(result);
                return true;
            }
            return false;
        }

        private void StartRecording()
        {
            if (isRecording) return;
            if (targetKey == 0) return;
            if (ActiveInterface == null) return;

            if (!ShowSaveFileDialog(out string filepath)) return;
            recordStream = new StreamWriter(filepath);
            recordStream.WriteLine("Time,Value");

            startButton.Visibility = Visibility.Collapsed;
            stopButton.Visibility = Visibility.Visible;

            isRecording = true;
            startTick = DateTime.UtcNow.Ticks;

            targetKeyInputField.IsEnabled = false;
        }

        private void StopRecording()
        {
            if (!isRecording) return;
            startButton.Visibility = Visibility.Visible;
            stopButton.Visibility = Visibility.Collapsed;
            isRecording = false;

            targetKeyInputField.IsEnabled = true;
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

        public override void Parse(ReadOnlyMemory<byte> bytes)
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
                    data[keyHash] = new() { keyCode = keyCode, values = values.ToArray(), isFresh = true };
                }
                else
                {
                    sample.values = values.ToArray();
                    sample.isFresh = true;
                }

                if (keyCode == targetKey)
                {
                    int value = ParseValue(values);
                    if (isRecording)
                    {
                        string time = DateTime.Now.ToString("MM-dd HH:mm:ss.fff");
                        recordStream.WriteLine($"{time},\"{value}\"");
                    }
                    Chart.AddSample(value, DateTime.Now.Ticks);
                }

                index += MfgCmdPackageSize;
            }
        }

        protected override void OnKeyDisplayClicked(byte keycode)
        {
            SetTargetKey(keycode);
        }

        protected void SetTargetKey(byte keycode)
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
}
