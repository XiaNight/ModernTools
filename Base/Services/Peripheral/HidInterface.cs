using Base.Services.Peripheral.Native;
using Microsoft.Win32.SafeHandles;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Base.Services.Peripheral
{
    public sealed class HidInterfaceDetail : PeripheraInterfaceDetail
    {
        public int InputReportByteLength { get; set; }
        public int OutputReportByteLength { get; set; }
        public HidInterfaceDetail(
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
            
        }

        public override PeripheralInterface Connect(bool useAsyncRead = false)
        {
            var connectionTask = Task.Run(() =>
            {
                return new HidInterface(this, useAsyncRead);
            });
            connectionTask.Wait(TimeSpan.FromSeconds(1));
            return connectionTask.Result;
        }
    }

    public sealed class HidInterface : PeripheralInterface
    {
        const string KeyVendorId = "System.Devices.Hid.VendorId";
        const string KeyProductId = "System.Devices.Hid.ProductId";
        const string KeyVersionNumber = "System.Devices.Hid.VersionNumber";
        const string KeyUsage = "System.Devices.Hid.UsageId";
        const string KeyUsagePage = "System.Devices.Hid.UsagePage";
        const string KeyManufacturer = "System.Devices.Manufacturer";
        const string KeyProductName = "System.ItemNameDisplay";


        private const int DIGCF_DEVICEINTERFACE = 0x10;
        private const int DIGCF_PRESENT = 0x2;
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        private const int DEFAULT_TIMEOUT_MS = 1000;


        private SafeFileHandle _handleRead;
        private SafeFileHandle _handleWrite;
        private FileStream _fsRead;
        private FileStream _fsWrite;
        private HidDescriptorContext _descriptorContext;
        private ushort _lcid;
        private HidNative.HIDP_CAPS _cap;
        private readonly SemaphoreSlim _readSem = new(1, 1);
        private readonly SemaphoreSlim _writeSem = new(1, 1);
        private CancellationTokenSource _readCts;
        private Task _readTask;
        private bool _disposed;

        private HidDevice device;
        private HidInterfaceDetail HidInfo => (ProductInfo as HidInterfaceDetail)!;

        private readonly ConcurrentQueue<byte[]> _rxQueue = new();
        private readonly SemaphoreSlim _rxSignal = new(0, int.MaxValue);
        private readonly object _openLock = new();

        public HidInterface(IPeripheralDetail info, bool useAsyncRead = false)
            : base(info, useAsyncRead)
        {
            CancellationTokenSource cts = new();
            cts.CancelAfter(TimeSpan.FromSeconds(2));
            InitDevice(info.ID, useAsyncRead);
        }

        public static new async Task<List<HidInterfaceDetail>> GetConnectedDevices()
        {
            var list = new List<HidInterfaceDetail>();

            // Get all connected HID devices
            list.AddRange(await GetConnectedDevices(0x01, 0x02)); // Generic Desktop / Mouse
            list.AddRange(await GetConnectedDevices(0x01, 0x05)); // Generic Desktop / Game Pad
            list.AddRange(await GetConnectedDevices(0x01, 0x06)); // Generic Desktop / Keyboard

            return list;
        }

        public static async Task<List<HidInterfaceDetail>> GetConnectedDevices(ushort usagepage, ushort usageId)
        {
            var selector = HidDevice.GetDeviceSelector(usagepage, usageId);

            string[] extraProps = 
            {
                "System.Devices.DeviceInstanceId",
                "System.ItemNameDisplay",
                "System.Devices.Manufacturer"
            };

            var infos = await DeviceInformation.FindAllAsync(selector, extraProps);

            var list = new List<HidInterfaceDetail>();

            foreach (var di in infos)
            {
                ushort vid = 0, pid = 0, ver = 0, usage = usageId, usagePage = usagepage;

                string manufacturer = di.Properties.TryGetValue("System.Devices.Manufacturer", out var oMan) && oMan is string man ? man : string.Empty;
                string product = di.Properties.TryGetValue("System.ItemNameDisplay", out var oName) && oName is string name ? name : (di.Name ?? string.Empty);
                string id = di.Id ?? string.Empty;

                var hid = await HidDevice.FromIdAsync(di.Id, FileAccessMode.Read);
                if (hid != null)
                {
                    vid = hid.VendorId;
                    pid = hid.ProductId;
                    ver = hid.Version;
                    usage = hid.UsageId;
                    usagePage = hid.UsagePage;

                    hid.Dispose();
                }
                else
                {
                    if (di.Properties.TryGetValue("System.Devices.DeviceInstanceId", out var oInst) && oInst is string inst)
                    {
                        var m = Regex.Match(inst, @"VID[_&]([0-9A-Fa-f]{4}).*PID[_&]([0-9A-Fa-f]{4})(?:.*REV[_&]([0-9A-Fa-f]{4}))?");
                        if (m.Success)
                        {
                            vid = Convert.ToUInt16(m.Groups[1].Value, 16);
                            pid = Convert.ToUInt16(m.Groups[2].Value, 16);
                            if (m.Groups[3].Success) ver = Convert.ToUInt16(m.Groups[3].Value, 16);
                        }
                    }
                }

                list.Add(new HidInterfaceDetail(
                    pid: pid,
                    vid: vid,
                    product: product,
                    manufacturer: manufacturer,
                    id: id,
                    versionNumber: ver,
                    usage: usage,
                    usagePage: usagePage));
            }

            return list;
        }

        private void StartAsyncRead()
        {
            _readCts = new CancellationTokenSource();
            _readTask = Task.Run(() => ReadLoopAsync(_readCts.Token));
        }

        private async Task ReadLoopAsync(CancellationToken token)
        {
            var buffer = GC.AllocateUninitializedArray<byte>(_cap.InputReportByteLength);

            while (!token.IsCancellationRequested && IsDeviceConnected)
            {
                try
                {
                    var n = await _fsRead.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
                    if (n <= 0)
                    {
                        if (IsDeviceConnected)
                        {
                            IsDeviceConnected = false;
                            InvokeDeviceDisconnected();
                        }
                        break;
                    }

                    // Zero-copy: just a slice view over the buffer
                    InvokeDataReceived(buffer.AsMemory(0, n));
                }
                catch (OperationCanceledException) { break; }
                catch
                {
                    if (IsDeviceConnected)
                    {
                        IsDeviceConnected = false;
                        InvokeDeviceDisconnected();
                    }
                    break;
                }
            }
        }

        public override async Task<bool> WriteAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (data is null) throw new ArgumentNullException(nameof(data));
            if (data.Length > _cap.OutputReportByteLength - 1)
                throw new ArgumentException($"Data length {data.Length} > max {_cap.OutputReportByteLength - 1}");

            await _writeSem.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var packet = new byte[_cap.OutputReportByteLength];
                packet[0] = GetReportId();
                Array.Copy(data, 0, packet, 1, data.Length);

                await _fsWrite.WriteAsync(packet, 0, packet.Length, cancellationToken).ConfigureAwait(false);
                await _fsWrite.FlushAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                _writeSem.Release();
            }
        }

        public override async Task<byte[]> ReadAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (UseAsyncReads) throw new InvalidOperationException("Disable async reads to use ReadAsync.");

            await _readSem.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var buffer = new byte[_cap.InputReportByteLength];

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(DEFAULT_TIMEOUT_MS);

                var n = await _fsRead.ReadAsync(buffer, 0, buffer.Length, cts.Token).ConfigureAwait(false);
                if (n <= 0) return Array.Empty<byte>();

                var data = new byte[n];
                Array.Copy(buffer, data, n);
                return data;
            }
            finally
            {
                _readSem.Release();
            }
        }

        private byte GetReportId()
        {
            if (Helpers.Constants.OMNIPIDList?.Any(pid => pid == ProductInfo.PID) == true)
            {
                return ProductInfo.UsagePage switch
                {
                    0xFF02 => 0x01,
                    0xFF00 => 0x02,
                    0xFF01 => 0x03,
                    _ => 0x00
                };
            }

            return (ProductInfo.PID == 0x1AD3 || ProductInfo.PID == 0x1AFA) ? (byte)0xCC : (byte)0x00;
        }

        protected override void CloseDevice()
        {
            lock (_openLock)
            {
                if (device != null)
                {
                    device.InputReportReceived -= OnInputReportReceived;
                    device.Dispose();
                    device = null;
                }
            }

            // Drain any queued items and reset signal
            while (_rxQueue.TryDequeue(out _)) { }
            while (_rxSignal.CurrentCount > 0) _rxSignal.Wait(0);
        }

        [DllImport("kernel32.dll")]
        static extern void SetLastError(uint dwErrCode);

        private void InitDevice(string path, bool asyncReads)
        {
            try
            {
                SetLastError(0);
                var err = Marshal.GetLastWin32Error();
                System.Diagnostics.Debug.WriteLine($"CreateFile failed, error={err}");

                _handleRead = HidNative.CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                _handleWrite = HidNative.CreateFile(path, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);


                if (_handleRead.IsInvalid || _handleWrite.IsInvalid)
                    throw new IOException("Failed to open HID handles.");

                _descriptorContext = new HidDescriptorContext(_handleRead.DangerousGetHandle());
                _cap = _descriptorContext.Caps;

                var attrs = new HidNative.HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HidNative.HIDD_ATTRIBUTES>() };
                if (!HidNative.HidD_GetAttributes(_handleRead, ref attrs))
                    throw new IOException("Failed to query HID attributes.");

                ProductInfo = new HidInterfaceDetail(
                    pid: (ushort)attrs.ProductID,
                    vid: (ushort)attrs.VendorID,
                    product: GetDeviceString(_handleRead, HidNative.HidD_GetProductString),
                    manufacturer: GetDeviceString(_handleRead, HidNative.HidD_GetManufacturerString),
                    id: path,
                    versionNumber: (ushort)attrs.VersionNumber,
                    usage: _cap.Usage,
                    usagePage: _cap.UsagePage)
                {
                    InputReportByteLength = _cap.InputReportByteLength,
                    OutputReportByteLength = _cap.OutputReportByteLength
                };

                _fsRead = new FileStream(_handleRead, FileAccess.Read, _cap.InputReportByteLength, isAsync: false);
                _fsWrite = new FileStream(_handleWrite, FileAccess.Write, _cap.OutputReportByteLength, isAsync: false);

                IsDeviceConnected = true;
                UseAsyncReads = asyncReads;

                if (asyncReads) StartAsyncRead();
            }
            catch
            {
                Cleanup();
                throw;
            }
        }

        private void OnInputReportReceived(HidDevice sender, HidInputReportReceivedEventArgs args)
        {
            try
            {
                var report = args.Report;
                var payload = BufferToArray(report.Data);
                var packet = new byte[(payload?.Length ?? 0) + 1];
                packet[0] = (byte)report.Id;
                if (payload is { Length: > 0 })
                    Array.Copy(payload, 0, packet, 1, payload.Length);

                _rxQueue.Enqueue(packet);
                _ = _rxSignal.Release();
            }
            catch
            {
                // swallow to avoid tearing down the event pipeline
            }
        }

        private static string GetDeviceString(SafeFileHandle handle, Func<SafeFileHandle, IntPtr, uint, bool> getter)
        {
            IntPtr buf = Marshal.AllocHGlobal(256);
            try
            {
                if (getter(handle, buf, 256)) return Marshal.PtrToStringUni(buf) ?? string.Empty;
                return string.Empty;
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }

        private static byte[] BufferToArray(IBuffer buffer)
        {
            if (buffer == null || buffer.Length == 0) return Array.Empty<byte>();
            var arr = buffer.ToArray(0, (int)buffer.Length);
            return arr ?? Array.Empty<byte>();
        }

        private static IBuffer ArrayToBuffer(byte[] data)
        {
            return (data == null || data.Length == 0) ? new byte[0].AsBuffer() : data.AsBuffer();
        }

        public override ushort[] GetPressedButtons(byte[] inputReport, ushort usagePage = 0x09, ushort linkCollection = 0)
            => _descriptorContext?.GetPressedButtons(inputReport, usagePage, linkCollection) ?? Array.Empty<ushort>();

        public override bool TryGetUsageValue(byte[] inputReport, ushort usagePage, ushort usage, out int value, ushort linkCollection = 0)
            => _descriptorContext?.TryGetUsageValue(inputReport, usagePage, usage, out value, linkCollection) ?? (value = 0) == 0 && false;

        private void Cleanup()
        {
            try { _fsRead?.Dispose(); } catch { }
            try { _fsWrite?.Dispose(); } catch { }
            try { _handleRead?.Dispose(); } catch { }
            try { _handleWrite?.Dispose(); } catch { }
            try { _descriptorContext?.Dispose(); } catch { }
            try { _readCts?.Dispose(); } catch { }
            try { _readSem?.Dispose(); } catch { }
            try { _writeSem?.Dispose(); } catch { }

            _fsRead = null;
            _fsWrite = null;
            _handleRead = null;
            _handleWrite = null;
            _descriptorContext = null;
        }
    }
}
