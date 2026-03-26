using NAudio.CoreAudioApi;
using System.Windows.Controls;

namespace Audio.Entries
{
    /// <summary>
    /// Interaction logic for AudioDeviceEntry.xaml
    /// </summary>
    public partial class AudioDeviceEntry : UserControl
    {
        // Source Device
        public MMDevice SelectedDevice { get; private set; }
        public event Action<MMDevice> OnSourceDeviceSelected;

        // Compare Device
        public MMDevice ComparingDevice { get; private set; }
        public event Action<MMDevice> OnCompareDeviceSelected;
        public delegate IEnumerable<MMDevice> CompareableDeviceRequestDelegate();
        public CompareableDeviceRequestDelegate OnCompareableDeviceRequest;
        public event Action<bool> OnShowComparisonToggled;

        public event Action<long> OnOffsetChanged;
        public void SetOffset(long offset)
        {
            OffsetTimer.Value = offset / 10_000_000d;
        }

        public AudioDeviceEntry()
        {
            InitializeComponent();

            SourceDeviceDropdown.DropDownOpened += OnSourceDeviceDropdownOpened;
            CompareDeviceDropdown.DropDownOpened += OnCompareDeviceDropdownOpened;

            SourceDeviceDropdown.SelectionChanged += SourceDeviceSelected;
            CompareDeviceDropdown.SelectionChanged += CompareDeviceSelected;

            OffsetTimer.ValueChanged += (s, e) => OnOffsetChanged?.Invoke((long)(OffsetTimer.Value * 10_000_000));

            ShowComparisonToggle.Checked += (s, e) => OnShowComparisonToggled?.Invoke(true);
            ShowComparisonToggle.Unchecked += (s, e) => OnShowComparisonToggled?.Invoke(false);
        }

        private void OnCompareDeviceDropdownOpened(object sender, EventArgs e)
        {
            CompareDeviceDropdown.Items.Clear();
            var devices = OnCompareableDeviceRequest?.Invoke() ?? [];

            var noneItem = new DeviceItem() { Header = "None" };
            CompareDeviceDropdown.Items.Add(noneItem);

            foreach (var device in devices)
            {
                var item = new DeviceItem() { Header = device.FriendlyName, Tag = device };
                CompareDeviceDropdown.Items.Add(item);
            }

            if (ComparingDevice != null)
            {
                foreach (DeviceItem item in CompareDeviceDropdown.Items)
                {
                    if (item.Tag is MMDevice device && device.ID == ComparingDevice.ID)
                    {
                        CompareDeviceDropdown.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        private void CompareDeviceSelected(object sender, SelectionChangedEventArgs e)
        {
            if (CompareDeviceDropdown.SelectedItem is DeviceItem item)
            {
                if (item.Tag is MMDevice device)
                {
                    if (ComparingDevice != null && ComparingDevice.ID == device.ID) return;
                    OnCompareDeviceSelected?.Invoke(device);
                }
                else if (item.Header as string == "None")
                {
                    OnCompareDeviceSelected?.Invoke(null);
                }
            }
        }

        private void OnSourceDeviceDropdownOpened(object sender, EventArgs e)
        {
            SourceDeviceDropdown.Items.Clear();

            foreach (var device in FindAllAudioDevices())
            {
                var item = new DeviceItem() { Header = device.FriendlyName, Tag = device };
                SourceDeviceDropdown.Items.Add(item);
            }

            if (SelectedDevice != null)
            {
                foreach (DeviceItem item in SourceDeviceDropdown.Items)
                {
                    if (item.Tag is MMDevice device && device.ID == SelectedDevice.ID)
                    {
                        SourceDeviceDropdown.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        private void SourceDeviceSelected(object sender, SelectionChangedEventArgs e)
        {
            if (SourceDeviceDropdown.SelectedItem is DeviceItem item)
            {
                if (item.Tag is MMDevice device)
                {
                    if (SelectedDevice != null && SelectedDevice.ID == device.ID) return;

                    SelectedDevice = device;
                    OnSourceDeviceSelected?.Invoke(device);
                }
            }
        }

        public static IEnumerable<MMDevice> FindAllAudioDevices()
        {
            using var deviceEnumerator = new MMDeviceEnumerator();
            var devices = deviceEnumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active);
            foreach (var device in devices)
            {
                yield return device;
            }
        }

        public class DeviceItem
        {
            public string Header { get; set; } = "";
            public MMDevice Tag { get; set; }

            public override string ToString()
            {
                return Header;
            }
        }
    }
}
