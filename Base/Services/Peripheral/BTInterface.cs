using System.IO;
using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;

namespace Base.Services.Peripheral
{
    public sealed class BTInterfaceDetail : PeripheraInterfaceDetail
    {
        public string PortName { get; }

        public BTInterfaceDetail(
            ushort pid = 0,
            ushort vid = 0,
            string product = "",
            string manufacturer = "",
            string id = "",
            string portName = "",
            ushort versionNumber = 0,
            ushort usage = 0,
            ushort usagePage = 0)
            : base(pid, vid, product, manufacturer, id, versionNumber, usage, usagePage)
        {
            PortName = portName ?? string.Empty;
        }

        public override PeripheralInterface Connect(bool useAsyncRead = false) => new BTInterface(this, useAsyncRead);
    }

    public sealed class BTInterface : PeripheralInterface
    {
        private readonly SerialPort _port;

        public BTInterfaceDetail BtInfo => (BTInterfaceDetail)ProductInfo;

        public BTInterface(IPeripheralDetail interfaceDetail, bool useAsyncRead = false)
            : base(interfaceDetail, useAsyncRead)
        {
            var match = GetConnectedDevicesTask()
                .FirstOrDefault(d => d.VID == interfaceDetail.VID && d.PID == interfaceDetail.PID);

            if (match is null)
                throw new IOException("Target BT serial device not found.");

            ProductInfo = match;

            _port = new SerialPort($"COM{match.PortName}", 9600, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 500,
                WriteTimeout = 500
            };
            _port.Open();
            IsDeviceConnected = _port.IsOpen;
        }

        public static List<BTInterfaceDetail> GetConnectedDevicesTask()
        {
            var t = Task.Run(GetConnectedDevices);
            t.Wait(TimeSpan.FromSeconds(2));
            return t.Result;
        }

        public static Task<List<BTInterfaceDetail>> GetConnectedDevices()
        {
            var list = new List<BTInterfaceDetail>();
            try
            {
                // Typical captions contain "... (COMx)"
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE Caption like '%(COM%'");
                foreach (var port in searcher.Get().Cast<ManagementBaseObject>())
                {
                    try
                    {
                        var caption = port["Caption"]?.ToString() ?? string.Empty;
                        var deviceId = port["DeviceID"]?.ToString() ?? string.Empty;
                        var manufacturer = port["Manufacturer"]?.ToString() ?? string.Empty;

                        var mPort = Regex.Match(caption, @"COM(?<n>\d+)");
                        if (!mPort.Success) continue;

                        // Support both VID_XXXX&PID_YYYY and VID&0000XXXX_PID&YYYY
                        ushort vid = 0, pid = 0;
                        var m1 = Regex.Match(deviceId, @"VID_([0-9A-F]{4}).*PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
                        if (m1.Success)
                        {
                            vid = Convert.ToUInt16(m1.Groups[1].Value, 16);
                            pid = Convert.ToUInt16(m1.Groups[2].Value, 16);
                        }
                        else
                        {
                            var m2 = Regex.Match(deviceId, @"VID&([0-9A-F]{8}).*PID&([0-9A-F]{4})", RegexOptions.IgnoreCase);
                            if (m2.Success)
                            {
                                var vidRaw = m2.Groups[1].Value;
                                vid = Convert.ToUInt16(vidRaw[^4..], 16); // last 4 are the USB VID
                                pid = Convert.ToUInt16(m2.Groups[2].Value, 16);
                            }
                        }

                        var detail = new BTInterfaceDetail(
                            pid: pid,
                            vid: vid,
                            product: caption,
                            manufacturer: manufacturer,
                            id: deviceId,
                            portName: mPort.Groups["n"].Value);

                        list.Add(detail);
                    }
                    catch
                    {
                        // ignore bad entries
                    }
                }
            }
            catch
            {
                // swallow WMI failures
            }

            return Task.FromResult(list);
        }

        public override async Task<bool> WriteAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (_port is null || !_port.IsOpen) return false;

            await _port.BaseStream.WriteAsync(data.AsMemory(0, data.Length), cancellationToken);
            await _port.BaseStream.FlushAsync(cancellationToken);
            return true;
        }

        public override async Task<byte[]> ReadAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (_port is null || !_port.IsOpen) return Array.Empty<byte>();

            var buffer = new byte[_port.BytesToRead > 0 ? _port.BytesToRead : 1];
            var n = await _port.BaseStream.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (n <= 0) return Array.Empty<byte>();

            if (n == buffer.Length) return buffer;
            var slice = new byte[n];
            Array.Copy(buffer, slice, n);
            return slice;
        }

        protected override void CloseDevice()
        {
            try
            {
                if (_port != null)
                {
                    if (_port.IsOpen) _port.Close();
                    _port.Dispose();
                }
            }
            catch { /* ignore */ }
            finally
            {
                IsDeviceConnected = false;
            }
        }
    }
}
