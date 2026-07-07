using System.Collections.Concurrent;

namespace Base.Services.Peripheral;

public enum ConnectionType { USB, HID, BLE, BT, Unknown }

/// <summary>
/// Physical transport a peripheral interface is reached over.
/// </summary>
public enum PeripheralTransport
{
    UsbHid,
    BluetoothLE,
    BluetoothClassic,
}

public static class PeripheralTransportExtensions
{
    /// <summary>Short label shown in the device selection UI.</summary>
    public static string GetLabel(this PeripheralTransport transport) => transport switch
    {
        PeripheralTransport.BluetoothLE => "BLE",
        PeripheralTransport.BluetoothClassic => "BT",
        _ => "USB/HID",
    };

    /// <summary>Stable token used in persisted/API device identifiers.</summary>
    public static string GetKey(this PeripheralTransport transport) => transport switch
    {
        PeripheralTransport.BluetoothLE => "BLE",
        PeripheralTransport.BluetoothClassic => "BT",
        _ => "USB",
    };
}

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
    string ContainerID { get; }
    ConnectionType ConnectionType { get; }
    PeripheralTransport Transport { get; }

    PeripheralInterface Connect(bool useAsyncRead = false);

    string GetUniqueIdentifier();
}

public abstract class PeripheralInterfaceDetail(
    ushort pid = 0,
    ushort vid = 0,
    string product = "",
    string manufacturer = "",
    string id = "",
    ushort versionNumber = 0,
    ushort usage = 0,
    ushort usagePage = 0,
    string containerId = "") : IPeripheralDetail
{
    protected static readonly Dictionary<string, PeripheralInterface> connections = new();
    public ushort PID { get; protected set; } = pid;
    public ushort VID { get; protected set; } = vid;
    public string Product { get; protected set; } = product ?? string.Empty;
    public string Manufacturer { get; protected set; } = manufacturer ?? string.Empty;
    public string ID { get; protected set; } = id ?? string.Empty;
    public ushort VersionNumber { get; protected set; } = versionNumber;
    public ushort UsagePage { get; protected set; } = usagePage;
    public ushort Usage { get; protected set; } = usage;
    public string ContainerID { get; protected set; } = containerId ?? string.Empty;
    public abstract ConnectionType ConnectionType { get; }

    public virtual PeripheralTransport Transport => DetectTransport(ID);

    /// <summary>
    /// HID interfaces surfaced by the Bluetooth stack carry the Bluetooth enumerator
    /// (BTHLE / BTHENUM) or the HID-over-GATT service UUID (0x1812) in their device
    /// path; anything else reached through the HID class driver is wired USB.
    /// </summary>
    public static PeripheralTransport DetectTransport(string devicePath)
    {
        if (string.IsNullOrEmpty(devicePath)) return PeripheralTransport.UsbHid;
        if (devicePath.Contains("BTHLE", StringComparison.OrdinalIgnoreCase)
            || devicePath.Contains("00001812-0000-1000-8000-00805f9b34fb", StringComparison.OrdinalIgnoreCase))
            return PeripheralTransport.BluetoothLE;
        if (devicePath.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase))
            return PeripheralTransport.BluetoothClassic;
        return PeripheralTransport.UsbHid;
    }

    protected abstract PeripheralInterface CreateConnection(bool useAsyncRead = false);
    public PeripheralInterface Connect(bool useAsyncRead = false)
    {
        var key = GetUniqueIdentifier();
        lock (connections)
        {
            if (connections.TryGetValue(key, out var existingConnection))
            {
                return existingConnection;
            }
            var newConnection = CreateConnection(useAsyncRead);
            if (newConnection == null) return null;
            connections[key] = newConnection;
            newConnection.OnDisconnected += () =>
            {
                lock (connections)
                {
                    connections.Remove(key);
                }
            };
            return newConnection;
        }
    }
    public virtual string GetUniqueIdentifier() => $"{VID:X4}:{PID:X4}:{UsagePage:X4}:{Usage:X4}:{ID}";
    public bool Equals(IPeripheralDetail other) => other is not null && GetUniqueIdentifier() == other.GetUniqueIdentifier();
    public override bool Equals(object obj) => Equals(obj as IPeripheralDetail);
    public override int GetHashCode() => GetUniqueIdentifier().GetHashCode();
    public override string ToString() => $"{Product} ({Manufacturer}) - VID:{VID:X4} PID:{PID:X4}";
}

public abstract class PeripheralInterface : IDisposable
{
    private readonly object lockObject = new();
    private readonly CancellationTokenSource cts = new();
    private bool disposed;

    public IPeripheralDetail ProductInfo { get; protected set; }

    private bool isDeviceConnected;
    public bool IsDeviceConnected
    {
        get { lock (lockObject) return isDeviceConnected; }
        protected set { lock (lockObject) isDeviceConnected = value; }
    }

    public bool UseAsyncReads { get; protected set; }

    private Action<ReadOnlyMemory<byte>, DateTime> onDataReceived;
    public event Action<ReadOnlyMemory<byte>, DateTime> OnDataReceived
    {
        add { lock (lockObject) onDataReceived += value; }
        remove { lock (lockObject) onDataReceived -= value; }
    }

    private Action<ReadOnlyMemory<byte>, DateTime> onDataSent;
    public event Action<ReadOnlyMemory<byte>, DateTime> OnDataSent
    {
        add { lock (lockObject) onDataSent += value; }
        remove { lock (lockObject) onDataSent -= value; }
    }

    private event Action onDisconnected;
    public event Action OnDisconnected
    {
        add { lock (lockObject) onDisconnected += value; }
        remove { lock (lockObject) onDisconnected -= value; }
    }

    private readonly SemaphoreSlim rxSignal = new(0, int.MaxValue);
    private readonly ConcurrentQueue<ReadOnlyMemory<byte>> rxQueue = new();

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

        var sources = new (string Name, Task<List<IPeripheralDetail>> Task)[]
        {
            ("USB", EnumerateSource(UsbInterface.GetConnectedDevices)),
            ("HID", EnumerateSource(HidInterface.GetConnectedDevices)),
            ("BLE", EnumerateSource(BLEInterface.GetConnectedDevices)),
            //("BT", EnumerateSource(BTInterface.GetConnectedDevices)),
        };

        // The underlying WinRT/Win32 enumerations are not reliably cancellable,
        // so bound the wait instead: a source that misses the deadline simply
        // contributes nothing this round.
        var all = Task.WhenAll(sources.Select(s => s.Task));
        await Task.WhenAny(all, Task.Delay(timeout.Value, cancellationToken)).ConfigureAwait(false);

        var results = new List<IPeripheralDetail>();
        foreach (var (name, task) in sources)
        {
            if (task.IsCompletedSuccessfully)
                results.AddRange(task.Result);
            else
                Debug.Log($"[Peripheral] {name} enumeration did not finish within {timeout.Value.TotalSeconds:0.#}s.");
        }
        return results;
    }

    /// <summary>
    /// Runs one enumeration source, isolating its failures so a broken or absent
    /// stack (e.g. no Bluetooth radio) never breaks discovery of the other sources.
    /// </summary>
    private static Task<List<IPeripheralDetail>> EnumerateSource<T>(Func<Task<List<T>>> source) where T : IPeripheralDetail
    {
        return Task.Run(async () =>
        {
            try
            {
                var list = await source().ConfigureAwait(false);
                return list?.Cast<IPeripheralDetail>().ToList() ?? new List<IPeripheralDetail>();
            }
            catch (Exception ex)
            {
                Debug.Log($"[Peripheral] Device enumeration failed: {ex.Message}");
                return new List<IPeripheralDetail>();
            }
        });
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

    /// <summary>
    /// Human-readable dump of the input-report capabilities the device DECLARES
    /// (button/value usages, report ids, bit sizes, logical ranges, link
    /// collections). Used for diagnosing usage→UI mapping mismatches. Interfaces
    /// without a HID report descriptor return a short placeholder.
    /// </summary>
    public virtual string DescribeInputCapabilities()
        => "No HID report descriptor available for this interface.";

    // Cache latest input report per ReportId (USB HID: first byte is ReportId if > 0)
    private readonly ConcurrentDictionary<byte, byte[]> lastReports = new();

    public virtual ushort[] GetPressedButtons(byte[] inputReport, ushort usagePage = 0x09, ushort linkCollection = 0)
        => Array.Empty<ushort>();

    public virtual bool TryGetUsageValue(byte[] inputReport, ushort usagePage, ushort usage, out int value, ushort linkCollection = 0)
    {
        value = 0;
        return false;
    }

    /// <summary>
    /// Returns the HID value capabilities (including LogicalMin/LogicalMax) for the specified usage.
    /// </summary>
    public virtual bool TryGetValueCap(ushort usagePage, ushort usage, out HidValueCap cap)
    {
        cap = default;
        return false;
    }

    // Call from concrete classes when a report arrives
    protected void CacheInputReport(byte[] report)
    {
        if (report is null || report.Length == 0) return;
        byte reportId = report[0];
        lastReports[reportId] = (byte[])report.Clone();
    }

    protected bool TryGetCachedReport(byte reportId, out byte[] report)
    {
        if (reportId == 0) // single-report devices sometimes don't prepend 0
        {
            // Prefer any cached report if ID 0 isn't explicitly present
            foreach (var kv in lastReports)
            {
                report = kv.Value;
                return true;
            }
        }
        return lastReports.TryGetValue(reportId, out report);
    }

    public virtual Task<bool> Write(byte[] data) => WriteAsync(data, CancellationToken.None);

    /// <summary>
    /// Wait for the next received report (from InvokeDataReceived) without polling.
    /// </summary>
    public async Task<ReadOnlyMemory<byte>> WaitForNextReportAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await rxSignal.WaitAsync(cancellationToken).ConfigureAwait(false);

        // Signal was released -> item should exist; be defensive anyway.
        ReadOnlyMemory<byte> data;
        while (!rxQueue.TryDequeue(out data))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }

        return data;
    }

    /// <summary>
    /// Drop queued reports; useful before issuing a request/response command.
    /// </summary>
    public void ClearPendingReports()
    {
        while (rxQueue.TryDequeue(out _)) { }
        while (rxSignal.CurrentCount > 0)
        {
            try { rxSignal.Wait(0); }
            catch { break; }
        }
    }

    public async Task<byte[]> WriteAndReadAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Prevent stale earlier reports from satisfying this call.
        ClearPendingReports();

        await WriteAsync(data, cancellationToken).ConfigureAwait(false);

        var report = await WaitForNextReportAsync(cancellationToken).ConfigureAwait(false);
        return report.ToArray();
    }

    protected void InvokeDataReceived(ReadOnlyMemory<byte> data)
    {
        DateTime now = DateTime.UtcNow;
        if (data.IsEmpty) return;

        // Always enqueue so WriteAndReadAsync can pick it up even if the
        // device responds before WaitForNextReportAsync starts waiting.
        rxQueue.Enqueue(data);
        rxSignal.Release();

        Action<ReadOnlyMemory<byte>, DateTime> handler;
        lock (lockObject) handler = onDataReceived;
        handler?.Invoke(data, now);
    }

    protected void InvokeDataSent(ReadOnlyMemory<byte> data)
    {
        DateTime now = DateTime.UtcNow;
        if (data.IsEmpty) return;

        Action<ReadOnlyMemory<byte>, DateTime> handler;
        lock (lockObject) handler = onDataSent;
        handler?.Invoke(data, now);
    }

    protected void InvokeDeviceDisconnected()
    {
        Action handler;
        lock (lockObject) handler = onDisconnected;
        handler?.Invoke();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        try
        {
            cts.Cancel();
            CloseDevice();
            InvokeDeviceDisconnected();
            lock (lockObject)
            {
                onDataReceived = null;
                onDataSent = null;
                onDisconnected = null;
            }
            IsDeviceConnected = false;
        }
        catch { /* ignore */ }
        finally
        {
            // Ensure any waiters unblock during shutdown.
            try { rxSignal.Release(int.MaxValue / 4); } catch { }
            rxSignal.Dispose();

            cts.Dispose();
            GC.SuppressFinalize(this);
        }
    }

    ~PeripheralInterface() => Dispose();

    protected void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
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

    public bool TryGetUsageValue(HidValueCap cap, out int value)
    {
        value = 0;
        return !TryGetCachedReport(cap.ReportId, out var report)
            ? false
            : TryGetUsageValue(report, cap.UsagePage, cap.Usage, out value, cap.LinkCollection);
    }
}
                                                                                                                                                