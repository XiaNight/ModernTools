using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Base.Services.Peripheral.Native;

namespace Base.Services.Peripheral
{
    public sealed class USBInterfaceDetail : PeripheraInterfaceDetail
    {
        public int SerialNumber { get; set; }
        public int InputReportByteLength { get; set; }
        public int OutputReportByteLength { get; set; }

        public USBInterfaceDetail(
            ushort pid = 0,
            ushort vid = 0,
            string product = "",
            string manufacturer = "",
            string id = "",
            ushort versionNumber = 0,
            ushort usage = 0,
            ushort usagePage = 0)
            : base(pid, vid, product, manufacturer, id, versionNumber, usage, usagePage)
        {
        }

        public override PeripheralInterface Connect(bool useAsyncRead = false) => new UsbInterface(this, useAsyncRead);
    }

    public sealed class UsbInterface : PeripheralInterface
    {
        private const int DIGCF_DEVICEINTERFACE = 0x10;
        private const int DIGCF_PRESENT = 0x2;
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const int DEFAULT_TIMEOUT_MS = 1000;

        private SafeFileHandle _handleRead;
        private SafeFileHandle _handleWrite;
        private FileStream _fsRead;
        private FileStream _fsWrite;
        private HidDescriptorContext _descriptorContext;
        private HidNative.HIDP_CAPS _cap;
        private readonly SemaphoreSlim _readSem = new(1, 1);
        private readonly SemaphoreSlim _writeSem = new(1, 1);
        private CancellationTokenSource _readCts;
        private Task _readTask;
        private bool _disposed;

        private static bool NtSuccess(int status) => status >= 0;

        public USBInterfaceDetail UsbInfo => (USBInterfaceDetail)ProductInfo;

        public override int InputReportLength => _cap.InputReportByteLength;
        public override int OutputReportLength => _cap.OutputReportByteLength;
        public override (ushort UsagePage, ushort Usage) GetTopLevelUsage() => (_cap.UsagePage, _cap.Usage);

        public UsbInterface(IPeripheralDetail info, bool useAsyncReads = false) : base(info, useAsyncReads)
        {
            try
            {
                InitDevice(info.ID, useAsyncReads);
                if (!IsDeviceConnected)
                    throw new InvalidOperationException($"Failed to connect: {info.Product}");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public static async Task<List<USBInterfaceDetail>> GetConnectedDevices()
            => await Task.Run(GetConnectedDevicesSync).ConfigureAwait(false);

        private static List<USBInterfaceDetail> GetConnectedDevicesSync()
        {
            var list = new List<USBInterfaceDetail>();
            IntPtr set = IntPtr.Zero;

            try
            {
                var hidGuid = Guid.Empty;
                HidNative.HidD_GetHidGuid(ref hidGuid);

                set = HidNative.SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_DEVICEINTERFACE | DIGCF_PRESENT);
                if (set == IntPtr.Zero || set.ToInt64() == -1) return list;

                var ifData = new HidNative.SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<HidNative.SP_DEVICE_INTERFACE_DATA>() };

                uint index = 0;
                while (HidNative.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, index++, ref ifData))
                {
                    var dev = GetDeviceDetails(set, ref ifData);
                    if (dev != null) list.Add(dev);
                }
            }
            finally
            {
                if (set != IntPtr.Zero && set.ToInt64() != -1)
                    HidNative.SetupDiDestroyDeviceInfoList(set);
            }

            return list;
        }

        private static USBInterfaceDetail GetDeviceDetails(IntPtr set, ref HidNative.SP_DEVICE_INTERFACE_DATA ifData)
        {
            var devInfo = new HidNative.SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<HidNative.SP_DEVINFO_DATA>() };
            var detail = new HidNative.SP_DEVICE_INTERFACE_DETAIL_DATA
            {
                cbSize = IntPtr.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize
            };

            if (!HidNative.SetupDiGetDeviceInterfaceDetail(set, ref ifData, ref detail, 256, out _, ref devInfo))
                return null;

            SafeFileHandle h = null;
            IntPtr pp = IntPtr.Zero;
            try
            {
                h = HidNative.CreateFile(detail.DevicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (h == null || h.IsInvalid) return null;

                if (!HidNative.HidD_GetPreparsedData(h, ref pp)) return null;

                var caps = new HidNative.HIDP_CAPS { Reserved = new ushort[17] };
                var st = HidNative.HidP_GetCaps(pp, ref caps);
                if (!NtSuccess(st)) return null;

                var attrs = new HidNative.HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HidNative.HIDD_ATTRIBUTES>() };
                if (!HidNative.HidD_GetAttributes(h, ref attrs)) return null;

                string product = GetDeviceString(h, HidNative.HidD_GetProductString);
                string manufacturer = GetDeviceString(h, HidNative.HidD_GetManufacturerString);
                string serial = GetDeviceString(h, HidNative.HidD_GetSerialNumberString);

                var info = new USBInterfaceDetail(
                    pid: (ushort)attrs.ProductID,
                    vid: (ushort)attrs.VendorID,
                    product: product,
                    manufacturer: manufacturer,
                    id: detail.DevicePath,
                    versionNumber: (ushort)attrs.VersionNumber,
                    usage: caps.Usage,
                    usagePage: caps.UsagePage)
                {
                    InputReportByteLength = caps.InputReportByteLength,
                    OutputReportByteLength = caps.OutputReportByteLength
                };

                if (int.TryParse(serial, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sn))
                    info.SerialNumber = sn;

                return info;
            }
            finally
            {
                if (pp != IntPtr.Zero) HidNative.HidD_FreePreparsedData(ref pp);
                h?.Dispose();
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

        private void InitDevice(string path, bool asyncReads)
        {
            try
            {
                _handleRead = HidNative.CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                _handleWrite = HidNative.CreateFile(path, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

                if (_handleRead.IsInvalid || _handleWrite.IsInvalid)
                    throw new IOException("Failed to open HID handles.");

                _descriptorContext = new HidDescriptorContext(_handleRead.DangerousGetHandle());
                _cap = _descriptorContext.Caps;

                var attrs = new HidNative.HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HidNative.HIDD_ATTRIBUTES>() };
                if (!HidNative.HidD_GetAttributes(_handleRead, ref attrs))
                    throw new IOException("Failed to query HID attributes.");

                ProductInfo = new USBInterfaceDetail(
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
            catch (OperationCanceledException)
            {
                // Preserve cancellation semantics
                throw;
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

        public override ushort[] GetPressedButtons(byte[] inputReport, ushort usagePage = 0x09, ushort linkCollection = 0)
            => _descriptorContext?.GetPressedButtons(inputReport, usagePage, linkCollection) ?? Array.Empty<ushort>();

        public override bool TryGetUsageValue(byte[] inputReport, ushort usagePage, ushort usage, out int value, ushort linkCollection = 0)
            => _descriptorContext?.TryGetUsageValue(inputReport, usagePage, usage, out value, linkCollection) ?? (value = 0) == 0 && false;

        protected override void CloseDevice()
        {
            try
            {
                _readCts?.Cancel();
                if (_readTask != null && !_readTask.IsCompleted)
                {
                    try { _readTask.Wait(500); } catch { /* ignored */ }
                }
            }
            finally
            {
                Cleanup();
                IsDeviceConnected = false;
            }
        }

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
