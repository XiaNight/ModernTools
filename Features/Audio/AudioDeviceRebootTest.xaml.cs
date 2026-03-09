using Base.Core;
using Base.Pages;
using Base.Services;
using NAudio.CoreAudioApi;
using System.Windows;
using System.Windows.Controls;
using static Audio.Entries.AudioDeviceEntry;

namespace Audio
{
    /// <summary>
    /// Interaction logic for AudioDeviceRebootTest.xaml
    /// </summary>
    public partial class AudioDeviceRebootTest : PageBase
    {
        public override string PageName => "Audio Device Reboot Test";

        public const string AUDIO_DEVICE_REBOOT_TEST_RUN_AT_STARTUP_KEY = "AudioDeviceRebootTestRunAtStartup";
        public const string TEST_STATE_KEY = "AudioDeviceRebootTestState";
        public const string TEST_INFO_KEY = "AudioDeviceRebootTestInfo";
        public MMDevice SelectedDeviceA { get; private set; }
        public MMDevice SelectedDeviceB { get; private set; }

        public AudioDeviceRebootTest()
        {
            InitializeComponent();

            SourceDeviceADropdown.DropDownOpened += OnSourceDeviceADropdownOpened;
            SourceDeviceBDropdown.DropDownOpened += OnSourceDeviceBDropdownOpened;

            SourceDeviceADropdown.SelectionChanged += SourceDeviceASelected;
            SourceDeviceBDropdown.SelectionChanged += SourceDeviceBSelected;
        }

        public override void Awake()
        {
            base.Awake();

            TestState state = LocalAppDataStore.Instance.Get(TEST_STATE_KEY, TestState.Idle);
            if (state == TestState.Testing)
            {
                if (!LocalAppDataStore.Instance.TryGet(TEST_INFO_KEY, out TestInfo info))
                {
                    StopTest();
                    MessageBox.Show("Failed to retrieve test info. Stopping test.");
                }

                if (info.targetCycle == 0)
                {
                    StopTest();
                    MessageBox.Show("Invalid test info. Stopping test.");
                }
                else
                {
                    SourceDeviceADropdown.SelectedItem = info.dutADeviceName;
                    SourceDeviceBDropdown.SelectedItem = info.dutBDeviceName;

                    CycleTimeInputField.Value = info.targetCycle;
                    RetryInputField.Value = info.retryCount;
                    RetryIntervalInputField.Value = info.retryInterval;

                    DisableUI();
                    ProgressTextBlock.Visibility = Visibility.Visible;
                    ProgressTextBlock.Text = $"{info.currentCycle}/{info.targetCycle}";

                    RetryTextBlock.Visibility = Visibility.Visible;
                    RetryTextBlock.Text = "";

                    Main.SelectPage(typeof(AudioDeviceRebootTest));

                    PerformTest(info, info.retryCount);
                }
            }

            Debug.Log(Base.MainWindow.GetExePath());
        }

        private void StartTest_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedDeviceA == null)
            {
                MessageBox.Show("Please select a source device before starting the test.");
                return;
            }

            RegisterRunAtStartup();

            DisableUI();
            TestInfo info = new()
            {
                targetCycle = (int)CycleTimeInputField.Value,
                currentCycle = 0,
                retryCount = (int)RetryInputField.Value,
                retryInterval = (int)RetryIntervalInputField.Value,
                dutADeviceId = SelectedDeviceA.ID,
                dutBDeviceId = SelectedDeviceB?.ID,
                dutADeviceName = SelectedDeviceA.FriendlyName,
                dutBDeviceName = SelectedDeviceB?.FriendlyName
            };

            LocalAppDataStore.Instance.Set(TEST_STATE_KEY, TestState.Testing);
            LocalAppDataStore.Instance.Set(TEST_INFO_KEY, info);

            ProgressTextBlock.Visibility = Visibility.Collapsed;
            ProgressTextBlock.Visibility = Visibility.Collapsed;

            Reboot();
        }

        private void StopTest_Click(object sender, RoutedEventArgs e)
        {
            StopTest();
        }

        private void OnSourceDeviceADropdownOpened(object sender, EventArgs e)
        {
            SourceDeviceADropdown.Items.Clear();

            foreach (var device in FindAllAudioDevices())
            {
                var item = new DeviceItem() { Header = device.FriendlyName, Tag = device };
                SourceDeviceADropdown.Items.Add(item);
            }

            if (SelectedDeviceA != null)
            {
                foreach (DeviceItem item in SourceDeviceADropdown.Items)
                {
                    if (item.Tag is MMDevice device && device.ID == SelectedDeviceA.ID)
                    {
                        SourceDeviceADropdown.SelectedItem = item;
                        break;
                    }
                }
            }
        }
        private void OnSourceDeviceBDropdownOpened(object sender, EventArgs e)
        {
            SourceDeviceBDropdown.Items.Clear();

            foreach (var device in FindAllAudioDevices())
            {
                var item = new DeviceItem() { Header = device.FriendlyName, Tag = device };
                SourceDeviceBDropdown.Items.Add(item);
            }

            if (SelectedDeviceB != null)
            {
                foreach (DeviceItem item in SourceDeviceBDropdown.Items)
                {
                    if (item.Tag is MMDevice device && device.ID == SelectedDeviceB.ID)
                    {
                        SourceDeviceBDropdown.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        private void SourceDeviceASelected(object sender, SelectionChangedEventArgs e)
        {
            if (SourceDeviceADropdown.SelectedItem is DeviceItem item)
            {
                if (item.Tag is MMDevice device)
                {
                    if (SelectedDeviceA != null && SelectedDeviceA.ID == device.ID) return;

                    SelectedDeviceA = device;
                }
            }
        }
        private void SourceDeviceBSelected(object sender, SelectionChangedEventArgs e)
        {
            if (SourceDeviceBDropdown.SelectedItem is DeviceItem item)
            {
                if (item.Tag is MMDevice device)
                {
                    if (SelectedDeviceB != null && SelectedDeviceB.ID == device.ID) return;

                    SelectedDeviceB = device;
                }
            }
        }

        private bool FindDeviceById(string id, out MMDevice device)
        {
            var devices = FindAllAudioDevices();
            device = devices.FirstOrDefault(d => d.ID == id);
            return device != null;
        }

        private void DisableUI()
        {
            SourceDeviceADropdown.IsEnabled = false;
            SourceDeviceBDropdown.IsEnabled = false;
            CycleTimeInputField.IsEnabled = false;
            RetryInputField.IsEnabled = false;
            RetryIntervalInputField.IsEnabled = false;
            StartTestButton.Visibility = Visibility.Collapsed;
            StopTestButton.Visibility = Visibility.Visible;
        }

        private void PerformTest(TestInfo info, int remainingTries)
        {
            if (remainingTries < 0)
            {
                MessageBox.Show($"Test failed after {info.retryCount} retries.");
                StopTest();
                LocalAppDataStore.Instance.Set(TEST_STATE_KEY, TestState.Idle);
                return;
            }

            if (remainingTries != info.retryCount)
            {
                RetryTextBlock.Text = $"Retrying... ({info.retryCount - remainingTries}/{info.retryCount})";
            }
            else
            {
                RetryTextBlock.Text = "";
            }

            bool success = true;

            if (!FindDeviceById(info.dutADeviceId, out _)) success = false;
            if (!string.IsNullOrEmpty(info.dutBDeviceId) && !FindDeviceById(info.dutBDeviceId, out _)) success = false;

            if (success)
            {
                // Cycle successful, wait for next cycle
                info.currentCycle++;

                if (info.currentCycle >= info.targetCycle)
                {
                    StopTest();
                    LocalAppDataStore.Instance.Set(TEST_STATE_KEY, TestState.Completed);
                    ProgressTextBlock.Text = $"PASS: {info.currentCycle}/{info.targetCycle}";
                    RetryTextBlock.Text = "";
                    return;
                }

                ProgressTextBlock.Text = $"{info.currentCycle}/{info.targetCycle}";
                RetryTextBlock.Text = $"Tried {info.retryCount - remainingTries}/{info.retryCount} times";

                LocalAppDataStore.Instance.Set(TEST_INFO_KEY, info);

                Reboot();
            }
            else
            {
                Task.Delay(info.retryInterval).ContinueWith(_ =>
                    Dispatcher.Invoke(() => PerformTest(info, --remainingTries))
                );
            }
        }

        private void Reboot()
        {
#if !DEBUG
            WindowsRebootHandler.Reboot(secondsDelay: 10, reasonComment: "Audio Device Reboot Test");
#endif
        }

        private void AbortReboot()
        {
#if !DEBUG
            WindowsRebootHandler.AbortRebootOrShutdown();
#endif
        }

        private void RegisterRunAtStartup()
        {
#if !DEBUG
            WindowsRebootHandler.RegisterRunAtStartupCurrentUser(AUDIO_DEVICE_REBOOT_TEST_RUN_AT_STARTUP_KEY, Base.MainWindow.GetExePath());
#endif
        }

        private void UnregisterRunAtStartup()
        {
#if !DEBUG
            WindowsRebootHandler.UnregisterRunAtStartupCurrentUser(AUDIO_DEVICE_REBOOT_TEST_RUN_AT_STARTUP_KEY);
#endif
        }

        private void StopTest()
        {
            AbortReboot();

            SourceDeviceADropdown.IsEnabled = true;
            SourceDeviceBDropdown.IsEnabled = true;
            CycleTimeInputField.IsEnabled = true;
            RetryInputField.IsEnabled = true;
            RetryIntervalInputField.IsEnabled = true;
            StartTestButton.Visibility = Visibility.Visible;
            StopTestButton.Visibility = Visibility.Collapsed;

            // Save Key
            LocalAppDataStore.Instance.Set(TEST_STATE_KEY, TestState.Idle);

            UnregisterRunAtStartup();
        }

        [System.Serializable]
        public class TestInfo
        {
            public int targetCycle;
            public int currentCycle;
            public int retryCount;
            public int retryInterval;

            public string dutADeviceId;
            public string dutADeviceName;

            public string dutBDeviceId;
            public string dutBDeviceName;
        }

        public enum TestState
        {
            Idle,
            Testing,
            Completed
        }
    }
}
