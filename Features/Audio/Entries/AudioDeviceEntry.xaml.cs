using NAudio.CoreAudioApi;
using System.Windows;
using System.Windows.Controls;
using static Base.Services.DeviceSelection;

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
        public event Action<double> OnVolumeOffsetChanged;
        public void SetOffset(long offset)
        {
            OffsetTimer.Value = offset / 10_000_000d;
        }

        public AudioDeviceEntry()
        {
            InitializeComponent();

            SourceDeviceDropdown.DropDownOpened += OnSourceDeviceDropdownOpened;
            CompareDeviceDropdown.DropDownOpened += OnCompareDeviceDropdownOpened;

            OffsetTimer.ValueChanged += (s, e) => OnOffsetChanged?.Invoke((long)(OffsetTimer.Value * 10_000_000));
            MagnitudeOffset.ValueChanged += (s, e) => OnVolumeOffsetChanged?.Invoke(MagnitudeOffset.Value);

            ShowComparisonToggle.Checked += (s, e) => OnShowComparisonToggled?.Invoke(true);
            ShowComparisonToggle.Unchecked += (s, e) => OnShowComparisonToggled?.Invoke(false);
        }

        private void OnCompareDeviceDropdownOpened(object sender, EventArgs e)
        {
            CompareDeviceDropdown.Items.Clear();
            var devices = OnCompareableDeviceRequest?.Invoke() ?? [];

            var noneItem = new MenuItem() { Header = "None" };
            noneItem.Click += CompareDeviceSelected;
            CompareDeviceDropdown.Items.Add(noneItem);

            foreach (var device in devices)
            {
                var item = new MenuItem() { Header = device.FriendlyName, Tag = device };
                item.Click += CompareDeviceSelected;
                CompareDeviceDropdown.Items.Add(item);
            }

            if (ComparingDevice != null)
            {
                foreach (MenuItem item in CompareDeviceDropdown.Items)
                {
                    if (item.Tag is MMDevice device && device.ID == ComparingDevice.ID)
                    {
                        CompareDeviceDropdown.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        private void CompareDeviceSelected(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.Tag is MMDevice device)
            {
                if (ComparingDevice != null && ComparingDevice.ID == device.ID) return;
                CompareDeviceDropdown.SelectedItem = item;
                OnCompareDeviceSelected?.Invoke(device);
                CompareDeviceDropdown.IsDropDownOpen = false;
            }
            else if(sender is MenuItem noneItem && noneItem.Header as string == "None")
            {
                CompareDeviceDropdown.SelectedItem = noneItem;
                OnCompareDeviceSelected?.Invoke(null);
                CompareDeviceDropdown.IsDropDownOpen = false;
            }
        }

        private void OnSourceDeviceDropdownOpened(object sender, EventArgs e)
        {
            SourceDeviceDropdown.Items.Clear();
            foreach (var device in FindAllAudioDevices())
            {
                var item = new MenuItem() { Header = device.FriendlyName, Tag = device };
                item.Click += SourceDeviceSelected;
                SourceDeviceDropdown.Items.Add(item);
            }

            if(SelectedDevice != null)
            {
                foreach (MenuItem item in SourceDeviceDropdown.Items)
                {
                    if (item.Tag is MMDevice device && device.ID == SelectedDevice.ID)
                    {
                        SourceDeviceDropdown.SelectedItem = item;
                        break;
                    }
                }
            }
        }

        private void SourceDeviceSelected(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.Tag is MMDevice device)
            {
                if(SelectedDevice != null && SelectedDevice.ID == device.ID) return;

                SourceDeviceDropdown.SelectedItem = item;
                SelectedDevice = device;
                OnSourceDeviceSelected?.Invoke(device);
                SourceDeviceDropdown.IsDropDownOpen = false;
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
    }
}
