using System.Windows;

namespace Base.Services;

using Base.Services.APIService;
using Core;
using Peripheral;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

public class DeviceSelection : WpfBehaviourSingleton<DeviceSelection>
{
    public event Action<List<Device>> OnConnectedDevicesUpdated;
    public UIEvent OnSelectedDeviceChanged = new();
    public UIEvent OnActiveDeviceConnected = new();
    public UIEvent OnActiveDeviceDisconnected = new();

    private Task<List<Device>> refreshTask;
    public List<Device> DiscoveredDevices { get; private set; } = new();
    private TextBlock pendingCmdCountText;
    private Device lastConnectedDevice;
    private Device defferSelectedDevice;

    private const string LAST_CONNECTED_DEVICE_KEY = "last_connected_device";

    public Device ActiveDevice { get; private set; }

    public class UIEvent
    {
        private event Action eventAction;
        public void Invoke()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                eventAction?.Invoke();
            });
        }

        public static UIEvent operator +(UIEvent a, Action handler)
        {
            a.eventAction += handler;
            return a;
        }

        public static UIEvent operator -(UIEvent a, Action handler)
        {
            a.eventAction -= handler;
            return a;
        }

        public void AddListener(Action handler)
        {
            eventAction += handler;
        }

        public void RemoveListener(Action handler)
        {
            eventAction -= handler;
        }
    }

    public override void Awake()
    {
        ApplyComboxStyle();

        OnConnectedDevicesUpdated += UpdatePortComboBox;
        var deviceName = Main.MainFooter.AddLeft();
        pendingCmdCountText = new TextBlock()
        {
            Text = "Pending commands: 0",
            FontSize = 14,
            Margin = new Thickness(4, -1, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        deviceName.Add(pendingCmdCountText);

        Main.ConnectButton.Click += (_, _) => { _ = Connect(); };
        Main.DisconnectButton.Click += (_, _) => { _ = Disconnect(); };

        ProtocolService.OnCmdSent += (_) => UpdatePendingCmdCount();
        ProtocolService.OnCmdQueued += (_) => UpdatePendingCmdCount();

        OnActiveDeviceConnected += () =>
        {
            Main.PortComboBox.Text = ActiveDevice.ToString();
            Main.PortComboBox.IsEnabled = false;
            Main.ConnectButton.Visibility = Visibility.Hidden;
            Main.DisconnectButton.Visibility = Visibility.Visible;
            Main.MainFooter.DeviceName.Text = $"{ActiveDevice.productName}";
            Main.MainFooter.DeviceVersion.Text = $"FW: ----";
            Main.MainFooter.DeviceVersion.Visibility = Visibility.Visible;
        };
        OnActiveDeviceDisconnected += () =>
        {
            Main.PortComboBox.IsEnabled = true;
            Main.ConnectButton.Visibility = Visibility.Visible;
            Main.DisconnectButton.Visibility = Visibility.Hidden;
            Main.MainFooter.DeviceName.Text = "Disconnected";
            Main.MainFooter.DeviceVersion.Text = $"FW: ----";
            Main.MainFooter.DeviceVersion.Visibility = Visibility.Collapsed;
            Main.MainFooter.BatteryIndicator.Visibility = Visibility.Collapsed;
            Main.MainFooter.BatteryIndicator.Reset();
        };
        //Main.BlockEventToggle.OnValueChanged += (state) =>
        //{
        //    if (ActiveDevice == null) return;
        //    Main.ReloadPage();
        //};

        Main.PortComboBox.SelectionChanged += (sender, e) =>
        {
            OnSelectedDeviceChanged.Invoke();
        };
        Main.PortComboBox.DropDownOpened += (sender, e) =>
        {
            _ = Refresh();
        };
        Main.PortComboBox.DropDownClosed += (sender, e) =>
        {
            RemoveUnavailableDevices();
        };

        StartupRefresh();
    }

    private async void StartupRefresh()
    {
        await Refresh().ConfigureAwait(false);

        string deviceIdentifier = LocalAppDataStore.Instance.Get(LAST_CONNECTED_DEVICE_KEY, "");
        if (string.IsNullOrEmpty(deviceIdentifier)) return;

        int index = -1;
        int i = 0;

        foreach (Device device in Main.PortComboBox.Items)
        {
            if (device.ProductIdentifier == deviceIdentifier)
            {
                index = i;
                lastConnectedDevice = device;
                break;
            }
            i++;
        }

        if (index >= 0) Dispatcher.Invoke(() => Main.PortComboBox.SelectedIndex = index);
    }

    private void PreviewDeviceSelection(object sender, MouseButtonEventArgs e)
    {
        var container = (ComboBoxItem)sender;

        // original bound item
        if (container.DataContext is not Device item) return;

        if (refreshTask != null)
        {
            defferSelectedDevice = item;
            OnConnectedDevicesUpdated += PreviewDeviceSelectionDeffered;
            e.Handled = true;
        }
        Connect(item);
    }

    private void PreviewDeviceSelectionDeffered(List<Device> devices)
    {
        OnConnectedDevicesUpdated -= PreviewDeviceSelectionDeffered;
        if (defferSelectedDevice == null) return;

        Connect(defferSelectedDevice);
        defferSelectedDevice = null;
    }

    private void UpdatePendingCmdCount()
    {
        try
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                pendingCmdCountText.Text = $"Pending commands: {ProtocolService.PendingCmdCount}";
            }, System.Windows.Threading.DispatcherPriority.Normal);
        }
        catch { }
    }

    public override void OnApplicationQuit(System.ComponentModel.CancelEventArgs e)
    {
        if (ActiveDevice == null) return;
        e.Cancel = true;
        DisconnectAndQuit();
    }

    private void UpdatePortComboBox(List<Device> list)
    {
        var filteredDevices = new List<Device>(list);

        bool isShiftPressed = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        if (!isShiftPressed) filteredDevices.RemoveAll(device =>
        {
            if (device.VID == 0x045e && device.PID == 0x02FF) return false; // Xbox One Game Controller
            return device.VID != 0x0B05;
        });

        Main.PortComboBox.ItemsSource = filteredDevices;
        if (lastConnectedDevice != null)
        {
            int index = filteredDevices.FindIndex(device => device == lastConnectedDevice);
            Main.PortComboBox.SelectedIndex = index >= 0 ? index : -1;
        }
        else if (list.Count > 0) Main.PortComboBox.SelectedIndex = 0;
    }

    [GET("refresh")]
    public async Task Refresh()
    {
        if (refreshTask != null) return;

        refreshTask = Task.Run(FindConnectedDevices);
        DiscoveredDevices = await refreshTask;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Main.ConnectButton.IsEnabled = lastConnectedDevice?.IsAvailable ?? true;

            OnConnectedDevicesUpdated?.Invoke(DiscoveredDevices);
            refreshTask = null;
        });
    }

    [GET]
    public List<Device> ListDiscoveredDevices()
    {
        return DiscoveredDevices;
    }

    private List<Device> FindConnectedDevices()
    {
        try
        {
            return MergeDiscoveredInterface(PeripheralInterface.GetConnectedDevices(), DiscoveredDevices);
        }
        catch (Exception ex)
        {
            Debug.Log("Failed to find connected devices: " + ex.Message);
            return [];
        }
    }

    public static List<Device> MergeDiscoveredInterface(IEnumerable<IPeripheralDetail> discoveredInterfacers, List<Device> existingDevices = null)
    {
        //List<Device> devices = existingDevices ?? [];
        List<Device> devices = [];

        var interfaceList = discoveredInterfacers.ToList();

        // Disable devices that have no interfaces present in the newly discovered list
        foreach (var device in devices)
        {
            bool hasAnyInterface = device.interfaces.Any(i => interfaceList.Contains(i));
            device.IsAvailable = hasAnyInterface;
            device.interfaces.Clear();
        }

        foreach (var deviceInterface in interfaceList)
        {
            try
            {
                var dev = devices.Find(d => d.VID == deviceInterface.VID && d.PID == deviceInterface.PID);
                if (dev == null)
                {
                    dev = new Device(deviceInterface.VID, deviceInterface.PID, deviceInterface.Product);
                    devices.Add(dev);
                }
                dev.IsAvailable = true;
                dev.AddInterface(deviceInterface);
            }
            catch (Exception ex)
            {
                Debug.Log($"Failed to add device interface for vid:{deviceInterface.VID} pid:{deviceInterface.PID}\n\t{ex.Message}");
            }
        }
        return devices;
    }

    public void RemoveUnavailableDevices()
    {
        DiscoveredDevices.RemoveAll(device => device != lastConnectedDevice && device.IsAvailable == false);
    }

    [GET("connect/pid", true)]
    public bool SelectDropdownPID(ushort pid)
    {
        var index = Main.PortComboBox.Items.IndexOf(Main.PortComboBox.Items
            .OfType<Device>()
            .FirstOrDefault(d => d.PID == pid));
        if (index < 0) return false;
        Main.PortComboBox.SelectedIndex = index;
        Connect().Wait();
        return true;
    }

    /// <summary>
    /// Connect to the selected device from the dropdown list.
    /// </summary>
    /// <returns></returns>
    public async Task<bool> Connect()
    {
        await Disconnect();

        int idx = Main.PortComboBox.SelectedIndex;
        if (idx < 0 || idx >= DiscoveredDevices.Count) return false;

        var device = Main.PortComboBox.ItemsSource.Cast<Device>().ToArray()[idx];
        return Connect(device);
    }

    [POST(requireMainThread: true)]
    public async Task<bool> Connect(string productIdentifier)
    {
        await Disconnect();
        var device = DiscoveredDevices.FirstOrDefault(d => string.Compare(d.ProductIdentifier, productIdentifier) == 0);
        if (device == null) return false;
        return Connect(device);
    }

    [POST(requireMainThread: true)]
    public async Task<bool> Connect(ushort vid, ushort pid)
    {
        await Disconnect();
        var device = DiscoveredDevices.FirstOrDefault(d => d.VID == vid && d.PID == pid);
        if (device == null) return false;
        return Connect(device);
    }

    public bool Connect(ushort vid, ushort pid, string name, params IPeripheralDetail[] interfaces)
    {
        List<IPeripheralDetail> filteredInterfaces = new(interfaces);
        filteredInterfaces.RemoveAll(@interface => @interface == null);
        if (filteredInterfaces.Count == 0) return false;

        var newDevice = new Device(vid, pid, name);
        newDevice.interfaces.AddRange(interfaces);
        return Connect(newDevice);
    }

    public bool Connect(Device device)
    {
        ActiveDevice = device;
        lastConnectedDevice = device;

        OnActiveDeviceConnected?.Invoke();

        Main.ReloadPage();

        LocalAppDataStore.Instance.Set(LAST_CONNECTED_DEVICE_KEY, lastConnectedDevice.ProductIdentifier);
        return true;
    }

    private async void DisconnectAndQuit()
    {
        await Disconnect();
        Application.Current.Shutdown(0);
    }

    public async Task Disconnect()
    {
        if (ActiveDevice == null) return;
        OnActiveDeviceDisconnected?.Invoke();
        ActiveDevice = null;
    }

    private void ApplyComboxStyle()
    {
        var baseStyle = (Style)Application.Current.FindResource(typeof(ComboBoxItem));

        var style = new Style(typeof(ComboBoxItem), baseStyle);

        style.Setters.Add(new Setter(
            UIElement.IsEnabledProperty,
            new Binding("IsAvailable")));

        style.Setters.Add(new EventSetter(
            UIElement.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(PreviewDeviceSelection)));

        Main.PortComboBox.ItemContainerStyle = style;
    }

    public class Device(ushort vid, ushort pid, string name) : INotifyPropertyChanged
    {
        public ushort VID = vid;
        public ushort PID = pid;
        public string productName = name;
        public readonly List<IPeripheralDetail> interfaces = [];

        private bool isAvailable = true;
        public bool IsAvailable
        {
            get => isAvailable;
            set
            {
                if (isAvailable == value) return;
                isAvailable = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAvailable)));
            }
        }

        public override string ToString()
        {
            return $"{productName} {PID:X4}";
        }

        public string ProductIdentifier => $"{VID:X4}:{PID:X4}";

        public void AddInterface(IPeripheralDetail @interface)
        {
            if (interfaces.Contains(@interface)) return;
            interfaces.Add(@interface);
        }

        public IPeripheralDetail this[int index] => interfaces[index];

        public event PropertyChangedEventHandler PropertyChanged;
    }
}