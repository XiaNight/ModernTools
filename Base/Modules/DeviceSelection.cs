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

    private const int REFRESH_INTERVAL_MS = 200;
    private Timer refreshSchedulerTimer;
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
        OnConnectedDevicesUpdated += (_) => RemoveUnavailableDevices();
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
            Main.MainFooter.DeviceName.Text = $"{ActiveDevice.ProductName}";
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

        Main.WindowMessageReceived += OnWindowMessageReceived;

        StartupRefresh();
    }

    private async void StartupRefresh()
    {
        await Refresh().ConfigureAwait(true);

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

        if (index < 0) return; // last device not present — fall back to no selection (normal startup)

        bool autoConnect = StartupSettings.Instance.AutoConnectLastDevice;
        Device target = lastConnectedDevice;

        Main.PortComboBox.SelectedIndex = index;

        // Reconnect the last-used device only when the user opted in and it is actually available.
        if (autoConnect && target != null && target.IsAvailable)
            Connect(target);
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

    #region Refresh

    private void UpdatePortComboBox(List<Device> list)
    {
        var filteredDevices = new List<Device>(list);

        bool isShiftPressed = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        if (!isShiftPressed) filteredDevices.RemoveAll(device =>
        {
            if (device.VID == 0x045e && device.PID == 0x02FF) return false; // Xbox One Game Controller
            // BLE devices exposing the ASUS vendor GATT service stay visible even
            // when they report no (or a Bluetooth SIG) vendor id.
            return device.interfaces.Any(i => i is BLEInterfaceDetail { IsVendorService: true }) ? false : device.VID != 0x0B05;
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

    public void ScheduleRefresh()
    {
        if (refreshSchedulerTimer != null)
        {
            refreshSchedulerTimer.Change(REFRESH_INTERVAL_MS, Timeout.Infinite);
            return;
        }
        refreshSchedulerTimer = new Timer(async _ =>
        {
            await Refresh();
            refreshSchedulerTimer.Dispose();
            refreshSchedulerTimer = null;
        }, null, REFRESH_INTERVAL_MS, Timeout.Infinite);
    }

    [GET("refresh",
        Summary = "Rescan for connected devices.",
        Description = "Rescans the system for connected peripherals and refreshes the device dropdown. Takes no " +
            "parameters. Discovered devices can then be read via ListDiscoveredDevices and connected to with " +
            "one of the connect endpoints.")]
    public async Task Refresh()
    {
        if (refreshTask != null) return;

        refreshTask = Task.Run(FindConnectedDevices);
        DiscoveredDevices = await refreshTask;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            OnConnectedDevicesUpdated?.Invoke(DiscoveredDevices);
            Main.ConnectButton.IsEnabled = lastConnectedDevice?.IsAvailable ?? true;
            refreshTask = null;
        });
    }

    [GET(
        Summary = "List devices found by the last scan.",
        Description = "Returns the list of devices found by the most recent scan. Takes no parameters; call " +
            "refresh first to populate or update it. Each entry describes one peripheral (VID, PID, product " +
            "name, transport and its interfaces) and can be used to select a device to connect to.")]
    public List<Device> ListDiscoveredDevices()
    {
        return DiscoveredDevices;
    }

    private List<Device> FindConnectedDevices()
    {
        try
        {
            return UpdateDeviceInterfaces(PeripheralInterface.GetConnectedDevices(), DiscoveredDevices);
        }
        catch (Exception ex)
        {
            Debug.Log("Failed to find connected devices: " + ex.Message);
            return [];
        }
    }

    /// <summary>
    /// Updates the device list using the currently discovered peripheral interfaces.
    /// Removes unreachable interfaces, marks unavailable devices, merges interfaces that
    /// belong to the same physical device, and creates new device entries as needed.
    /// </summary>
    /// <param name="interfaces">The currently discovered peripheral interfaces.</param>
    /// <param name="devices">
    /// The existing device list to update.
    /// </param>
    /// <returns>The updated device list.</returns>
    public static List<Device> UpdateDeviceInterfaces(IEnumerable<IPeripheralDetail> interfaces, List<Device> devices = null)
    {
        devices ??= [];

        // Disable devices that have no interfaces present in the newly discovered list
        foreach (Device device in devices)
        {
            device.interfaces.RemoveAll(i => !interfaces.Contains(i));
            device.IsAvailable = device.interfaces.Count > 0;
        }

        foreach (IPeripheralDetail deviceInterface in interfaces)
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
        return !string.IsNullOrEmpty(device.ContainerID) && !string.IsNullOrEmpty(candidate.ContainerID)
            ? device.ContainerID == candidate.ContainerID
            : device.VID == candidate.VID
            && device.PID == candidate.PID
            && device.Transport == candidate.Transport;
    }

    public void RemoveUnavailableDevices()
    {
        List<Device> unavailableDevices = DiscoveredDevices.FindAll(device => device.IsAvailable == false);

        foreach (var device in unavailableDevices)
        {
            RemoveDevice(device);
        }
    }

    private void RemoveDevice(Device device)
    {
        if (device.ProductIdentifier == ActiveDevice?.ProductIdentifier)
        {
            Disconnect().Wait();
        }
        DiscoveredDevices.Remove(device);
        device?.Dispose();
    }

    #endregion

    [GET("connect/pid", true,
        Summary = "Connect to a device by USB Product ID.",
        Description = "Selects the device with the given USB Product ID in the dropdown and connects to it. " +
            "Query: ?pid=<value> (decimal or hex, e.g. 6100 or 0x17D4). Returns true on success, or false if " +
            "no device with that PID is present in the dropdown.")]
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

    #region Connect / Disconnect

    /// <summary>
    /// Connect to the selected device from the dropdown list.
    /// </summary>
    /// <returns></returns>
    public async Task<bool> Connect()
    {
        await Disconnect();

        int idx = Main.PortComboBox.SelectedIndex;
        var items = Main.PortComboBox.ItemsSource?.Cast<Device>().ToArray() ?? [];
        return idx < 0 || idx >= items.Length ? false : Connect(items[idx]);
    }

    [POST(requireMainThread: true,
        Summary = "Connect to a device by product identifier.",
        Description = "Connects to a discovered device identified by its product-identifier string (as reported " +
            "on each entry from ListDiscoveredDevices). Body: { \"productIdentifier\": string }. Any currently " +
            "connected device is disconnected first. Returns true on success, false if no discovered device " +
            "matches the identifier.")]
    public async Task<ApiResponse> ConnectIdentifier(string productIdentifier)
    {
        await Disconnect();

        var device = DiscoveredDevices.FirstOrDefault(d => d.MatchesIdentifier(productIdentifier));
        if (device == null)
        {
            return new ApiResponse()
            {
                Status = 404,
                Data = $"No device found with identifier: {productIdentifier}"
            };
        }

        return Connect(device)
        ? new ApiResponse()
        {
            Status = 200,
            Data = $"Connected to device: {device.ProductName} VID: {device.VID:X4} PID: {device.PID:X4}"
        }
        : new ApiResponse()
        {
            Status = 500,
            Data = $"Failed to connect to device: {device.ProductName} VID: {device.VID:X4} PID: {device.PID:X4}"
        };
    }

    [POST(requireMainThread: true,
    Summary = "Connect to a device by USB Vendor ID and Product ID.",
    Description = "Connects to a discovered device by its USB Vendor ID and Product ID. " +
        "Body: { \"vid\": integer, \"pid\": integer } (decimal or hex). Any currently connected device is " +
        "disconnected first. Returns an ApiResponse describing the connection result.")]
    public async Task<ApiResponse> ConnectVidPid(ushort vid, ushort pid)
    {
        await Disconnect();

        var device = DiscoveredDevices.FirstOrDefault(d => d.VID == vid && d.PID == pid);
        if (device == null)
        {
            return new ApiResponse()
            {
                Status = 404,
                Data = $"No device found with VID: {vid:X4} PID: {pid:X4}"
            };
        }

        return Connect(device)
        ? new ApiResponse()
        {
            Status = 200,
            Data = $"Connected to device: {device.ProductName} VID: {device.VID:X4} PID: {device.PID:X4}"
        }
        : new ApiResponse()
        {
            Status = 500,
            Data = $"Failed to connect to device: {device.ProductName} VID: {device.VID:X4} PID: {device.PID:X4}"
        };
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

    [POST]
    public async Task Disconnect()
    {
        if (ActiveDevice == null) return;
        OnActiveDeviceDisconnected?.Invoke();
        ActiveDevice = null;
    }

    #endregion

    #region Physical device event

    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVNODES_CHANGED = 0x0007;

    // Windows broadcasts WM_DEVICECHANGE / DBT_DEVNODES_CHANGED to every top-level
    // window whenever the device tree changes (any add or remove), so there is no
    // need to register for notifications. Re-scan on that signal; the diff in
    // DetectPhysicalDisconnects works out what was unplugged.
    private void OnWindowMessageReceived(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, bool handled)
    {
        if (msg != WM_DEVICECHANGE) return;
        checked
        {
            if ((int)wParam != DBT_DEVNODES_CHANGED) return;
        }
        ScheduleRefresh();
    }

    #endregion

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

    public class Device(ushort vid, ushort pid, string name, string containerId = "", PeripheralTransport transport = PeripheralTransport.UsbHid) : INotifyPropertyChanged, IDisposable
    {
        public ushort VID { get; private set; } = vid;
        public ushort PID { get; private set; } = pid;
        public string ProductName { get; private set; } = name;
        public string ContainerID { get; set; } = containerId;
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
            return $"{ProductName} {PID:X4} [{TransportLabel}]";
        }

        public string ProductIdentifier => string.IsNullOrEmpty(ContainerID)
            ? $"{VID:X4}:{PID:X4}:{Transport.GetKey()}"
            : $"{VID:X4}:{PID:X4}:{Transport.GetKey()}:{ContainerID}";

        /// <summary>
        /// Matches the current identifier format plus the legacy formats persisted by
        /// earlier versions: "VID:PID" (no transport component) and
        /// "VID:PID:transport" (no Container ID component).
        /// </summary>
        public bool MatchesIdentifier(string identifier)
            => identifier == ProductIdentifier
            || identifier == $"{VID:X4}:{PID:X4}"
            || identifier == $"{VID:X4}:{PID:X4}:{Transport.GetKey()}";

        public void AddInterface(IPeripheralDetail @interface)
        {
            if (interfaces.Contains(@interface)) return;
            interfaces.Add(@interface);
        }

        public bool FindInterface(ushort usage, ushort usagepage, out PeripheralInterfaceDetail interfaceDetail)
        {
            interfaceDetail = null;
            foreach (var @interface in interfaces)
            {
                if (@interface.Usage == usage && @interface.UsagePage == usagepage)
                {
                    interfaceDetail = @interface as PeripheralInterfaceDetail;
                    return true;
                }
            }
            return false;
        }

        public void Dispose()
        {
            foreach (var @interface in interfaces)
            {
                @interface.Dispose();
            }
            interfaces.Clear();
            GC.SuppressFinalize(this);
        }

        public IPeripheralDetail this[int index] => interfaces[index];

        public event PropertyChangedEventHandler PropertyChanged;
    }
}