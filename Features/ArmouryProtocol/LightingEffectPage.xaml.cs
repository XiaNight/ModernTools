using Base.Core;
using Base.Helpers;
using Base.Pages;
using Base.Services;
using Base.Services.Peripheral;

namespace ArmouryProtocol;

/*
 * 3-1 Set Advance Effect (Layer) - Mode
 * C1 00 00 00 0A 00 13 06
 * 
 * 3-2 Set Advance Effect (Layer) - Dat
 * C1 01 00 00 72 53 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00
 * C1 01 00 00 5F 13 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00
 * C1 01 00 00 4C 13 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00
 * C1 01 00 00 39 13 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00
 * C1 01 00 00 26 13 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00
 * C1 01 00 00 13 93 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00 FF 00 00

 * 3-3 Get Advance Effect (Reactive) - Setting
 * C1 02 03 00 0F 32 01 FF FF 00 00 00 00 00 00 00 00 00 00 00

 * 3-3 Get Advance Effect (Ripple ) - Setting
 * C1 02 05 02 64 32 01 FF 01 01 64 00 00 00 00 00 00 00 00
 * C1 02 05 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00
 * C1 02 05 00 00 00 00 00 00 00 00 00 00

 * 3-4 Set Advance Effect - Brightness
 * C1 03 00 00 32

 * 3-5 Set Advance Effect (Layer) - Apply
 * C1 04 00 00 8E 71 00 00 00 00 00 00
 */

[PageInfo("Advanced Lighting", Path = ["Keyboard", "Armoury"])]
public partial class LightingEffectPage : PageBase
{
    [Config(key: "總共Frame數量", Description = "Follow Device XY鍵位表", Min = 1, Type = ConfigType.Auto, Header = "3-1 Set Advance Effect (Layer) - Mode")]
    private short frameCount = 0x72;

    [Config(key: "X數量", Description = "Follow Device XY鍵位表", Min = 1, Type = ConfigType.Hex)]
    private byte xCount = 0x13;

    [Config(key: "Y數量", Description = "Follow Device XY鍵位表", Min = 1, Type = ConfigType.Hex)]
    private byte yCount = 0x06;

    [Config(key: "Key數量", Min = 1, Header = "3-2 Set Advance Effect (Layer) - Data")]
    private byte keyCount = 0x72;
    
    [Config(key: "速度", Min = 0x00, Max = 0xfe, Header = "3-3 Set Advance Effect (Reactive)", Type = ConfigType.Hex)]
    private byte reactiveSpeed = 0x30;

    [Config(key: "亮度", Min = 1, Max = 100, Type = ConfigType.Slider)]
    private byte reactiveBrightness = 100;

    [Config(key: "亮度", Min = 1, Max = 100, Header = "3-4 Set Advance Effect (Layer) - Brightness", Type =ConfigType.Slider)]
    private byte brightness = 50;

    private readonly int keysPerPacket = 0x13;

    private PeripheralInterface ActiveInterface;

    public LightingEffectPage()
    {
        InitializeComponent();
    }

    public override void Awake()
    {
        base.Awake();
        DeviceSelection.Instance.OnActiveDeviceConnected += ConnectToDevice;
        DeviceSelection.Instance.OnActiveDeviceDisconnected += OnActiveDeviceDisconnected;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        if(ActiveInterface == null && DeviceSelection.Instance.ActiveDevice != null)
        {
            ConnectToDevice();
        }
    }

    private void ConnectToDevice()
    {

        var device = DeviceSelection.Instance.ActiveDevice;
        if (device == null) return;
        try
        {
            var usagePage = device.PID == 0x1ACE ? 0xFF02 : 0xFF00;
            if (device.interfaces.Count == 0) return;

            var deviceInterface = device.interfaces.FirstOrDefault(@interface =>
                (@interface.UsagePage == usagePage) && (@interface.Usage == 1),
                device.interfaces[0]
            );
            if (deviceInterface == null) return;

            ActiveInterface = deviceInterface.Connect(true);
        }
        catch (Exception ex)
        {
            Debug.Log("[Keyboard] Failed to open HID device: " + ex.Message);
            return;
        }
    }

    private void OnActiveDeviceDisconnected()
    {
        ActiveInterface = null;
    }

    [AppMenuItem("Send All Packets")]
    private List<byte[]> SendAll()
    {
        List<byte[]> bytes = new();

        int checksum = 0;
        byte[] initialPacket = Construct3_1Mode();
        bytes.AddRange(Construct3_2Data(ref checksum));
        bytes.AddRange(Construct3_3Reactive());
        byte[] brightnessPacket = Construct3_4Brightness();
        byte[] applyPacket = Construct3_5Apply(checksum);

        ProtocolService.AppendCmdTimeout(ActiveInterface, initialPacket, true, 5000);


        for (int packetId = 0; packetId < bytes.Count; packetId++)
        {
            byte[] packet = bytes[packetId];
            ProtocolService.AppendCmd(ActiveInterface, packet, packetId % 6 == 5);
        }

        ProtocolService.AppendCmdTimeout(ActiveInterface, brightnessPacket, true, 5000);

        ProtocolService.AppendCmdTimeout(ActiveInterface, applyPacket, true, 5000);


        return bytes;
    }

    private byte[] Construct3_1Mode()
    {
        byte frameCountLowByte = (byte)(frameCount & 0xFF);
        byte frameCountHighByte = (byte)((frameCount >> 8) & 0xFF);
        byte[] data = [0xC1, 0x00, 0x00, 0x00, frameCountLowByte, frameCountHighByte, xCount, yCount];
        return data;
    }

    private List<byte[]> Construct3_2Data(ref int checksum)
    {
        List<byte[]> dataList = new();

        for (short currentFrameIndex = 0; currentFrameIndex < frameCount; currentFrameIndex++)
        {
            dataList.AddRange(Construct3_2SingleFrame(currentFrameIndex, ref checksum));
        }

        return dataList;
    }

    private List<byte[]> Construct3_2SingleFrame(short currentFrameIndex, ref int checksum)
    {
        byte remainingKeys = keyCount;
        short requiredPackets = (short)Math.Ceiling((double)keyCount / keysPerPacket);

        List<byte[]> frameDataList = new(requiredPackets);

        for (short packetIndex = 0; packetIndex < requiredPackets; packetIndex++)
        {
            byte packetState = packetIndex switch
            {
                0 => 0b01, // First packet
                _ when packetIndex == requiredPackets - 1 => 0b10, // Last packet
                _ => 0b00, // Middle packets
            };

            byte[] data = Construct3_2SinglePacket(currentFrameIndex, packetState, packetIndex, remainingKeys, ref checksum);
            frameDataList.Add(data);
            remainingKeys -= (byte)Math.Min(remainingKeys, keysPerPacket);
        }

        return frameDataList;
    }

    private byte[] Construct3_2SinglePacket(short currentFrameIndex, in byte packetState, in int packetIndex, in byte remainingKeys, ref int checksum)
    {
        int headerSize = 6;

        byte keysInThisPacket = (byte)Math.Min(remainingKeys, keysPerPacket);
        
        byte[] data = new byte[headerSize + keysInThisPacket * 3];
        data[0] = 0xC1;
        data[1] = 0x01;
        data[2] = currentFrameIndex.LowByte();
        data[3] = currentFrameIndex.HighByte();
        data[4] = remainingKeys;
        data[5] = (byte)((packetState << 6) | (keysInThisPacket & 0x3F));
        for (int j = 0; j < keysInThisPacket; j++)
        {
            int keyIndex = (packetIndex * keysPerPacket) + j;
            if (keyIndex >= keyCount)
                break;
            byte[] rgb = GetKeyRGB(currentFrameIndex, (byte)packetIndex, (byte)j);
            data[headerSize + j * 3] = rgb[0];     // R
            data[headerSize + j * 3 + 1] = rgb[1]; // G
            data[headerSize + j * 3 + 2] = rgb[2]; // B
            checksum += rgb[0] + rgb[1] + rgb[2];
        }

        return data;
    }

    /// <param name="frameIndex">Current frame index in all frames.</param>
    /// <param name="packetIndex">Current packet index in a single frame.</param>
    /// <param name="keyIndex">Current key index in a single packet.</param>
    /// <returns></returns>
    private byte[] GetKeyRGB(short frameIndex, byte packetIndex, byte keyIndex)
    {
        int keyGlobalIndex = (packetIndex * keysPerPacket) + keyIndex;

        return FrameScan(frameIndex, keyGlobalIndex);
    }

    private byte[] KeyScan(short frameIndex, int keyGlobalIndex)
    {
        bool isFrameMatchKeyIndex = keyGlobalIndex == frameIndex; // Example condition, adjust as needed
        return isFrameMatchKeyIndex ? [0xFF, 0x00, 0x00] : [0x00, 0x00, 0x00];
    }

    private byte[] FrameScan(short frameIndex, int keyGlobalIndex)
    {
        bool contains = false;
        foreach (var candidate in FrameIndicatorCandidates(frameIndex))
        {
            if (candidate == keyGlobalIndex)
            {
                contains = true;
                break;
            }
        }

        return contains ? [0xFF, 0xFF, 0xFF] : [0x00, 0x00, 0x00];
    }

    private int[] FrameIndicatorCandidates(short frameIndex)
    {
        int tens = frameIndex % 10;
        int hundreds = (frameIndex / 10) % 10;
        int thousands = (frameIndex / 100) % 10;
        int tenthousands = (frameIndex / 1000) % 10;

        (int, int) tensC = tensMap[tens];
        (int, int) hundredsC = hundredsMap[hundreds];
        (int, int) thousandsC = thousandsMap[thousands];
        (int, int) tenthousandsC = tenthousandsMap[tenthousands];

        int[] candidates = new int[4];
        candidates[0] = MatrixConverter(tensC.Item1, tensC.Item2);
        candidates[1] = MatrixConverter(hundredsC.Item1, hundredsC.Item2);
        candidates[2] = MatrixConverter(thousandsC.Item1, thousandsC.Item2);
        candidates[3] = MatrixConverter(tenthousandsC.Item1, tenthousandsC.Item2);

        return candidates;
    }

    private int MatrixConverter(int x, int y)
    {
        return (y * xCount) + x;
    }

    private (int, int)[] tensMap = [(3, 1), (4, 1), (5, 1), (6, 1), (7, 1), (8, 1), (9, 1), (10, 1), (11, 1), (12, 1)];
    private (int, int)[] hundredsMap = [(3, 2), (4, 2), (5, 2), (6, 2), (7, 2), (8, 2), (9, 2), (10, 2), (11, 2), (12, 2)];
    private (int, int)[] thousandsMap = [(3, 3), (4, 3), (5, 3), (6, 3), (7, 3), (8, 3), (9, 3), (10, 3), (11, 3), (12, 3)];
    private (int, int)[] tenthousandsMap = [(4, 4), (5, 4), (6, 4), (7, 4), (8, 4), (9, 4), (10, 4), (11, 4), (12, 4), (13, 4)];

    private List<byte[]> Construct3_3Reactive()
    {
        List<byte[]> dataList = new();
        byte[] data = [0xC1, 0x02, 0x03, 0x00, reactiveSpeed, reactiveBrightness, 0x01, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        dataList.Add(data);
        return dataList;
    }

    private byte[] Construct3_4Brightness()
    {
        return [0xC1, 0x03, 0x00, 0x00, brightness];
    }

    private byte[] Construct3_5Apply(int checksum)
    {
        byte b0 = (byte)(checksum & 0xFF);
        byte b1 = (byte)((checksum >> 8) & 0xFF);
        byte b2 = (byte)((checksum >> 16) & 0xFF);
        byte b3 = (byte)((checksum >> 24) & 0xFF);

        return [0xC1, 0x04, 0x00, 0x00, b0, b1, b2, b3, 0x00, 0x00, 0x00];
    }
}
