using System.Windows;

namespace Base.Services
{
    using Base.Services.APIService;
    using Core;
    using Peripheral;
    using System.Windows.Controls;
    using System.Windows.Input;
    using Windows.Foundation.Metadata;

    public class DeviceSelection : WpfBehaviourSingleton<DeviceSelection>
    {
        public event Action<List<Device>> OnConnectedDevicesUpdated;
        public UIEvent OnSelectedDeviceChanged = new();
        public UIEvent OnActiveDeviceConnected = new();
        public UIEvent OnActiveDeviceDisconnected = new();

        public List<Device> ConnectedDevices => connectedDevices;

        private Task<List<Device>> refreshTask;
        private List<Device> connectedDevices = new();
        private TextBlock pendingCmdCountText;
        private Device lastConnectedDevice;

        public Device ActiveDevice { get; private set; }

        /// <summary>
        /// HID-compliant Vendor Defined Device
        /// </summary>
        [Deprecated("Use ActiveDevice instead.", DeprecationType.Deprecate, 0x0107)]
        public PeripheralInterface ActiveInterface { get => activeInterface; }
        [Deprecated("Use ActiveDevice instead.", DeprecationType.Deprecate, 0x0107)]
        public UIEvent OnActiveInterfaceConnected = new();
        [Deprecated("Use ActiveDevice instead.", DeprecationType.Deprecate, 0x0107)]
        public UIEvent OnActiveInterfaceDisconnected = new();
        [Deprecated("Use ActiveDevice instead.", DeprecationType.Deprecate, 0x0107)]
        private PeripheralInterface activeInterface;

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

            Main.RefreshButton.Click += (_, _) => { _ = Refresh(); };
            Main.ConnectButton.Click += (_, _) => { _ = Connect(); };
            Main.DisconnectButton.Click += (_, _) => { _ = Disconnect(); };

            ProtocalService.OnCmdSent += UpdatePendingCmdCount;
            ProtocalService.OnCmdQueued += UpdatePendingCmdCount;

            OnActiveDeviceConnected += () =>
            {
                Main.PortComboBox.Text = ActiveDevice.ToString();
                Main.PortComboBox.IsEnabled = false;
                Main.RefreshButton.IsEnabled = false;
                Main.ConnectButton.Visibility = Visibility.Hidden;
                Main.DisconnectButton.Visibility = Visibility.Visible;
                Main.MainFooter.DeviceName.Text = $"Connected: {ActiveDevice.productName}";
                Main.MainFooter.DeviceVersion.Text = $"FW: ----";
                Main.MainFooter.DeviceBattery.Text = "Battery: ----";
            };
            OnActiveDeviceDisconnected += () =>
            {
                Main.PortComboBox.IsEnabled = true;
                Main.RefreshButton.IsEnabled = true;
                Main.ConnectButton.Visibility = Visibility.Visible;
                Main.DisconnectButton.Visibility = Visibility.Hidden;
                Main.MainFooter.DeviceName.Text = "Disconnected";
                Main.MainFooter.DeviceVersion.Text = $"FW: ----";
                Main.MainFooter.DeviceBattery.Text = "Battery: ----";
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

            BatteryIndicator.OnBatteryLevelChanged += (levels) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    string batteryText = "Battery:";
                    foreach (var level in levels)
                    {
                        //- level byte is 0 ~ 100 (0x00 ~ 0x64), no need to convert
                        batteryText += $" {level:F1}%";
                    }
                    //Main.FooterDeviceBattery.Text = batteryText;
                });
            };
            OnActiveInterfaceConnected += BatteryIndicator.GetBatteryLevel;

            _ = Refresh();
        }

        private void UpdatePendingCmdCount(ProtocalService.CmdData cmd)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                pendingCmdCountText.Text = $"Pending commands: {ProtocalService.PendingCmdCount}";
            });
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
                if (device.VID != 0x0B05) return true;
                return false;
            });

            Main.PortComboBox.ItemsSource = filteredDevices;
            if (lastConnectedDevice != null)
            {
                int index = filteredDevices.FindIndex(device => device.interfaces.Any(@interface => @interface.Product == lastConnectedDevice.productName));
                if (index >= 0) Main.PortComboBox.SelectedIndex = index;
                else Main.PortComboBox.SelectedIndex = -1;
            }
            else if (list.Count > 0) Main.PortComboBox.SelectedIndex = 0;
        }

        [GET("refresh")]
        public async Task Refresh()
        {
            if (refreshTask != null) return;
            await Application.Current.Dispatcher.Invoke(async () =>
            {
                Main.RefreshButton.IsEnabled = false;
            });

            refreshTask = Task.Run(FindConnectedDevices);
            connectedDevices = await refreshTask;

            await Application.Current.Dispatcher.Invoke(async () =>
            {
                Main.RefreshButton.IsEnabled = true;

                OnConnectedDevicesUpdated?.Invoke(connectedDevices);
                refreshTask = null;
            });
        }

        private List<Device> FindConnectedDevices()
        {
            try
            {
                return ConstructDevice(PeripheralInterface.GetConnectedDevices());
            }
            catch (Exception ex)
            {
                Debug.Log("Failed to find connected devices: " + ex.Message);
                return [];
            }
        }

        public static List<Device> ConstructDevice(IEnumerable<IPeripheralDetail> interfaces)
        {
            List<Device> devices = [];
            foreach (var device in interfaces)
            {
                try
                {
                    var dev = devices.Find(d => d.VID == device.VID && d.PID == device.PID);
                    if (dev == null)
                    {
                        dev = new Device(device.VID, device.PID, device.Product);
                        devices.Add(dev);
                    }
                    dev.AddInterface(device);
                }
                catch (Exception ex)
                {
                    Debug.Log($"Failed to add device interface for vid:{device.VID} pid:{device.PID}\n\t{ex.Message}");
                }
            }
            return devices;
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
        public async Task Connect()
        {
            await Disconnect();

            int idx = Main.PortComboBox.SelectedIndex;
            if (idx < 0 || idx >= connectedDevices.Count) return;

            var device = Main.PortComboBox.ItemsSource.Cast<Device>().ToArray()[idx];
            Connect(device);
        }

        public void Connect(ushort vid, ushort pid, string name, params IPeripheralDetail[] interfaces)
        {
            List<IPeripheralDetail> filteredInterfaces = new(interfaces);
            filteredInterfaces.RemoveAll(@interface => @interface == null);
            if (filteredInterfaces.Count == 0) return;

            var newDevice = new Device(vid, pid, name);
            newDevice.interfaces.AddRange(interfaces);
            Connect(newDevice);
        }

        public void Connect(Device device)
        {
            ActiveDevice = device;
            lastConnectedDevice = device;

            OnActiveDeviceConnected?.Invoke();

            Main.ReloadPage();
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
        }

        public class Device(ushort vid, ushort pid, string name)
        {
            public ushort VID = vid;
            public ushort PID = pid;
            public string productName = name;
            public readonly List<IPeripheralDetail> interfaces = [];

            public override string ToString()
            {
                return $"{productName} {PID:X4}";
            }

            public void AddInterface(IPeripheralDetail @interface)
            {
                if (interfaces.Contains(@interface)) return;
                interfaces.Add(@interface);
            }
        }
    }
}