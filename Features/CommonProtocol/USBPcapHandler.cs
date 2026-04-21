using Microsoft.Win32.SafeHandles;
using System.Buffers;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public sealed class USBPCapHandler
{
    #region Definitions
    public sealed class UsbPcapRoot
    {
        public string FilterPath { get; set; } = "";
        public string HubPath { get; set; } = "";
        public List<UsbDeviceNode> Devices { get; } = new();
    }

    public sealed class UsbDeviceNode
    {
        public int Port { get; set; }
        public ushort DeviceAddress { get; set; }
        public ushort ParentHubAddress { get; set; }
        public bool IsHub { get; set; }
        public bool IsUsb2OrLower { get; set; }
        public ushort Vid { get; set; }
        public ushort Pid { get; set; }
        public ushort BcdUsb { get; set; }
        public byte DeviceClass { get; set; }
        public byte DeviceSubClass { get; set; }
        public byte DeviceProtocol { get; set; }
        public string HubPath { get; set; } = "";
        public string ExternalHubPath { get; set; }
        public List<UsbDeviceNode> Children { get; } = new();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct USBPCAP_IOCTL_SIZE
    {
        public uint size;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct USBPCAP_ADDRESS_FILTER
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public uint[] addresses;

        [MarshalAs(UnmanagedType.U1)]
        public bool filterAll;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct PcapGlobalHeader
    {
        public uint MagicNumber;
        public ushort VersionMajor;
        public ushort VersionMinor;
        public int ThisZone;
        public uint SigFigs;
        public uint SnapLen;
        public uint Network;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PcapRecordHeader
    {
        public uint TsSec;
        public uint TsUsec;
        public uint InclLen;
        public uint OrigLen;
    }
    public enum UsbPcapTransferType : byte
    {
        Isochronous = 0,
        Interrupt = 1,
        Control = 2,
        Bulk = 3
    }

    public enum UsbPcapEndpointDirection : byte
    {
        OUT = 0x00,
        IN = 0x80,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct UsbPcapPacket
    {
        public byte HeaderLength;
        public ulong IrpId;
        public uint Status;
        public ushort Function;
        public byte Info;
        public ushort Bus;
        public ushort Device;
        public byte Endpoint;
        public UsbPcapTransferType TransferType;
        public uint DataLength;
        public ReadOnlyMemory<byte> Payload;

        public readonly bool IsCompletion => (Info & 0x01) != 0;
        public readonly bool IsFdoToPdo => (Info & 0x01) == 0;
        public readonly byte EndpointNumber => (byte)(Endpoint & 0x7F);
        public readonly UsbPcapEndpointDirection Direction => (UsbPcapEndpointDirection)(Endpoint & 0x80);
    }

    #endregion

    #region Capturing

    public delegate void UsbPcapPacketHandler(ref PcapRecordHeader pcapHeader, ref UsbPcapPacket packet);
    public event UsbPcapPacketHandler OnPacketCaptured;

    public async Task StartCaptureToDebugAsync(
        string filterPath,
        byte deviceAddress,
        CancellationToken cancellationToken,
        bool captureNewDevicesAtAddressZero = false,
        uint snapLen = 256,
        uint kernelBufferSize = 1024 * 1024 * 16,
        bool printPayloadPreview = true,
        int maxPreviewBytes = 64)
    {
        if (string.IsNullOrWhiteSpace(filterPath))
            throw new ArgumentException("Filter path is required.", nameof(filterPath));

        if (deviceAddress > 127)
            throw new ArgumentOutOfRangeException(nameof(deviceAddress), "USB device address must be in range 0..127.");

        using SafeFileHandle filterHandle = CreateFile(
            filterPath,
            GENERIC_READ | GENERIC_WRITE,
            0,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_OVERLAPPED,
            IntPtr.Zero);

        if (filterHandle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, $"Failed to open USBPcap filter '{filterPath}'. Win32={error}");
        }

        SendUsbPcapSizeIoctl(filterHandle, IOCTL_USBPCAP_SET_SNAPLEN_SIZE, snapLen);
        SendUsbPcapSizeIoctl(filterHandle, IOCTL_USBPCAP_SETUP_BUFFER, kernelBufferSize);

        USBPCAP_ADDRESS_FILTER filter = CreateAddressFilter(deviceAddress, captureNewDevicesAtAddressZero);

        if (!DeviceIoControl(
                filterHandle,
                IOCTL_USBPCAP_START_FILTERING,
                ref filter,
                Marshal.SizeOf<USBPCAP_ADDRESS_FILTER>(),
                IntPtr.Zero,
                0,
                out _,
                IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "IOCTL_USBPCAP_START_FILTERING failed.");
        }

        Debug.WriteLine($"USBPcap capture started: Filter={filterPath}, DeviceAddress={deviceAddress}, SnapLen={snapLen}, Buffer={kernelBufferSize}");

        using var stream = new FileStream(filterHandle, FileAccess.Read, 8192, isAsync: true);

        await ReadPcapStreamAsync(stream, printPayloadPreview, maxPreviewBytes, cancellationToken);

        Debug.WriteLine("USBPcap capture stopped.");
    }

    private static USBPCAP_ADDRESS_FILTER CreateAddressFilter(byte deviceAddress, bool captureNewDevicesAtAddressZero)
    {
        var filter = new USBPCAP_ADDRESS_FILTER
        {
            addresses = new uint[4],
            filterAll = false
        };

        SetFilteredAddress(ref filter, deviceAddress);

        if (captureNewDevicesAtAddressZero)
            SetFilteredAddress(ref filter, 0);

        return filter;
    }

    private static void SetFilteredAddress(ref USBPCAP_ADDRESS_FILTER filter, int address)
    {
        if (address is < 0 or > 127)
            throw new ArgumentOutOfRangeException(nameof(address));

        int range = address / 32;
        int index = address % 32;
        filter.addresses[range] |= (1u << index);
    }

    private static void SendUsbPcapSizeIoctl(SafeFileHandle filterHandle, uint ioctl, uint value)
    {
        USBPCAP_IOCTL_SIZE input = new() { size = value };

        if (!DeviceIoControl(
                filterHandle,
                ioctl,
                ref input,
                Marshal.SizeOf<USBPCAP_IOCTL_SIZE>(),
                IntPtr.Zero,
                0,
                out _,
                IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"USBPcap size IOCTL 0x{ioctl:X8} failed.");
        }
    }

    readonly CallsPerSecondCounter cpsC = new();

    private async Task ReadPcapStreamAsync(Stream stream, bool printPayloadPreview, int maxPreviewBytes, CancellationToken cancellationToken)
    {
        byte[] globalHeaderBuffer = ArrayPool<byte>.Shared.Rent(24);
        byte[] recordHeaderBuffer = ArrayPool<byte>.Shared.Rent(256);

        try
        {
            PcapGlobalHeader global = await ReadPcapGlobalHeaderAsync(stream, globalHeaderBuffer, cancellationToken);

            Debug.WriteLine(
                $"PCAP Global Header: Magic=0x{global.MagicNumber:X8}, Version={global.VersionMajor}.{global.VersionMinor}, " +
                $"SnapLen={global.SnapLen}, Network={global.Network}");

            int packetIndex = 0;
            UsbPcapPacket usbPcapPacketBuffer = new();

            while (!cancellationToken.IsCancellationRequested)
            {
                var (success, record) = await TryReadPcapRecordHeaderAsync(stream, recordHeaderBuffer, cancellationToken);
                if (!success)
                    break;

                // temp dummy packet for testing the callback speed
                //OnPacketCaptured?.Invoke(ref record, ref usbPcapPacketBuffer);

                cpsC.Tick();

                //int payloadLength = checked((int)record.InclLen);
                //byte[] payloadBuffer = ArrayPool<byte>.Shared.Rent(payloadLength);

                //try
                //{
                //    await ReadExactAsync(stream, payloadBuffer, payloadLength, cancellationToken);

                //    packetIndex++;

                //    if (!ParseUsbPcapPacketNonAlloc(payloadBuffer.AsMemory(0, payloadLength), ref usbPcapPacketBuffer))
                //        continue;

                //    OnPacketCaptured?.Invoke(ref record, ref usbPcapPacketBuffer);
                //}
                //finally
                //{
                //    usbPcapPacketBuffer.Payload = ReadOnlyMemory<byte>.Empty;
                //    ArrayPool<byte>.Shared.Return(payloadBuffer);
                //}
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(globalHeaderBuffer);
            ArrayPool<byte>.Shared.Return(recordHeaderBuffer);
        }
    }

    public sealed class CallsPerSecondCounter : IDisposable
    {
        private readonly List<long> _ticks = new();
        private readonly object _lock = new();
        private Timer? _timer;

        public CallsPerSecondCounter()
        {
            StartDispatchTimer();
        }

        public void Tick()
        {
            lock (_lock)
            {
                _ticks.Add(DateTime.Now.Ticks);
            }
        }

        public double GetCallsPerSecond()
        {
            long now = DateTime.Now.Ticks;
            long cutoff = now - TimeSpan.TicksPerSecond;

            lock (_lock)
            {
                int removeCount = 0;

                while (removeCount < _ticks.Count && _ticks[removeCount] < cutoff)
                    removeCount++;

                if (removeCount > 0)
                    _ticks.RemoveRange(0, removeCount);

                return _ticks.Count;
            }
        }

        public void StartDispatchTimer(TimeSpan? interval = null, string prefix = "TPS")
        {
            _timer?.Dispose();
            _timer = new Timer(_ =>
            {
                double tps = GetCallsPerSecond();
                Debug.WriteLine($"{prefix}: {tps:F0}");
            }, null, TimeSpan.Zero, interval ?? TimeSpan.FromSeconds(1));
        }

        public void StopDispatchTimer()
        {
            _timer?.Dispose();
            _timer = null;
        }

        public void Dispose()
        {
            StopDispatchTimer();
        }
    }

    private static async Task<PcapGlobalHeader> ReadPcapGlobalHeaderAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        const int size = 24;
        await ReadExactAsync(stream, buffer, size, cancellationToken);
        return ParsePcapGlobalHeader(buffer);
    }

    private static async Task<(bool Success, PcapRecordHeader Value)> TryReadPcapRecordHeaderAsync(
        Stream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        const int size = 256;

        int read = await ReadExactOrLessAsync(stream, buffer, 0, size, cancellationToken);
        if (read == 0)
            return (false, default);

        return read != size
        ? throw new EndOfStreamException("Unexpected end of stream while reading PcapRecordHeader.")
        : ((bool Success, PcapRecordHeader Value))(true, ParsePcapRecordHeader(buffer));
    }

    private static PcapGlobalHeader ParsePcapGlobalHeader(byte[] buffer)
    {
        ReadOnlySpan<byte> span = buffer.AsSpan(0, 24);

        return new PcapGlobalHeader
        {
            MagicNumber = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(0, 4)),
            VersionMajor = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(4, 2)),
            VersionMinor = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(6, 2)),
            ThisZone = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(8, 4)),
            SigFigs = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(12, 4)),
            SnapLen = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(16, 4)),
            Network = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(20, 4)),
        };
    }

    private static PcapRecordHeader ParsePcapRecordHeader(byte[] buffer)
    {
        ReadOnlySpan<byte> span = buffer.AsSpan(0, 16);

        return new PcapRecordHeader
        {
            TsSec = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(0, 4)),
            TsUsec = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4, 4)),
            InclLen = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(8, 4)),
            OrigLen = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(12, 4)),
        };
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, int length, CancellationToken cancellationToken)
    {
        int read = await ReadExactOrLessAsync(stream, buffer, 0, length, cancellationToken);
        if (read != length)
            throw new EndOfStreamException("Unexpected end of stream while reading packet payload.");
    }

    private static async Task<int> ReadExactOrLessAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int total = 0;

        while (total < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset + total, count - total), cancellationToken);
            if (read == 0)
                break;

            total += read;
        }

        return total;
    }

    private static bool ParseUsbPcapPacketNonAlloc(ReadOnlyMemory<byte> buffer, ref UsbPcapPacket packet)
    {
        ReadOnlySpan<byte> span = buffer.Span;
        const int headerSize = 27;

        if (span.Length < headerSize)
            return false;

        byte headerLength = span[0];
        if (headerLength < headerSize || headerLength > span.Length)
            return false;

        uint dataLength = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(23, 4));
        int payloadOffset = headerLength;

        if ((ulong)payloadOffset + dataLength > (ulong)span.Length)
            return false;

        packet.HeaderLength = headerLength;
        packet.IrpId = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(1, 8));
        packet.Status = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(9, 4));
        packet.Function = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(13, 2));
        packet.Info = span[15];
        packet.Bus = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(16, 2));
        packet.Device = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(18, 2));
        packet.Endpoint = span[20];
        packet.TransferType = (UsbPcapTransferType)span[21];
        packet.DataLength = dataLength;
        packet.Payload = buffer.Slice(payloadOffset, checked((int)dataLength));

        return true;
    }

    #endregion

    #region Hub Enumeration

    public IReadOnlyList<UsbPcapRoot> BuildDeviceTree(int maxFiltersToProbe = 16)
    {
        var roots = new List<UsbPcapRoot>();

        for (int i = 1; i <= maxFiltersToProbe; i++)
        {
            string filterPath = $@"\\.\USBPcap{i}";
            string? hubPath = TryGetUsbPcapHubSymlink(filterPath);
            if (string.IsNullOrWhiteSpace(hubPath))
                continue;

            var root = new UsbPcapRoot
            {
                FilterPath = filterPath,
                HubPath = hubPath
            };

            EnumerateHubRecursive(hubPath, parentHubAddress: 0, root.Devices);
            roots.Add(root);
        }

        return roots;
    }

    private void EnumerateHubRecursive(string hubPath, ushort parentHubAddress, List<UsbDeviceNode> output)
    {
        using SafeFileHandle hubHandle = OpenHub(hubPath);
        if (hubHandle.IsInvalid)
            return;

        USB_NODE_INFORMATION hubInfo = new()
        {
            NodeType = USB_HUB_NODE.UsbHub
        };

        if (!DeviceIoControl(
                hubHandle,
                IOCTL_USB_GET_NODE_INFORMATION,
                ref hubInfo,
                Marshal.SizeOf<USB_NODE_INFORMATION>(),
                ref hubInfo,
                Marshal.SizeOf<USB_NODE_INFORMATION>(),
                out _,
                IntPtr.Zero))
        {
            return;
        }

        int portCount = hubInfo.HubInformation.HubDescriptor.bNumberOfPorts;

        for (int port = 1; port <= portCount; port++)
        {
            USB_NODE_CONNECTION_INFORMATION_EX conn = new()
            {
                ConnectionIndex = (uint)port
            };

            if (!DeviceIoControl(
                    hubHandle,
                    IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX,
                    ref conn,
                    Marshal.SizeOf<USB_NODE_CONNECTION_INFORMATION_EX>(),
                    ref conn,
                    Marshal.SizeOf<USB_NODE_CONNECTION_INFORMATION_EX>(),
                    out _,
                    IntPtr.Zero))
            {
                Debug.WriteLine($"  Port {port}: IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX failed, err={Marshal.GetLastWin32Error()}");
                continue;
            }

            if (conn.ConnectionStatus == USB_CONNECTION_STATUS.NoDeviceConnected)
                continue;

            var node = new UsbDeviceNode
            {
                Port = port,
                DeviceAddress = conn.DeviceAddress,
                ParentHubAddress = parentHubAddress,
                IsHub = conn.DeviceIsHub,
                IsUsb2OrLower = conn.DeviceDescriptor.bcdUSB <= 0x0200,
                Vid = conn.DeviceDescriptor.idVendor,
                Pid = conn.DeviceDescriptor.idProduct,
                BcdUsb = conn.DeviceDescriptor.bcdUSB,
                DeviceClass = conn.DeviceDescriptor.bDeviceClass,
                DeviceSubClass = conn.DeviceDescriptor.bDeviceSubClass,
                DeviceProtocol = conn.DeviceDescriptor.bDeviceProtocol,
                HubPath = hubPath
            };

            output.Add(node);

            if (node.IsHub)
            {
                string? externalHubPath = TryGetExternalHubName(hubHandle, port);
                if (!string.IsNullOrWhiteSpace(externalHubPath))
                {
                    node.ExternalHubPath = externalHubPath;
                    EnumerateHubRecursive(externalHubPath, node.DeviceAddress, node.Children);
                }
            }
        }
    }

    private static string? TryGetUsbPcapHubSymlink(string filterPath)
    {
        using SafeFileHandle filterHandle = CreateFile(
            filterPath,
            0,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (filterHandle.IsInvalid)
            return null;

        byte[] buffer = new byte[2048];
        if (!DeviceIoControl(
                filterHandle,
                IOCTL_USBPCAP_GET_HUB_SYMLINK,
                IntPtr.Zero,
                0,
                buffer,
                buffer.Length,
                out uint bytesReturned,
                IntPtr.Zero))
        {
            return null;
        }

        if (bytesReturned < 2)
            return null;

        string value = Encoding.Unicode.GetString(buffer, 0, (int)bytesReturned);
        int nul = value.IndexOf('\0');
        return nul >= 0 ? value[..nul] : value;
    }

    private static string? TryGetExternalHubName(SafeFileHandle hubHandle, int connectionIndex)
    {
        int size = 1024;
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.WriteInt32(buffer, 0, connectionIndex);
            Marshal.WriteInt32(buffer, 4, 0);

            if (!DeviceIoControl(
                    hubHandle,
                    IOCTL_USB_GET_NODE_CONNECTION_NAME,
                    buffer,
                    size,
                    buffer,
                    size,
                    out _,
                    IntPtr.Zero))
            {
                return null;
            }

            int actualLength = Marshal.ReadInt32(buffer, 4);
            if (actualLength <= 8)
                return null;

            string value = Marshal.PtrToStringUni(IntPtr.Add(buffer, 8)) ?? "";
            return string.IsNullOrWhiteSpace(value) ? null : NormalizeHubPath(value);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static SafeFileHandle OpenHub(string rawHubPath)
    {
        string hubPath = NormalizeHubPath(rawHubPath);

        return CreateFile(
            hubPath,
            GENERIC_WRITE,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            0,
            IntPtr.Zero);
    }

    private static string NormalizeHubPath(string path)
    {
        return path.StartsWith(@"\??\", StringComparison.Ordinal)
            ? @"\\.\" + path.Substring(4)
            : path.StartsWith(@"\\.\", StringComparison.Ordinal)
            ? path
            : path.StartsWith(@"\", StringComparison.Ordinal) ? path : @"\\.\" + path;
    }

    #endregion

    #region Debug Printing
    public void DebugPrintDeviceTree(int maxFiltersToProbe = 16)
    {
        var roots = BuildDeviceTree(maxFiltersToProbe);

        if (roots.Count == 0)
        {
            Debug.WriteLine("USBPcap: no capture roots found.");
            return;
        }

        foreach (var root in roots)
        {
            Debug.WriteLine($"USBPcap Root: Filter={root.FilterPath}, Hub={root.HubPath}");

            if (root.Devices.Count == 0)
            {
                Debug.WriteLine("  (no devices)");
                continue;
            }

            foreach (var device in root.Devices)
                DebugPrintNode(device, 1);
        }
    }

    private static void DebugPrintNode(UsbDeviceNode node, int indentLevel)
    {
        string indent = new(' ', indentLevel * 2);

        Debug.WriteLine(
            $"{indent}- Port={node.Port}, Addr={node.DeviceAddress}, ParentAddr={node.ParentHubAddress}, " +
            $"Hub={node.IsHub}, Usb2OrLower={node.IsUsb2OrLower}, " +
            $"VID=0x{node.Vid:X4}, PID=0x{node.Pid:X4}, bcdUSB=0x{node.BcdUsb:X4}, " +
            $"Class=0x{node.DeviceClass:X2}, SubClass=0x{node.DeviceSubClass:X2}, Protocol=0x{node.DeviceProtocol:X2}, " +
            $"HubPath={node.HubPath}, ExternalHubPath={node.ExternalHubPath ?? ""}");

        foreach (var child in node.Children)
            DebugPrintNode(child, indentLevel + 1);
    }

    #endregion

    #region WinAPI Constants and Structs

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;

    private const uint FILE_DEVICE_UNKNOWN = 0x00000022;
    private const uint FILE_ANY_ACCESS = 0;
    private const uint FILE_READ_ACCESS = 0x0001;
    private const uint FILE_WRITE_ACCESS = 0x0002;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    private const uint METHOD_BUFFERED = 0;

    private static uint CTL_CODE(uint deviceType, uint function, uint method, uint access) =>
        (deviceType << 16) | (access << 14) | (function << 2) | method;

    private static readonly uint IOCTL_USBPCAP_SETUP_BUFFER =
        CTL_CODE(FILE_DEVICE_UNKNOWN, 0x800, METHOD_BUFFERED, FILE_READ_ACCESS);

    private static readonly uint IOCTL_USBPCAP_START_FILTERING =
        CTL_CODE(FILE_DEVICE_UNKNOWN, 0x801, METHOD_BUFFERED, FILE_READ_ACCESS | FILE_WRITE_ACCESS);

    private static readonly uint IOCTL_USBPCAP_STOP_FILTERING =
        CTL_CODE(FILE_DEVICE_UNKNOWN, 0x802, METHOD_BUFFERED, FILE_READ_ACCESS | FILE_WRITE_ACCESS);

    private static readonly uint IOCTL_USBPCAP_GET_HUB_SYMLINK =
        CTL_CODE(FILE_DEVICE_UNKNOWN, 0x803, METHOD_BUFFERED, FILE_ANY_ACCESS);

    private static readonly uint IOCTL_USBPCAP_SET_SNAPLEN_SIZE =
        CTL_CODE(FILE_DEVICE_UNKNOWN, 0x804, METHOD_BUFFERED, FILE_READ_ACCESS);

    private static readonly uint IOCTL_USB_GET_NODE_INFORMATION =
        CTL_CODE(FILE_DEVICE_UNKNOWN, 258, METHOD_BUFFERED, FILE_ANY_ACCESS);

    private static readonly uint IOCTL_USB_GET_NODE_CONNECTION_NAME =
        CTL_CODE(FILE_DEVICE_UNKNOWN, 261, METHOD_BUFFERED, FILE_ANY_ACCESS);

    private static readonly uint IOCTL_USB_GET_NODE_CONNECTION_INFORMATION_EX =
        CTL_CODE(FILE_DEVICE_UNKNOWN, 274, METHOD_BUFFERED, FILE_ANY_ACCESS);

    [StructLayout(LayoutKind.Sequential)]
    private struct USB_HUB_DESCRIPTOR
    {
        public byte bDescriptorLength;
        public byte bDescriptorType;
        public byte bNumberOfPorts;
        public ushort wHubCharacteristics;
        public byte bPowerOnToPowerGood;
        public byte bHubControlCurrent;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] bRemoveAndPowerMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct USB_HUB_INFORMATION
    {
        public USB_HUB_DESCRIPTOR HubDescriptor;

        [MarshalAs(UnmanagedType.U1)]
        public bool HubIsBusPowered;
    }

    private enum USB_HUB_NODE : int
    {
        UsbHub = 0,
        UsbMIParent = 1
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct USB_MI_PARENT_INFORMATION
    {
        public uint NumberOfInterfaces;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct USB_NODE_INFORMATION_UNION
    {
        [FieldOffset(0)]
        public USB_HUB_INFORMATION HubInformation;

        [FieldOffset(0)]
        public USB_MI_PARENT_INFORMATION MiParentInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct USB_NODE_INFORMATION
    {
        public USB_HUB_NODE NodeType;
        public USB_NODE_INFORMATION_UNION HubInformationUnion;

        public USB_HUB_INFORMATION HubInformation
        {
            readonly get => HubInformationUnion.HubInformation;
            set => HubInformationUnion.HubInformation = value;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct USB_DEVICE_DESCRIPTOR
    {
        public byte bLength;
        public byte bDescriptorType;
        public ushort bcdUSB;
        public byte bDeviceClass;
        public byte bDeviceSubClass;
        public byte bDeviceProtocol;
        public byte bMaxPacketSize0;
        public ushort idVendor;
        public ushort idProduct;
        public ushort bcdDevice;
        public byte iManufacturer;
        public byte iProduct;
        public byte iSerialNumber;
        public byte bNumConfigurations;
    }

    private enum USB_CONNECTION_STATUS : int
    {
        NoDeviceConnected = 0,
        DeviceConnected = 1,
        DeviceFailedEnumeration = 2,
        DeviceGeneralFailure = 3,
        DeviceCausedOvercurrent = 4,
        DeviceNotEnoughPower = 5,
        DeviceNotEnoughBandwidth = 6,
        DeviceHubNestedTooDeeply = 7,
        DeviceInLegacyHub = 8,
        DeviceEnumerating = 9,
        DeviceReset = 10
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct USB_NODE_CONNECTION_INFORMATION_EX
    {
        public uint ConnectionIndex;
        public USB_DEVICE_DESCRIPTOR DeviceDescriptor;
        public byte CurrentConfigurationValue;
        public byte Speed;

        [MarshalAs(UnmanagedType.U1)]
        public bool DeviceIsHub;

        public ushort DeviceAddress;
        public uint NumberOfOpenPipes;
        public USB_CONNECTION_STATUS ConnectionStatus;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        int nInBufferSize,
        [Out] byte[] lpOutBuffer,
        int nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        ref USB_NODE_INFORMATION lpInBuffer,
        int nInBufferSize,
        ref USB_NODE_INFORMATION lpOutBuffer,
        int nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        ref USB_NODE_CONNECTION_INFORMATION_EX lpInBuffer,
        int nInBufferSize,
        ref USB_NODE_CONNECTION_INFORMATION_EX lpOutBuffer,
        int nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        ref USBPCAP_IOCTL_SIZE lpInBuffer,
        int nInBufferSize,
        IntPtr lpOutBuffer,
        int nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        ref USBPCAP_ADDRESS_FILTER lpInBuffer,
        int nInBufferSize,
        IntPtr lpOutBuffer,
        int nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        int nInBufferSize,
        IntPtr lpOutBuffer,
        int nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);
    #endregion
}