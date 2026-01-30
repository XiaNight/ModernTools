using System.Collections.Concurrent;
using Windows.Devices.Bluetooth.Advertisement;
using static Base.Services.DeviceSelection;

namespace Base.Services.Peripheral
{
    public interface IPeripheralDetail : IEquatable<IPeripheralDetail>
    {
        ushort PID { get; }
        ushort VID { get; }
        string Product { get; }
        string Manufacturer { get; }
        string ID { get; }
        ushort VersionNumber { get; }
        ushort UsagePage { get; }
        ushort Usage { get; }

        PeripheralInterface Connect(bool useAsyncRead = false);

        string GetUniqueIdentifier();
    }

    public abstract class PeripheraInterfaceDetail : IPeripheralDetail
    {
        public ushort PID { get; protected set; }
        public ushort VID { get; protected set; }
        public string Product { get; protected set; }
        public string Manufacturer { get; protected set; }
        public string ID { get; protected set; }
        public ushort VersionNumber { get; protected set; }
        public ushort UsagePage { get; protected set; }
        public ushort Usage { get; protected set; }

        protected PeripheraInterfaceDetail(
            ushort pid = 0,
            ushort vid = 0,
            string product = "",
            string manufacturer = "",
            string id = "",
            ushort versionNumber = 0,
            ushort usage = 0,
            ushort usagePage = 0)
        {
            PID = pid;
            VID = vid;
            Product = product ?? string.Empty;
            Manufacturer = manufacturer ?? string.Empty;
            ID = id ?? string.Empty;
            VersionNumber = versionNumber;
            Usage = usage;
            UsagePage = usagePage;
        }

        public abstract PeripheralInterface Connect(bool useAsyncRead = false);
        public virtual string GetUniqueIdentifier() => $"{VID:X4}:{PID:X4}:{UsagePage:X4}:{Usage:X4}:{ID}";
        public bool Equals(IPeripheralDetail other) => other is not null && GetUniqueIdentifier() == other.GetUniqueIdentifier();
        public override bool Equals(object obj) => Equals(obj as IPeripheralDetail);
        public override int GetHashCode() => GetUniqueIdentifier().GetHashCode();
        public override string ToString() => $"{Product} ({Manufacturer}) - VID:{VID:X4} PID:{PID:X4}";
    }

    public abstract class PeripheralInterface : IDisposable
    {
        private readonly object _lockObject = new();
        private readonly CancellationTokenSource _cts = new();
        private bool _disposed;

        public IPeripheralDetail ProductInfo { get; protected set; }

        private bool _isDeviceConnected;
        public bool IsDeviceConnected
        {
            get { lock (_lockObject) return _isDeviceConnected; }
            protected set { lock (_lockObject) _isDeviceConnected = value; }
        }

        public bool UseAsyncReads { get; protected set; }
        public bool ReadFlag { get; set; }

        private Action<ReadOnlyMemory<byte>> _onDataReceived;
        public event Action<ReadOnlyMemory<byte>> OnDataReceived
        {
            add { lock (_lockObject) _onDataReceived += value; }
            remove { lock (_lockObject) _onDataReceived -= value; }
        }

        private event Action _onDisconnected;
        public event Action OnDisconnected
        {
            add { lock (_lockObject) _onDisconnected += value; }
            remove { lock (_lockObject) _onDisconnected -= value; }
        }

        protected PeripheralInterface(IPeripheralDetail interfaceDetail, bool useAsyncReads = false)
        {
            ProductInfo = interfaceDetail ?? throw new ArgumentNullException(nameof(interfaceDetail));
            UseAsyncReads = useAsyncReads;
        }

        public static async Task<List<IPeripheralDetail>> GetConnectedDevicesAsync(
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            timeout ??= TimeSpan.FromSeconds(5);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout.Value);

            var tasks = new List<Task<List<IPeripheralDetail>>>
            {
                Task.Run(() => UsbInterface.GetConnectedDevices().Result.Cast<IPeripheralDetail>().ToList(), cts.Token),
                //BLEInterface.GetConnectedDevices().ContinueWith(t => t.Result.Cast<IPeripheralDetail>().ToList(), cts.Token),
                //BTInterface.GetConnectedDevices().ContinueWith(t => t.Result.Cast<IPeripheralDetail>().ToList(), cts.Token),
                HidInterface.GetConnectedDevices().ContinueWith(t => t.Result.Cast<IPeripheralDetail>().ToList(), cts.Token)
            };

            var finishedTasks = await Task.WhenAll(tasks);

            var results = new List<IPeripheralDetail>();
            foreach (var t in finishedTasks)
            {
                try
                {
                    results.AddRange(t);
                }
                catch { /* ignore per-source failures */ }
            }
            return results;
        }

        [Obsolete("Use GetConnectedDevicesAsync for better performance and cancellation support")]
        public static List<IPeripheralDetail> GetConnectedDevices()
            => GetConnectedDevicesAsync().GetAwaiter().GetResult();

        public abstract Task<bool> WriteAsync(byte[] data, CancellationToken cancellationToken = default);
        public abstract Task<byte[]> ReadAsync(CancellationToken cancellationToken = default);
        protected abstract void CloseDevice();

        public virtual int InputReportLength => 0;
        public virtual int OutputReportLength => 0;
        public virtual (ushort UsagePage, ushort Usage) GetTopLevelUsage() => (0, 0);
        // Cache latest input report per ReportId (USB HID: first byte is ReportId if > 0)
        private readonly ConcurrentDictionary<byte, byte[]> _lastReports = new();

        public virtual ushort[] GetPressedButtons(byte[] inputReport, ushort usagePage = 0x09, ushort linkCollection = 0)
            => Array.Empty<ushort>();

        public virtual bool TryGetUsageValue(byte[] inputReport, ushort usagePage, ushort usage, out int value, ushort linkCollection = 0)
        {
            value = 0;
            return false;
        }

        // Call from concrete classes when a report arrives
        protected void CacheInputReport(byte[] report)
        {
            if (report is null || report.Length == 0) return;
            byte reportId = report[0];
            _lastReports[reportId] = (byte[])report.Clone();
        }

        protected bool TryGetCachedReport(byte reportId, out byte[] report)
        {
            if (reportId == 0) // single-report devices sometimes don't prepend 0
            {
                // Prefer any cached report if ID 0 isn't explicitly present
                foreach (var kv in _lastReports)
                {
                    report = kv.Value;
                    return true;
                }
            }
            return _lastReports.TryGetValue(reportId, out report);
        }

        public virtual Task Write(byte[] data) => WriteAsync(data, CancellationToken.None);

        public async Task<byte[]> WriteAndReadAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            ReadOnlyMemory<byte> returnData = Array.Empty<byte>();
            OnDataReceived += handler;

            try
            {
                ReadFlag = false;
                await WriteAsync(data, cancellationToken);

                await Task.Run(async () =>
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    while (!ReadFlag)
                    {
                        await Task.Delay(10); // Yield control
                        if (sw.ElapsedMilliseconds > 3000) break; // 3s timeout
                    }
                }, cancellationToken);
            }
            catch
            {
                Debug.Log("[HID] WriteAndReadAsync operation failed.");
            }
            finally
            {
                OnDataReceived -= handler;
            }

            return returnData.ToArray();

            void handler(ReadOnlyMemory<byte> receivedData)
            {
                returnData = receivedData;
            }
        }

        protected void InvokeDataReceived(ReadOnlyMemory<byte> data)
        {
            ReadFlag = true;
            if (data.IsEmpty) return;
            Action<ReadOnlyMemory<byte>> handler;
            lock (_lockObject) handler = _onDataReceived;
            handler?.Invoke(data);
        }

        protected void InvokeDeviceDisconnected()
        {
            Action handler;
            lock (_lockObject) handler = _onDisconnected;
            handler?.Invoke();
        }

        public void Close() => Dispose();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _cts.Cancel();
                CloseDevice();
                lock (_lockObject)
                {
                    _onDataReceived = null;
                    _onDisconnected = null;
                }
                IsDeviceConnected = false;
                InvokeDeviceDisconnected();
            }
            catch { /* ignore */ }
            finally
            {
                _cts.Dispose();
                GC.SuppressFinalize(this);
            }
        }

        ~PeripheralInterface() => Dispose();

        protected void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);
        }

        public struct HidValueCap
        {
            public ushort UsagePage { get; set; }
            public ushort Usage { get; set; }
            public ushort LinkCollection { get; set; }
            public byte ReportId { get; set; }

            public int LogicalMin { get; set; }
            public int LogicalMax { get; set; }
            public ushort BitSize { get; set; }
            public ushort ReportCount { get; set; }
            public bool HasNull { get; set; }
            public bool IsAbsolute { get; set; }
        }

        // Prefer this overload when you have a HidValueCap with ReportId/LinkCollection
        public bool TryGetUsageValue(HidValueCap cap, out int value)
        {
            value = 0;
            if (!TryGetCachedReport(cap.ReportId, out var report))
                return false;
            return TryGetUsageValue(report, cap.UsagePage, cap.Usage, out value, cap.LinkCollection);
        }
    }
}
