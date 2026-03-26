// BleScanner.xaml.cs
using Base.Core;
using Base.Helpers;
using Base.Services;
using Base.Services.Peripheral;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Windows.Devices.Bluetooth.Advertisement;

namespace Base.Pages
{
    public partial class BleScanner : UserControl
    {
        public ObservableCollection<BleDeviceViewModel> Devices { get; } = new();
        private readonly Dictionary<ulong, BleDeviceViewModel> _byAddress = new();
        private GridViewColumnHeader _lastHeaderClicked;
        private ListSortDirection _lastDirection = ListSortDirection.Ascending;

        public ICommand ConnectCommand { get; }

        public BleScanner()
        {
            InitializeComponent();
            DataContext = this;

            ConnectCommand = new RelayCommand<BleDeviceViewModel>(OnConnect);
        }

        private async void OnConnect(BleDeviceViewModel device)
        {
            if (device == null) return;
            System.Diagnostics.Debug.WriteLine($"Trying to connect to {device.Name} [{device.Address}] ...");

            var interfaceDetails = await BLEInterface.CreateInterfaceDetailFromAddress(device.Address);
            if (interfaceDetails == null) return;
            var devices = DeviceSelection.ConstructDevice(interfaceDetails.Cast<IPeripheralDetail>());
            DeviceSelection.Instance.Connect(devices[0]);

            /*
            var newDevice = BLEInterface.FromAddress(device.Address, true);
            newDevice.OnDataReceived += (data) =>
            {
                string hex = BitConverter.ToString(data.ToArray()).Replace("-", " ");
                System.Diagnostics.Debug.WriteLine($"[BLE] Received: {hex}");
            };

            // Get Detail
            await newDevice.Write([0xFF, 0x00, 0x24, 0x00]);

            // Turn lights on and off
            await Task.Delay(1000);
            await newDevice.Write([0xFF, 0x02, 0xA2, 0x10, 0x01, 0x00]);
            await Task.Delay(1000);
            await newDevice.Write([0xFF, 0x02, 0xA2, 0x10, 0x01, 0x01]);
            await Task.Delay(1000);
            await newDevice.Write([0xFF, 0x02, 0xA2, 0x10, 0x01, 0x00]);
            await Task.Delay(1000);
            await newDevice.Write([0xFF, 0x02, 0xA2, 0x10, 0x01, 0x01]);
            


            if (newDevice != null)
            {
                MessageBox.Show($"Connected to {newDevice.ProductInfo.Product} {newDevice.ProductInfo.Manufacturer:X}");
            }
            else
            {
                MessageBox.Show($"Failed to connect to device.");
            }
            */
        }

        public void UpsertDevice(string name, ulong address, int rssi)
        {
            Dispatcher.Invoke(() =>
            {
                if (_byAddress.TryGetValue(address, out var existing))
                {
                    existing.Name = string.IsNullOrEmpty(name) ? existing.Name : name;
                    existing.Rssi = rssi;
                    existing.LastSeen = DateTimeOffset.Now;
                }
                else
                {
                    var vm = new BleDeviceViewModel
                    {
                        Name = string.IsNullOrEmpty(name) ? "!N/A" : name,
                        Address = address,
                        Rssi = rssi,
                        LastSeen = DateTimeOffset.Now
                    };
                    _byAddress[address] = vm;
                    Devices.Add(vm);
                }
            });
        }
        private void DeviceList_OnColumnClick(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is GridViewColumnHeader headerClicked && headerClicked.Column != null)
            {
                string sortBy = headerClicked.Column.Header as string;
                if (string.IsNullOrEmpty(sortBy)) return;

                ListSortDirection direction = (headerClicked == _lastHeaderClicked && _lastDirection == ListSortDirection.Ascending)
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;

                Sort(sortBy, direction);

                _lastHeaderClicked = headerClicked;
                _lastDirection = direction;
            }
        }

        private void Sort(string sortBy, ListSortDirection direction)
        {
            var dataView = CollectionViewSource.GetDefaultView(DeviceList.ItemsSource);
            dataView.SortDescriptions.Clear();

            // Match GridView header to property name
            string property = sortBy switch
            {
                "Name" => nameof(BleDeviceViewModel.Name),
                "Address" => nameof(BleDeviceViewModel.Address),
                "RSSI (dBm)" => nameof(BleDeviceViewModel.Rssi),
                "Last Seen" => nameof(BleDeviceViewModel.LastSeen),
                _ => null
            };

            if (property != null)
            {
                dataView.SortDescriptions.Add(new SortDescription(property, direction));
                dataView.Refresh();
            }
        }
    }

    public class BleScannerPage : PageBase
    {
        public override string PageName => "BLE Scanner";
        public override int NavOrder => -1;

        private BleScanner page;
        private BluetoothLEAdvertisementWatcher watcher;

        public override void Awake()
        {
            base.Awake();
            page = new BleScanner();
            root.Children.Add(page);
        }

        [AppMenuItem("Start Scan")]
        public void StartScan()
        {
            Task task = Task.Run(() =>
            {
                if (watcher != null)
                {
                    switch (watcher.Status)
                    {
                        case BluetoothLEAdvertisementWatcherStatus.Created:
                        case BluetoothLEAdvertisementWatcherStatus.Stopped:
                            watcher.Received += Recieved;
                            watcher.Start();
                            return;
                        case BluetoothLEAdvertisementWatcherStatus.Started:
                            watcher.Stop();
                            watcher = null;
                            break;
                        case BluetoothLEAdvertisementWatcherStatus.Aborted:
                            watcher = null;
                            break;
                    }
                }
                watcher = BLEInterface.StartWatcher(Recieved);

                void Recieved(BluetoothLEAdvertisementWatcher watcher, BluetoothLEAdvertisementReceivedEventArgs args)
                {
                    page.UpsertDevice(args.Advertisement.LocalName, args.BluetoothAddress, args.RawSignalStrengthInDBm);
                }
                ;
            });
            task.Wait(TimeSpan.FromSeconds(1));
        }
    }

    public class BleDeviceViewModel : INotifyPropertyChanged
    {
        private string _name;
        private ulong _address;
        private int _rssi;
        private DateTimeOffset _lastSeen;

        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
        public ulong Address { get => _address; set { _address = value; OnPropertyChanged(); } }
        public int Rssi { get => _rssi; set { _rssi = value; OnPropertyChanged(); } }
        public DateTimeOffset LastSeen
        {
            get => _lastSeen;
            set
            {
                _lastSeen = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LastSeenFormatted));
            }
        }

        public string LastSeenFormatted => _lastSeen.ToString("HH:mm:ss");
        public string AddressFormatted => _address.ToString("X");

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string propName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
