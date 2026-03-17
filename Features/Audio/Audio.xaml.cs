using Audio.Entries;
using Audio.Generator;
using Base;
using Base.Core;
using Base.Pages;
using NAudio.CoreAudioApi;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TimedFloat = Audio.Receiver.AudioChannelHandler.TimedValue<float>;

namespace Audio
{
    public partial class AudioPage : PageBase
    {
        public override string Glyph => "\uE189";
        public override bool ShowDeviceSelection => false;
        public bool IsTesting { get; private set; } = false;
        public int Cycle
        {
            get
            {
                if (int.TryParse(CycleTimeInputField.Text, out int result))
                {
                    return result;
                }
                return 60;
            }
        }

        public int DurationMs
        {
            get
            {
                if (int.TryParse(DurationInputField.Text, out int result))
                {
                    return result;
                }
                return 1000;
            }
        }

        public float Tolerance { get; private set; }
        public int LowCutOff { get; private set; }
        public int HighCutOff { get; private set; }
        public int TriggerSensitivity { get; private set; }
        public float NoiseCancelingEffect { get; private set; }

        public AudioPage()
        {
            InitializeComponent();
        }

        public override void Start()
        {
            base.Start();
            StartButton.Click += (s, e) =>
            {
                PrepareButton.Visibility = Visibility.Collapsed;
                ThresholdAdaptButton.Visibility = Visibility.Collapsed;
                StartButton.Visibility = Visibility.Collapsed;
                StopButton.Visibility = Visibility.Visible;
                IsTesting = true;
                StartTest();
            };
            StopButton.Click += (s, e) =>
            {
                PrepareButton.Visibility = Visibility.Visible;
                ThresholdAdaptButton.Visibility = Visibility.Visible;
                StartButton.Visibility = Visibility.Visible;
                StopButton.Visibility = Visibility.Collapsed;
                CycleTimeInputField.IsEnabled = true;
                DurationInputField.IsEnabled = true;
                IsTesting = false;
                StopTest();
            };

            PrepareButton.Click += (s, e) =>
            {
                StartAdapting(Adaptation);
            };
            ThresholdAdaptButton.Click += (s, e) =>
            {
                StartAdapting(ThresholdAdapt);
            };
            StopPrepareButton.Click += (s, e) =>
            {
                StopAdapting();
            };

            ToleranceInputField.TextChanged += (s, e) =>
            {
                ParseToleranceInput();
            };
            ParseToleranceInput();

            LowCutoffInputField.TextChanged += (s, e) =>
            {
                ParseLowCutOffInput();
            };
            ParseLowCutOffInput();

            HighCutoffInputField.TextChanged += (s, e) =>
            {
                ParseHighCutOffInput();
            };
            ParseHighCutOffInput();

            TriggerSensitivityInputField.TextChanged += (s, e) =>
            {
                ParseTriggerSensitivityInput();
            };
            ParseTriggerSensitivityInput();

            NoiseCancelingEffectSlider.ValueChanged += (s, e) =>
            {
                ParseNoiseCancelingEffect();
            };
            ParseNoiseCancelingEffect();

            CycleTimeInputField.TextChanged += (s, e) => UpdateRemainingDurationText(Cycle);
            DurationInputField.TextChanged += (s, e) => UpdateRemainingDurationText(Cycle);
            UpdateRemainingDurationText(Cycle);

            for (int i = 0; i < 10; i++)
            {
                AudioSubject newSubject = AddSubject();
            }
        }

        private void ParseToleranceInput()
        {
            string text = ToleranceInputField.Text;
            if (string.IsNullOrEmpty(text)) return;
            if (float.TryParse(text, out float value))
            {
                if (Tolerance == value) return;

                Tolerance = value;
            }
            else
            {
                ToleranceInputField.Text = Tolerance.ToString();
            }
        }

        private void ParseLowCutOffInput()
        {
            string text = LowCutoffInputField.Text.ToString();
            if (string.IsNullOrEmpty(text)) return;
            if (int.TryParse(text, out int value))
            {
                if (LowCutOff == value) return;
                LowCutOff = value;
            }
            else
            {
                LowCutoffInputField.Text = LowCutOff.ToString();
            }
        }

        private void ParseHighCutOffInput()
        {
            string text = HighCutoffInputField.Text.ToString();
            if (string.IsNullOrEmpty(text)) return;
            if(int.TryParse(text, out int value))
            {
                if (HighCutOff == value) return;
                HighCutOff = value;
            }
            else
            {
                HighCutoffInputField.Text = HighCutOff.ToString();
            }
        }

        private void ParseTriggerSensitivityInput()
        {
            string text = TriggerSensitivityInputField.Text;
            if (string.IsNullOrEmpty(text)) return;
            if (int.TryParse(text, out int value))
            {
                if (TriggerSensitivity == value) return;
                TriggerSensitivity = value;
            }
            else
            {
                TriggerSensitivityInputField.Text = TriggerSensitivity.ToString();
            }
        }

        private void ParseNoiseCancelingEffect()
        {
            NoiseCancelingEffect = (float)NoiseCancelingEffectSlider.Value;
            NoiseCancelingEffectText.Text = NoiseCancelingEffect.ToString("F1");

            foreach (AudioSubject entry in spawnedEntries)
            {
                entry.SetNoiseCancelingEffect(NoiseCancelingEffect);
            }
        }

        public void UpdateRemainingDurationText(int cycle)
        {
            int remaining = (int)Math.Ceiling(cycle * DurationMs / 1000f);
            RemainingDurationText.Text = $"Remaining Duration: {remaining} s";
        }

        [Path("Audio")]
        public override string PageName => "Audio";
        public override string Description => "Place AudioTrigger.bat under ./Tools folder.";

        public event Action<TimedFloat> InVolumeOutsideThresholdTriggered;
        public long InVolumeTriggerDebounce { get; set; } = TimeSpan.FromSeconds(1).Ticks;

        private readonly List<AudioSubject> spawnedEntries = [];
        private bool isAdapting = false;
        private Task adaptingTask;
        private CancellationTokenSource adaptCts;

        private Timer timer;
        private int remainingCycle;
        private int failedCycle;

        public override void Awake()
        {
            base.Awake();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            StartAudioStream();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            StopAudioStream();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            StopAudioStream();

            foreach (var entry in spawnedEntries)
            {
                entry.Dispose();
            }
        }

        private void StartTest()
        {
            CycleTimeInputField.IsEnabled = false;
            DurationInputField.IsEnabled = false;
            ToleranceInputField.IsEnabled = false;
            TriggerSensitivityInputField.IsEnabled = false;
            LowCutoffInputField.IsEnabled = false;
            HighCutoffInputField.IsEnabled = false;
            NoiseCancelingEffectSlider.IsEnabled = false;

            List<AudioSubject.AudioSubjectTestingState> testingStates = new();

            foreach (AudioSubject entry in spawnedEntries)
            {
                entry.SetNoiseCancelingEffect(NoiseCancelingEffect);
                entry.SetTriggerCutOff(LowCutOff, HighCutOff);
                entry.SetTriggerTolerance(Tolerance);
                testingStates.Add(entry.SetState<AudioSubject.AudioSubjectTestingState>());
            }

            int cycles = Cycle;
            int duration = DurationMs;
            remainingCycle = cycles;

            timer = new Timer((state) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (remainingCycle <= 0)
                    {
                        StopTest();
                        return;
                    }

                    foreach (var testingState in testingStates)
                    {
                        testingState.TestTick();
                    }

                    remainingCycle--;
                    UpdateRemainingDurationText(remainingCycle);
                });
            }, null, 0, duration);
        }

        private void StopTest()
        {
            CycleTimeInputField.IsEnabled = true;
            DurationInputField.IsEnabled = true;
            ToleranceInputField.IsEnabled = true;
            TriggerSensitivityInputField.IsEnabled = true;
            LowCutoffInputField.IsEnabled = true;
            HighCutoffInputField.IsEnabled = true;
            NoiseCancelingEffectSlider.IsEnabled = true;

            foreach (var entry in spawnedEntries)
            {
                if (entry.TryGetState<AudioSubject.AudioSubjectTestingState>(out _))
                    entry.ExitState();
            }

            timer?.Dispose();
            timer = null;
        }

        private void StartAudioStream()
        {
            foreach (var entry in spawnedEntries)
            {
                entry.StartAudioStream();
            }
        }

        private void StopAudioStream()
        {
            foreach (var entry in spawnedEntries)
            {
                entry.StopAudioStream();
            }
        }

        private async Task Adaptation()
        {
            List<AudioGenerator> generators = new();
            foreach (var entry in spawnedEntries)
            {
                var generator = entry.AudioGenerator;
                if (generator == null) continue;
                generators.Add(generator);
            }

            foreach (var generator in generators)
            {
                generator?.SetAmplitude(0.0001f);
                generator?.SetWaveType(AudioGenerator.WaveType.Noise);
                generator?.Start();
            }

            await Task.Delay(1000, adaptCts.Token);

            // Set entries to time alignment state
            foreach (var entry in spawnedEntries)
            {
                var timeState = entry.SetState<AudioSubject.AudioSubjectTimeAlignmentState>();
                timeState.StartNoiseCalculation();
            }

            // Wait for 5 seconds to collect ambient noise data
            await Task.Delay(5000, adaptCts.Token);

            // Stop mean calculation
            foreach (var entry in spawnedEntries)
            {
                if (entry.TryGetState<AudioSubject.AudioSubjectTimeAlignmentState>(out var state))
                {
                    state.StopNoiseCalculation();
                }
            }
            await Task.Delay(500, adaptCts.Token);

            // Volume trigger setup + check volume levels
            foreach (var generator in generators)
            {
                generator.SetAmplitude(.5f);
                generator.Start();
            }

            // Wait for volume equalize
            await Task.Delay(1500, adaptCts.Token);

            // Start volume mean calculation
            foreach (var entry in spawnedEntries)
            {
                if (entry.TryGetState<AudioSubject.AudioSubjectTimeAlignmentState>(out var state))
                {
                    state.StartVolumeCalculation();
                }
            }

            // Wait to collect volume data
            await Task.Delay(3000, adaptCts.Token);

            // Stop volume mean calculation
            foreach (var entry in spawnedEntries)
            {
                if (entry.TryGetState<AudioSubject.AudioSubjectTimeAlignmentState>(out var state))
                {
                    state.StopVolumeCalculation();
                }
            }

            // Lower volume to trigger level
            foreach (var generator in generators)
            {
                generator?.SetAmplitude(0.0001f);
                generator?.SetWaveType(AudioGenerator.WaveType.Noise);
            }
            await Task.Delay(1000, adaptCts.Token);

            // Quite down to prepare for trigger
            foreach (var entry in spawnedEntries)
            {
                if (entry.TryGetState<AudioSubject.AudioSubjectTimeAlignmentState>(out var state))
                {
                    state.SetupVolumeTrigger(2f);
                }
            }
            await Task.Delay(1500, adaptCts.Token);

            // Fire volume to trigger
            foreach (var generator in generators)
            {
                generator?.SetAmplitude(.5f);
            }
            await Task.Delay(200, adaptCts.Token);

            // Stop all generators
            foreach (var generator in generators)
                generator.Pause();

            await SpectrumAnalisis();
        }


        [AppMenuItem("Threshold Adapt")]
        private void ThresholdAdaptMenuItem_Click()
        {
            StartAdapting(ThresholdAdapt);
        }

        private async Task ThresholdAdapt()
        {
            adaptCts ??= new CancellationTokenSource();
            // Set state to adapting
            foreach (var entry in spawnedEntries)
            {
                var state = entry.SetState<AudioSubject.AudioSubjectAdaptingState>();
                state.StartSpectrumResponseCalculation();
            }
        }

        [AppMenuItem("Spectrum Analysis")]
        private void SpectrumAnalysisMenuItem_Click()
        {
            StartAdapting(SpectrumAnalisis);
        }

        private async Task SpectrumAnalisis()
        {
            List<AudioGenerator> generators = new();
            foreach (var entry in spawnedEntries)
            {
                var generator = entry.AudioGenerator;
                if (generator == null) continue;
                generators.Add(generator);
            }

            // Noise for preparation
            foreach (var generator in generators)
            {
                generator?.SetAmplitude(.5f);
                generator?.SetWaveType(AudioGenerator.WaveType.Noise);
                generator?.Start();
            }
            await Task.Delay(1000);

            // Set entries to spectrum response state
            foreach (var entry in spawnedEntries)
            {
                var timeState = entry.SetState<AudioSubject.AudioSubjectSpectrumResponseState>();
                timeState.StartSpectrumResponseCalculation();
            }

            // Noise blast for spectrum analysis
            foreach (var generator in generators)
            {
                generator?.SetAmplitude(.5f);
                generator?.SetWaveType(AudioGenerator.WaveType.Noise);
                generator?.Start();
            }
            await Task.Delay(3000);

            // Set entries to spectrum response state
            foreach (var entry in spawnedEntries)
            {
                var timeState = entry.SetState<AudioSubject.AudioSubjectSpectrumResponseState>();
                timeState.StopSpectrumResponseCalculation();
                entry.ExitState();
            }

            foreach (var generator in generators)
            {
                generator?.Pause();
            }
            StopAdapting();
        }

        [AppMenuItem("Beep")]
        private async Task Beep(int durationMs = 50)
        {
            List<AudioGenerator> generators = new();
            foreach (var entry in spawnedEntries)
            {
                var generator = entry.AudioGenerator;
                if (generator == null) continue;
                generators.Add(generator);
            }

            foreach (var generator in generators)
            {
                generator.SetAmplitude(1f);
                generator.Start();
            }

            await Task.Delay(durationMs);

            foreach (var generator in generators)
            {
                generator.Pause();
            }
        }

        private void StartAdapting(Func<Task> function)
        {
            if (adaptingTask != null && !adaptingTask.IsCompleted)
            {
                return;
            }
            adaptCts = new CancellationTokenSource();
            adaptingTask = Task.Run(function, adaptCts.Token);
            isAdapting = true;

            Dispatcher.Invoke(() =>
            {
                PrepareButton.Visibility = System.Windows.Visibility.Collapsed;
                ThresholdAdaptButton.Visibility = System.Windows.Visibility.Collapsed;
                StartButton.Visibility = System.Windows.Visibility.Collapsed;
                StopPrepareButton.Visibility = System.Windows.Visibility.Visible;
            });
        }

        private void StopAdapting()
        {
            adaptCts?.Cancel();
            isAdapting = false;
            adaptCts = null;
            foreach (var entry in spawnedEntries)
            {
                entry.ExitState();
            }

            Dispatcher.Invoke(() =>
            {
                PrepareButton.Visibility = System.Windows.Visibility.Visible;
                ThresholdAdaptButton.Visibility = System.Windows.Visibility.Visible;
                StartButton.Visibility = System.Windows.Visibility.Visible;
                StopPrepareButton.Visibility = System.Windows.Visibility.Collapsed;
            });
        }

        private AudioSubject AddSubject() 
        {
            AudioSubject newSubject = new(256, 4096, InVolumeTriggerDebounce)
            {
                RequestComparableDevice = GetSelectedOutputSources
            };
            AudioDeviceEntries.Children.Add(newSubject.AudioDeviceEntry);
            spawnedEntries.Add(newSubject);
            return newSubject;
        }

        private IEnumerable<AudioSubject> GetSelectedOutputSources()
        {
            return spawnedEntries.Where((entry) => entry is { SelectedSource.DataFlow: DataFlow.Render });
        }

        [AppMenuItem("/ShowAudioExampleWindow")]
        public static void ShowExampleWindow()
        {
            WaveFormRendererApp.MainForm form = new WaveFormRendererApp.MainForm();
            form.Show();
        }

        protected override void Update()
        {
            base.Update();
            foreach (var entry in spawnedEntries)
            {
                entry.UpdateSpectrogram();
            }

            //if (isAdapting)
            //{
            //    foreach (var entry in spawnedEntries)
            //    {
            //        entry.SetTriggerThreshold(Tolerance);
            //        entry.ShowTriggerThresholds();
            //    }
            //}
        }

        [AppMenuItem("/Show Recordings Folder")]
        private static void OpenRecordingsFolderInFileExplorer()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "recordings");
            if (Directory.Exists(path))
            {
                Process.Start("explorer.exe", path);
            }
        }

        protected static MainWindow Main = Application.Current.MainWindow as MainWindow
                ?? throw new InvalidOperationException("MainWindow not found or invalid.");

        private static void AppendLog(long timeTick)
        {
            string path = Path.Combine(MainWindow.GetToolFolder(), "log");
            if (!File.Exists(path))
            {
                File.WriteAllText(path, "");
            }

            string timeString = new DateTime(timeTick).ToString("HH:mm:ss.fff");
            string log = $"{timeString}{Environment.NewLine}";
            File.AppendAllText(path, log);
        }

        private static void TriggerBatchFile(long timestampUnix)
        {
            string path = Path.Combine(MainWindow.GetToolFolder(), "AudioTrigger.bat");
            if (!File.Exists(path)) return;

            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                Arguments = timestampUnix.ToString(),
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            System.Diagnostics.Process.Start(startInfo);
        }
    }
}
