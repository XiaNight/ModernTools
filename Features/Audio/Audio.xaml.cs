using Audio.Entries;
using Audio.Generator;
using Base;
using Base.Core;
using Base.Pages;
using NAudio.CoreAudioApi;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using TimedFloat = Audio.Receiver.AudioChannelHandler.TimedValue<float>;

namespace Audio
{
    public partial class AudioPage : PageBase
    {
        public override string Glyph => "\uE189";
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

        public float Threshold { get; private set; }

        public event Action OnTestStart;
        public event Action OnTestStop;
        public event Action OnAdaptationStarted;
        public event Action OnAdaptationStopped;

        public AudioPage()
        {
            InitializeComponent();

            StartButton.Click += (s, e) =>
            {
                StartButton.Visibility = System.Windows.Visibility.Collapsed;
                StopButton.Visibility = System.Windows.Visibility.Visible;
                PrepareButton.Visibility = System.Windows.Visibility.Collapsed;
                IsTesting = true;
                OnTestStart?.Invoke();
            };
            StopButton.Click += (s, e) =>
            {
                StartButton.Visibility = System.Windows.Visibility.Visible;
                StopButton.Visibility = System.Windows.Visibility.Collapsed;
                PrepareButton.Visibility = System.Windows.Visibility.Visible;
                CycleTimeInputField.IsEnabled = true;
                DurationInputField.IsEnabled = true;
                IsTesting = false;
                OnTestStop?.Invoke();
            };

            PrepareButton.Click += (s, e) =>
            {
                OnAdaptationStarted?.Invoke();
                PrepareButton.Visibility = System.Windows.Visibility.Collapsed;
                StartButton.Visibility = System.Windows.Visibility.Collapsed;
                StopPrepareButton.Visibility = System.Windows.Visibility.Visible;
            };
            StopPrepareButton.Click += (s, e) =>
            {
                OnAdaptationStopped?.Invoke();
            };

            ThresholdInputField.TextChanged += (s, e) =>
            {
                ParseThresholdInput();
            };
            ParseThresholdInput();

            CycleTimeInputField.TextChanged += (s, e) => UpdateRemainingDurationText(Cycle);
            DurationInputField.TextChanged += (s, e) => UpdateRemainingDurationText(Cycle);
            UpdateRemainingDurationText(Cycle);
        }

        private void ParseThresholdInput()
        {
            string text = ThresholdInputField.Text;
            if (string.IsNullOrEmpty(text)) return;
            if (float.TryParse(text, out float value))
            {
                Threshold = value;
            }
            else
            {
                ThresholdInputField.Text = Threshold.ToString();
            }
        }

        public void UpdateRemainingDurationText(int cycle)
        {
            int remaining = (int)Math.Ceiling(cycle * DurationMs / 1000f);
            RemainingDurationText.Text = $"Remaining Duration: {remaining} s";
        }

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

            OnTestStart += StartTest;
            OnTestStop += StopTest;

            OnAdaptationStarted += StartAdapting;
            OnAdaptationStopped += StopAdapting;

            for (int i = 0; i < 10; i++)
            {
                AudioSubject newSubject = AddSubject();
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            StartLoop(120);
            StartAudioStream();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            StopLoop();
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

            List<AudioSubject.AudioSubjectTestingState> testingStates = new();

            foreach (var entry in spawnedEntries)
                testingStates.Add(entry.SetState<AudioSubject.AudioSubjectTestingState>());

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

        private void StartAdapting()
        {
            if (adaptingTask != null && !adaptingTask.IsCompleted)
            {
                return;
            }
            adaptCts = new CancellationTokenSource();
            adaptingTask = Task.Run(Adaptation, adaptCts.Token);
            isAdapting = true;
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
                generator?.SetWaveType(AudioGenerator.WaveType.Sine);
                generator.SetAmplitude(3f);
                generator.Start();
            }

            // Wait for volume equalize
            await Task.Delay(2000, adaptCts.Token);

            // Start volume mean calculation
            foreach (var entry in spawnedEntries)
            {
                if (entry.TryGetState<AudioSubject.AudioSubjectTimeAlignmentState>(out var state))
                {
                    state.StartVolumeCalculation();
                }
            }

            // Wait for 5 seconds to collect volume data
            await Task.Delay(3000, adaptCts.Token);

            // Stop volume mean calculation
            foreach (var entry in spawnedEntries)
            {
                if (entry.TryGetState<AudioSubject.AudioSubjectTimeAlignmentState>(out var state))
                {
                    state.StopVolumeCalculation();
                }
            }
            await Task.Delay(250, adaptCts.Token);

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
                    state.SetupVolumeTrigger(1);
                }
            }
            await Task.Delay(1500, adaptCts.Token);

            // Fire volume to trigger
            foreach (var generator in generators)
            {
                generator?.SetAmplitude(1f);
                generator?.SetWaveType(AudioGenerator.WaveType.Sine);
            }
            await Task.Delay(200, adaptCts.Token);

            

            /*
            // short rest after beep
            await Task.Delay(1000, adaptCts.Token);

            // Set state to adapting
            foreach (var entry in spawnedEntries)
                entry.SetState<AudioSubject.AudioSubjectAdaptingState>();

            // Adapt for 3 seconds
            await Task.Delay(3000, adaptCts.Token);
            */

            // Stop all generators
            foreach (var generator in generators)
                generator.Pause();

            // Stop adapting
            StopAdapting();
        }

        [AppMenuItem("Spectrum Analysis")]
        private async void SpectrumAnalisis()
        {
            List<AudioGenerator> generators = new();
            foreach (var entry in spawnedEntries)
            {
                var generator = entry.AudioGenerator;
                if (generator == null) continue;
                generators.Add(generator);
            }
            // Noise blast for spectrum analysis
            foreach (var generator in generators)
            {
                generator?.SetAmplitude(.5f);
                generator?.SetWaveType(AudioGenerator.WaveType.Noise);
                generator?.Start();
            }
            await Task.Delay(10000);

            foreach (var generator in generators)
            {
                generator?.Pause();
            }
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

        private void StopAdapting()
        {
            adaptCts?.Cancel();
            isAdapting = false;
            foreach (var entry in spawnedEntries)
            {
                entry.ExitState();
            }

            Dispatcher.Invoke(() =>
            {
                StopPrepareButton.Visibility = System.Windows.Visibility.Collapsed;
                StartButton.Visibility = System.Windows.Visibility.Visible;
                PrepareButton.Visibility = System.Windows.Visibility.Visible;
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

            if (isAdapting)
            {
                foreach (var entry in spawnedEntries)
                {
                    entry.SetTriggerThreshold(Threshold);
                    entry.ShowTriggerThresholds();
                }
            }
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
            string path = Path.Combine(Main.GetToolFolder(), "log");
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
            string path = Path.Combine(Main.GetToolFolder(), "AudioTrigger.bat");
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
