using Base.Core;
using Base.Services;
using Base.Services.Peripheral;

namespace CommonProtocol;

public class CommonProtocol : WpfBehaviourSingleton<CommonProtocol>
{
    private PeripheralInterface activeInterface;

    private CmdParser parser;
    public override void Awake()
    {
        base.Awake();

        DeviceSelection.Instance.OnActiveDeviceConnected += ConnectToInterface;
        DeviceSelection.Instance.OnActiveDeviceDisconnected += DisconnectInterface;
    }

    public override void Start()
    {
        base.Start();
        parser = new CmdParser();

        Listener ttLog = parser.AddTrigger([0xFD, 0xA0], TTLog);

        Listener deviceInfo = parser.AddTrigger([0x12, 0x00], DeviceInfo);
    }

    private void ConnectToInterface()
    {
        var device = DeviceSelection.Instance.ActiveDevice;
        try
        {
            var usagePage = device.PID == 0x1ACE ? 0xFF02 : 0xFF00;
            if (device.interfaces.Count == 0) return;

            var deviceInterface = device.interfaces.FirstOrDefault(@interface =>
                (@interface.UsagePage == usagePage) && (@interface.Usage == 1),
                device.interfaces[0]
            );
            if (deviceInterface == null) return;

            activeInterface = deviceInterface.Connect(true);
            activeInterface.OnDataReceived += parser.Parse;
        }
        catch (Exception ex)
        {
            Debug.Log("[CommonProtocol] Failed to open HID device: " + ex.Message);
            return;
        }
    }

    private void DisconnectInterface()
    {
        if (activeInterface == null) return;
        if (!activeInterface.IsDeviceConnected) return;
        activeInterface.OnDataReceived -= parser.Parse;

        activeInterface = null;
    }

        private void TTLog(Listener listener, ReadOnlyMemory<byte> bytes, DateTime time)
        {
            ReadOnlySpan<byte> data = bytes.Span.Slice(5);

        int length = data.IndexOf((byte)0);
        if (length < 0)
        {
            length = data.Length;
        }
        if (length == 0) return;

        string message = System.Text.Encoding.ASCII.GetString(data.Slice(0, length));
        Debug.Log(message);
    }

    private void DeviceInfo(Listener listener, ReadOnlyMemory<byte> bytes, DateTime time)
    {
        ReadOnlySpan<byte> data = bytes.Span;
        if (data.Length < 10) return;
        string deviceName = System.Text.Encoding.ASCII.GetString(data.Slice(5, 10).TrimEnd((byte)0));
        Debug.Log($"Device Name: {deviceName}");
    }

    private class CmdParser
    {
        public readonly List<Listener> triggers = new();
        private readonly Dictionary<string, Listener.IData> structure = new();
        public void Parse(ReadOnlyMemory<byte> data, DateTime time)
        {
            foreach (var trigger in triggers)
            {
                trigger.Match(data, time);
            }
        }

        public Listener AddTrigger(byte[] pattern, Action<Listener, ReadOnlyMemory<byte>, DateTime> onTriggered)
        {
            Listener trigger = new(pattern);
            trigger.OnTriggered += onTriggered;
            triggers.Add(trigger);
            return trigger;
        }
    }
}