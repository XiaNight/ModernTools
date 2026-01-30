using System.Globalization;
using System.IO;
using System.Text;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace Base.Services.Peripheral
{
    public sealed class BLEInterfaceDetail : PeripheraInterfaceDetail
    {
        public ulong SerialNumber { get; internal set; }
        public ulong Mac { get; internal set; }
        public uint VIDUInt { get; internal set; }
        public Guid bleServiceGuid { get; internal set; }

        public BLEInterfaceDetail(
            ushort pid = 0,
            uint vid = 0,
            string product = "",
            string manufacturer = "",
            string id = "",
            ushort versionNumber = 0,
            ushort usage = 0,
            ushort usagePage = 0)
            : base(pid, (ushort)vid, product, manufacturer, id, versionNumber, usage, usagePage)
        {
            VIDUInt = vid;
        }

        public override PeripheralInterface Connect(bool useAsyncRead = false)
        {
            var ble_task = Task.Run(() =>
            {
                return new BLEInterface(this, useAsyncRead);
            });
            ble_task.Wait(TimeSpan.FromSeconds(1));
            return ble_task.Result;
        }
    }

    public sealed class BLEInterface : PeripheralInterface
    {
        private const uint BLEWriteCharacUuid = 0xFFF1;
        private const uint BLEReadCharacUuid = 0xFFF2;
        private const uint BLENotificationCharacUuid = 0xFFF4;

        public BLEInterfaceDetail BleInfo => (BLEInterfaceDetail)ProductInfo;

        private BluetoothLEDevice bleDevice;
        private GattCharacteristic characteristicWrite;
        private GattCharacteristic characteristicRead;
        private GattCharacteristic characteristicNotify;
        private bool initialized;

        public BLEInterface(IPeripheralDetail interfaceDetail, bool useAsyncRead = false)
            : base(interfaceDetail, useAsyncRead)
        {
            Initialize().GetAwaiter().GetResult();
        }

        private async Task EnsureInitializedAsync()
        {
            if (initialized) return;
            await Initialize();
        }

        private async Task Initialize()
        {
            if (initialized) return;
            bleDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(BleInfo.Mac);
            //if (_bleDevice?.ConnectionStatus != BluetoothConnectionStatus.Connected)
            //    throw new IOException("BLE device not connected.");

            var servicesResult = await bleDevice.GetGattServicesForUuidAsync(BleInfo.bleServiceGuid);

            //var servicesResult = await bleDevice.GetGattServicesAsync();
            if (servicesResult.Status != GattCommunicationStatus.Success)
                throw new IOException("Failed to enumerate GATT services.");

            ushort targetUsagePage = BleInfo.UsagePage;
            if (targetUsagePage == 0) targetUsagePage = 0xFFF0; // Default to FFF0 if not set
            GattDeviceService service = null;
            foreach (var s in servicesResult.Services)
            { 
                uint? sid = BluetoothUuidHelper.TryGetShortId(s.Uuid);
                if (sid == null)
                {
                    string toString = s.Uuid.ToString();
                    if (toString == "f364fff0-00b0-4240-ba50-05ca45bf8abc")
                        sid = 0xFFF0;
                }
                if (sid == targetUsagePage)
                {
                    service = s;
                    break;
                }
            }
            if (service is null) throw new IOException($"Target {targetUsagePage} service not found.");

            //BluetoothUuidHelper.FromShortId(0x)
            //service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.GapPeripheralPreferredConnectionParameters);

            var charsResult = await service.GetCharacteristicsAsync();
            if (charsResult.Status != GattCommunicationStatus.Success)
                throw new IOException("Failed to enumerate characteristics.");

            foreach (var c in charsResult.Characteristics)
            {
                uint? sid = BluetoothUuidHelper.TryGetShortId(c.Uuid);
                if (sid == null)
                {
                    switch (c.Uuid.ToString())
                    {
                        case "f364fff1-00b0-4240-ba50-05ca45bf8abc":
                            sid = BLEWriteCharacUuid;
                            break;
                        case "f364fff4-00b0-4240-ba50-05ca45bf8abc":
                            sid = BLENotificationCharacUuid;
                            break;
                    }
                }
                switch (sid)
                {
                    case BLEWriteCharacUuid:
                        characteristicWrite = c;
                        break;
                    case BLEReadCharacUuid:
                        characteristicRead = c;
                        break;
                    case BLENotificationCharacUuid:
                        characteristicNotify = c;
                        break;
                    case 0x2A04:
                        characteristicRead = c;
                        break;
                    default:
                        continue;
                }
                if (sid == BLEWriteCharacUuid) characteristicWrite = c;
                else if (sid == BLEReadCharacUuid) characteristicRead = c;
            }
            if (characteristicWrite is null && characteristicRead is null && characteristicNotify is null)
                throw new IOException("Required FFF1/FFF2 characteristics not found."); 

            if (UseAsyncReads)
            {
                if(characteristicRead != null)
                {
                    characteristicRead.ValueChanged += OnValueChanged;
                    await characteristicRead.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.Notify);
                }
                if(characteristicNotify != null)
                {
                    characteristicNotify.ValueChanged += OnValueChanged;
                }
            }

            initialized = true;
            IsDeviceConnected = true;
        }

        private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var data = new byte[args.CharacteristicValue.Length];
            DataReader.FromBuffer(args.CharacteristicValue).ReadBytes(data);
            InvokeDataReceived(data);
        }

        public static List<BLEInterfaceDetail> GetConnectedDevicesTask()
        {
            var t = Task.Run(GetConnectedDevices);
            t.Wait(TimeSpan.FromSeconds(10));
            return t.Result;
        }

        public static new async Task<List<BLEInterfaceDetail>> GetConnectedDevices()
        {
            //var selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(false);
            var selector = BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
            var devs = await DeviceInformation.FindAllAsync(selector);

            var list = new List<BLEInterfaceDetail>();
            foreach (var d in devs)
            {
                var item = await CreateInterfaceDetailFromDeviceId(d);
                if (item != null) list.AddRange(item);
            }
            return list;
        }

        public static BluetoothLEAdvertisementWatcher StartWatcher(TypedEventHandler<BluetoothLEAdvertisementWatcher, BluetoothLEAdvertisementReceivedEventArgs> onReceived)
        {
            var watcher = new BluetoothLEAdvertisementWatcher
            {
                ScanningMode = BluetoothLEScanningMode.Active,
                AllowExtendedAdvertisements = true,
            };
            watcher.Received += onReceived;
            watcher.Start();
            return watcher;
        }
         
        public static async Task<List<BLEInterfaceDetail>> CreateInterfaceDetailFromAddress(ulong address)
        {
            try
            {
                using var ble = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
                //if (ble?.ConnectionStatus != BluetoothConnectionStatus.Connected) return null;
                var servicesResult = await ble.GetGattServicesAsync();
                if (servicesResult.Status != GattCommunicationStatus.Success) return null;
                return await ExtractInterfaceDetails(ble, servicesResult.Services);
            }
            catch
            {
                return null;
            }
        }

        private static async Task<List<BLEInterfaceDetail>> CreateInterfaceDetailFromDeviceId(DeviceInformation device)
        {
            try
            {
                using var bleDevice = await BluetoothLEDevice.FromIdAsync(device.Id);
                if (bleDevice?.ConnectionStatus != BluetoothConnectionStatus.Connected) return null;

                var servicesResult = await bleDevice.GetGattServicesAsync();
                if (servicesResult.Status != GattCommunicationStatus.Success) return null;

                return await ExtractInterfaceDetails(bleDevice, servicesResult.Services);
            }
            catch
            {
                return null;
            }
        }

        private static async Task<List<BLEInterfaceDetail>> ExtractInterfaceDetails(BluetoothLEDevice dev, IReadOnlyList<GattDeviceService> services)
        {
            string manufacturer = string.Empty;
            ulong serialNumber = 0;

            var deviceInformationService = services.FirstOrDefault(s => s.Uuid == GattServiceUuids.DeviceInformation);
            PnpInfo pnpInfo = null;
            if (deviceInformationService != null)
            {
                await ProcessCharacteristic(deviceInformationService, GattCharacteristicUuids.ManufacturerNameString, v => manufacturer = Encoding.UTF8.GetString(v));
                await ProcessCharacteristic(deviceInformationService, GattCharacteristicUuids.SerialNumberString, v =>
                {
                    var hex = Encoding.UTF8.GetString(v);
                    if (!ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out serialNumber))
                        serialNumber = 0;
                });
                await ProcessCharacteristic(deviceInformationService, GattCharacteristicUuids.PnpId, v => TryParsePnpId(v, out pnpInfo));
            }

            List<BLEInterfaceDetail> stdServices = new();
            foreach(var service in services)
            {
                ushort? usagePage = (ushort?)BluetoothUuidHelper.TryGetShortId(service.Uuid);
                if (usagePage == null) continue;

                stdServices.Add(new BLEInterfaceDetail(
                    pid: pnpInfo?.ProductId ?? 0,
                    vid: pnpInfo?.VendorId ?? 0,
                    product: dev.Name ?? string.Empty,
                    manufacturer: manufacturer ?? string.Empty,
                    id: dev.DeviceId,
                    usagePage: (ushort)usagePage)
                    {
                        Mac = dev.BluetoothAddress,
                        SerialNumber = serialNumber,
                        bleServiceGuid = service.Uuid
                    }
                );
            }

            return stdServices;
        }

        private static async Task ProcessCharacteristic(GattDeviceService service, Guid uuid, Action<byte[]> onValue)
        {
            var result = await service.GetCharacteristicsForUuidAsync(uuid);
            if (result.Status != GattCommunicationStatus.Success || result.Characteristics.Count == 0) return;
            var characteristic = result.Characteristics[0];
            var read = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
            if (read.Status != GattCommunicationStatus.Success) return;
            var data = new byte[read.Value.Length];
            DataReader.FromBuffer(read.Value).ReadBytes(data);
            onValue?.Invoke(data);
        }

        private enum VendorIdSource : byte
        {
            BluetoothSig = 0x01, // 16-bit Company Identifier (Bluetooth SIG list)
            UsbIf = 0x02  // 16-bit USB Vendor ID (USB-IF list)
        }

        private sealed class PnpInfo
        {
            public VendorIdSource Source { get; init; }
            public ushort VendorId { get; init; }   // Company ID if Source=BluetoothSig; USB VID if Source=UsbIf
            public ushort ProductId { get; init; }  // USB-style PID (if Source=UsbIf); device-defined otherwise
            public ushort ProductVersion { get; init; }
        }

        private static bool TryParsePnpId(byte[] v, out PnpInfo info)
        {
            info = null;
            if (v == null || v.Length < 7) return false;

            var src = (VendorIdSource)v[0];
            ushort vid = (ushort)(v[1] | (v[2] << 8));      // little-endian
            ushort pid = (ushort)(v[3] | (v[4] << 8));
            ushort ver = (ushort)(v[5] | (v[6] << 8));

            // Only 0x01 and 0x02 are valid per spec
            if (src != VendorIdSource.BluetoothSig && src != VendorIdSource.UsbIf) return false;

            info = new PnpInfo { Source = src, VendorId = vid, ProductId = pid, ProductVersion = ver };
            return true;
        }

        public override async Task<bool> WriteAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await EnsureInitializedAsync();

            if (characteristicWrite is null) return false;

            using var writer = new DataWriter();
            writer.WriteBytes(data);
            var buf = writer.DetachBuffer();

            var status = await characteristicWrite.WriteValueAsync(buf, GattWriteOption.WriteWithoutResponse).AsTask(cancellationToken);
            return status == GattCommunicationStatus.Success;
        }

        public override async Task<byte[]> ReadAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            await EnsureInitializedAsync();

            if (characteristicRead is null) return [];

            var result = await characteristicRead.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken);
            if (result.Status != GattCommunicationStatus.Success || result.Value is null) return [];

            var data = new byte[result.Value.Length];
            DataReader.FromBuffer(result.Value).ReadBytes(data);
            return data;
        }

        protected override void CloseDevice()
        {
            try
            {
                if (characteristicRead != null)
                {
                    characteristicRead.ValueChanged -= OnValueChanged;
                    _ = characteristicRead.WriteClientCharacteristicConfigurationDescriptorAsync(
                        GattClientCharacteristicConfigurationDescriptorValue.None);
                }
            }
            catch { /* ignore */ }
            finally
            {
                characteristicWrite = null;
                characteristicRead = null;
                bleDevice?.Dispose();
                bleDevice = null;
                initialized = false;
                IsDeviceConnected = false;
            }
        }
    }
}
