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
            if (device.MatchesIdentifier(deviceIdentifier))
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
            // BLE devices exposing the ASUS vendor GATT service stay visible even
            // when they report no (or a Bluetooth SIG) vendor id.
            if (device.interfaces.Any(i => i is BLEInterfaceDetail { IsVendorService: true })) return false;
            return device.VID != 0x0B05;
        });

        Main.PortComboBox.ItemsSource = filteredDevices;
        if (lastConnectedDevice != null)
        {
            // Each refresh produces fresh Device instances, so match by identity
            // (VID/PID/transport) instead of by reference and re-anchor to the
            // fresh instance so the selection survives dropdown refreshes.
            int index = filteredDevices.FindIndex(device => device.ProductIdentifier == lastConnectedDevice.ProductIdentifier);
            if (index >= 0) lastConnectedDevice = filteredDevices[index];
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
                // A single physical device can surface through more than one
                // enumeration path at once. An HID-over-GATT gamepad, for example,
                // is returned by the BLE scanner (no Container ID) *and* by the HID
                // stack (with a Container ID); both must collapse into one entry.
                Device dev = devices.Find(d => IsSameDeviceEntry(d, deviceInterface));

                if (dev == null)
                {
                    dev = new Device(deviceInterface.VID, deviceInterface.PID,
                                     deviceInterface.Product, deviceInterface.ContainerID,
                                     deviceInterface.Transport);
                    devices.Add(dev);
                }
                else if (string.IsNullOrEmpty(dev.ContainerID) && !string.IsNullOrEmpty(deviceInterface.ContainerID))
                {
                    // Adopt a Container ID contributed by a later interface so the
                    // entry's identity is stable regardless of enumeration order.
                    dev.ContainerID = deviceInterface.ContainerID;
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

    /// <summary>
    /// Decides whether a discovered interface belongs to an existing device entry.
    /// <para>
    /// When both sides carry a Windows Container ID, they must match exactly — this
    /// keeps two identical devices on different ports as separate entries. When
    /// either side lacks a Container ID (e.g. a BLE/BT interface, or the same
    /// physical device seen once via the HID stack and once via the BLE scanner),
    /// fall back to VID+PID+transport. That collapses the duplicate enumerations of
    /// one physical device while still keeping the same product reached over
    /// different transports (USB vs BLE) as distinct, clearly labelled entries.
    /// </para>
    /// </summary>
    private static bool IsSameDeviceEntry(Device device, IPeripheralDetail candidate)
    {
        if (!string.IsNullOrEmpty(device.ContainerID) && !string.IsNullOrEmpty(candidate.ContainerID))
            return device.ContainerID == candidate.ContainerID;

        return device.VID == candidate.VID
            && device.PID == candidate.PID
            && device.Transport == candidate.Transport;
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
        var items = Main.PortComboBox.ItemsSource?.Cast<Device>().ToArray() ?? [];
        if (idx < 0 || idx >= items.Length) return false;

        return Connect(items[idx]);
    }

    [POST(requireMainThread: true)]
    public async Task<bool> Connect(string productIdentifier)
    {
        await Disconnect();
        var device = DiscoveredDevices.FirstOrDefault(d => d.MatchesIdentifier(productIdentifier));
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

        var newDevice = new Device(vid, pid, name, filteredInterfaces[0].ContainerID, filteredInterfaces[0].Transport);
        newDevice.interfaces.AddRange(filteredInterfaces);
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

    public class Device(ushort vid, ushort pid, string name, string containerId = "", PeripheralTransport transport = PeripheralTransport.UsbHid) : INotifyPropertyChanged
    {
        public ushort VID = vid;
        public ushort PID = pid;
        public string productName = name;
        public string ContainerID = containerId;
        public readonly List<IPeripheralDetail> interfaces = [];

        /// <summary>Transport all interfaces of this entry are reached over.</summary>
        public PeripheralTransport Transport { get; } = transport;

        /// <summary>Connection type shown in the device selection dropdown.</summary>
        public string TransportLabel => Transport.GetLabel();

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
            return $"{productName} {PID:X4} [{TransportLabel}]";
        }

        // Encodes the transport, plus the Container ID when available so two identical
        // physical devices on different ports are distinct entries. Devices without a
        // Container ID (e.g. BLE/BT) fall back to VID:PID:transport.
        public string ProductIdentifier => string.IsNullOrEmpty(ContainerID)
            ? $"{VID:X4}:{PID:X4}:{Transport.GetKey()}"
            : $"{VID:X4}:{PID:X4}:{Transport.GetKey()}:{ContainerID}";

        /// <summary>
        /// Matches the current identifier format plus the legacy formats persisted by
        /// earlier versions: "VID:PID" (no transport component) and
        /// "VID:PID:transport" (no Container ID component).
        /// </summary>
        public bool MatchesIdentifier(string identifier)
     