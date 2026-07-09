using Base.Core;
using Base.Framework.Utilities;
using Base.Helpers;
using Base.Pages;
using Base.Services;
using Base.Services.Peripheral;
using KeyboardHallSensor;
using System.Windows.Controls;
using System.Windows.Threading;

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
    [Config(key: "總共Frame數量", Description = "Follow Device XY鍵位表", Min = 1, Type = ConfigType.Auto, Header = "3-1 Set Advance Effect (Layer) - Mode", Changed = nameof(FrameCountChanged))]
    private readonly short frameCount = 0x72;

    [Config(key: "X數量", Description = "Follow Device XY鍵位表", Min = 1, Type = ConfigType.Hex)]
    private readonly byte xCount = 0x13;

    [Config(key: "Y數量", Description = "Follow Device XY鍵位表", Min = 1, Type = ConfigType.Hex)]
    private readonly byte yCount = 0x06;

    [Config(key: "Key數量", Min = 1, Header = "3-2 Set Advance Effect (Layer) - Data")]
    private readonly byte keyCount = 0x72;

    [Config(key: "速度", Min = 0x00, Max = 0xfe, Header = "3-3 Set Advance Effect (Reactive)", Type = ConfigType.Hex)]
    private readonly byte reactiveSpeed = 0x30;

    [Config(key: "亮度", Min = 1, Max = 100, Type = ConfigType.Slider)]
    private readonly byte reactiveBrightness = 100;

    [Config(key: "亮度", Min = 1, Max = 100, Header = "3-4 Set Advance Effect (Layer) - Brightness", Type = ConfigType.Slider)]
    private readonly byte brightness = 50;

    private readonly int keysPerPacket = 0x13;

    private PeripheralInterface ActiveInterface;

    //- Keyboard UI
    private readonly Dictionary<string, KeyDisplayRendered> keyDisplayByLayoutKey = new(StringComparer.Ordinal);
    public List<FrameData> frameDatas = new();
    private FrameData.ColorStrategy currentStrategy;

    //- Animation Player
    private DispatcherTimer animationTimer;
    private bool repeatAll = true;

    public LightingEffectPage()
    {
        InitializeComponent();

        TimelineSlider.ValueChanged += TimelineSlider_ValueChanged;
        TimelineSlider.Maximum = frameCount;

        animationTimer = new DispatcherTimer();
        animationTimer.Interval = TimeSpan.FromMilliseconds(1000 / 40);
        animationTimer.Tick += AnimationTimer_Tick;

        PlayButton.Click += PlayButton_Click;
        PauseButton.Click += PauseButton_Click;
        PreviousButton.Click += PreviousButton_Click;
        NextButton.Click += NextButton_Click;
        RepeatAllButton.Click += RepeatAllButton_Click; ;
        RepeatOffButton.Click += RepeatOffButton_Click;
    }

    private void RepeatOffButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        SetRepeat(false);
    }
    private void RepeatAllButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        SetRepeat(true);
    }

    public void SetRepeat(bool repeat)
    {
        if (repeat)
        {
            RepeatAllButton.Visibility = System.Windows.Visibility.Collapsed;
            RepeatOffButton.Visibility = System.Windows.Visibility.Visible;
        }
        else
        {
            RepeatAllButton.Visibility = System.Windows.Visibility.Visible;
            RepeatOffButton.Visibility = System.Windows.Visibility.Collapsed;
        }
        repeatAll = repeat;
    }

    private void NextButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        PauseAnimation();
        int currentFrame = (int)TimelineSlider.Value;
        currentFrame++;
        if (currentFrame > frameCount)
        {
            currentFrame = 1;
        }
        TimelineSlider.Value = currentFrame;
        ShowFrame(currentFrame - 1);
    }

    private void PreviousButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {   
        PauseAnimation();
        int currentFrame = (int)TimelineSlider.Value;
        currentFrame--;
        if (currentFrame < 1)
        {
            currentFrame = frameCount;
        }
        TimelineSlider.Value = currentFrame;
        ShowFrame(currentFrame - 1);
    }

    private void PauseButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        PauseAnimation();
        PlayButton.Visibility = System.Windows.Visibility.Visible;
        PauseButton.Visibility = System.Windows.Visibility.Collapsed;
    }

    private void PlayButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        StartAnimation();
        PlayButton.Visibility = System.Windows.Visibility.Collapsed;
        PauseButton.Visibility = System.Windows.Visibility.Visible;
    }

    private void FrameCountChanged()
    {
        TimelineSlider.Maximum = frameCount;

        string[] layoutKeys = keyDisplayByLayoutKey.Keys.ToArray();

        for (short i = (short)frameDatas.Count; i < frameCount; i++)
        {
            FrameData frameData = new(i);
            frameData.SetAllKeyColors(layoutKeys, currentStrategy, M708MatrixLightKeyTable, xCount);
            frameDatas.Add(frameData);
        }
    }

    private void TimelineSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        int displayFrameNumber = (int)e.NewValue;
        ShowFrame(displayFrameNumber - 1);
        CurrentFrameText.Text = displayFrameNumber.ToString();
    }

    #region WPF Behavious

    public override void Awake()
    {
        base.Awake();
        DeviceSelection.Instance.OnActiveDeviceConnected += ConnectToDevice;
        DeviceSelection.Instance.OnActiveDeviceDisconnected += OnActiveDeviceDisconnected;

        BuildKeyboard();

        for (short i = 0; i < frameCount; i++)
        {
            frameDatas.Add(new(i));
        }

        currentStrategy = FrameScan;
        string[] layoutKeys = keyDisplayByLayoutKey.Keys.ToArray();
        SetAllFramesColor(layoutKeys, currentStrategy);
        ShowFrame(0);
        StartAnimation();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        if (ActiveInterface == null && DeviceSelection.Instance.ActiveDevice != null)
        {
            ConnectToDevice();
        }
    }

    #endregion

    #region Device Connection

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

    #endregion

    #region Page building

    private void BuildKeyboard()
    {
        var keyDefs = LayoutConverter.Convert("M708.txt");
        const float unit = 50f;
        float maxX = 0, maxY = 0;

        foreach (var def in keyDefs)
        {
            float px = def.X * unit;
            float py = def.Y * unit;
            float pw = def.W * unit;
            float ph = def.H * unit;

            var display = new KeyDisplayRendered(0, pw, ph, def.Label);
            Canvas.SetLeft(display, px);
            Canvas.SetTop(display, py);
            KeyboardCanvas.Children.Add(display);

            keyDisplayByLayoutKey[def.Label] = display;

            maxX = Math.Max(maxX, px + pw);
            maxY = Math.Max(maxY, py + ph);
        }

        KeyboardCanvas.Width = maxX + 8;
        KeyboardCanvas.Height = maxY + 8;
    }

    #endregion

    #region Animation Control

    private void StartAnimation()
    {
        if (animationTimer == null)
        {
            animationTimer = new DispatcherTimer();
            animationTimer.Interval = TimeSpan.FromMilliseconds(1000 / 40); // 40 FPS
            animationTimer.Tick += AnimationTimer_Tick;
        }
        animationTimer.Start();
    }

    private void PauseAnimation()
    {
        animationTimer?.Stop();
    }

    private void AnimationTimer_Tick(object sender, EventArgs e)
    {
        int currentFrame = (int)TimelineSlider.Value;
        currentFrame++;
        if (repeatAll && currentFrame > frameCount)
        {
            currentFrame = 1;
        }
        TimelineSlider.Value = currentFrame;

        ShowFrame(currentFrame - 1);
    }

    #endregion

    #region Lighting Control

    public void ShowFrame(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= frameDatas.Count)
            return;
        FrameData frameData = frameDatas[frameIndex];
        foreach (var kvp in keyDisplayByLayoutKey)
        {
            string layoutKey = kvp.Key;
            KeyDisplayRendered display = kvp.Value;
            string matrixKey = ConvertToMatrixKey(layoutKey);
            Vector2Int vector = GetKeyPositionFromMatrixKey(M708MatrixLightKeyTable, matrixKey);
            int globalKeyIndex = MatrixTransform(vector.x, vector.y, xCount);
            byte[] rgb = frameData.GetKeyColor(globalKeyIndex);
            display.SetColor(rgb[0], rgb[1], rgb[2]);
        }
    }

    public void SetAllFramesColor(string[] layoutKeys, FrameData.ColorStrategy colorStrategy)
    {
        foreach (var frameData in frameDatas)
        {
            frameData.SetAllKeyColors(layoutKeys, colorStrategy, M708MatrixLightKeyTable, xCount);
        }
    }

    public class FrameData
    {
        public readonly short frameIndex;
        public Dictionary<int, byte[]> KeyColors { get; set; } = new();

        public delegate byte[] ColorStrategy(short frameIndex, int globalKeyIndex);

        public FrameData(short index)
        {
            this.frameIndex = index;
        }

        public void SetAllKeyColors(string[] layoutKeys, ColorStrategy colorStrategy, string[,] matrix, byte xCount)
        {
            for (int i = 0; i < layoutKeys.Length; i++)
            {
                string matrixKey = ConvertToMatrixKey(layoutKeys[i]);
                Vector2Int vector = GetKeyPositionFromMatrixKey(matrix, matrixKey);
                int globalKeyIndex = MatrixTransform(vector.x, vector.y, xCount);
                SetKeyColor(globalKeyIndex, colorStrategy);
            }
        }

        public void SetKeyColor(int globalKeyIndex, ColorStrategy colorStrategy)
        {
            byte[] rgb = colorStrategy(frameIndex, globalKeyIndex);
            KeyColors[globalKeyIndex] = rgb;
        }

        public byte[] GetKeyColor(int globalKeyIndex)
        {
            return KeyColors.TryGetValue(globalKeyIndex, out var rgb) ? rgb : new byte[] { 0x00, 0x00, 0x00 };
        }
    }

    #endregion

    #region Construct Packets

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

    #endregion

    #region Color Strategy

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

        Vector2Int tensC = tensMap[tens];
        Vector2Int hundredsC = hundredsMap[hundreds];
        Vector2Int thousandsC = thousandsMap[thousands];
        Vector2Int tenthousandsC = tenthousandsMap[tenthousands];

        int[] candidates =
        [
            MatrixTransform(tensC.x, tensC.y, xCount),
            MatrixTransform(hundredsC.x, hundredsC.y, xCount),
            MatrixTransform(thousandsC.x, thousandsC.y, xCount),
            MatrixTransform(tenthousandsC.x, tenthousandsC.y, xCount),
        ];
        return candidates;
    }

    private readonly Vector2Int[] tensMap = [new(3, 1), new(4, 1), new(5, 1), new(6, 1), new(7, 1), new(8, 1), new(9, 1), new(10, 1), new(11, 1), new(12, 1)];
    private readonly Vector2Int[] hundredsMap = [new(3, 2), new(4, 2), new(5, 2), new(6, 2), new(7, 2), new(8, 2), new(9, 2), new(10, 2), new(11, 2), new(12, 2)];
    private readonly Vector2Int[] thousandsMap = [new(3, 3), new(4, 3), new(5, 3), new(6, 3), new(7, 3), new(8, 3), new(9, 3), new(10, 3), new(11, 3), new(12, 3)];
    private readonly Vector2Int[] tenthousandsMap = [new(4, 4), new(5, 4), new(6, 4), new(7, 4), new(8, 4), new(9, 4), new(10, 4), new(11, 4), new(12, 4), new(13, 4)];

    #endregion

    #region Matrix Conversion

    private static int MatrixTransform(int x, int y, byte xCount)
    {
        return (y * xCount) + x;
    }

    private static Vector2Int InverseMatrixTransform(int globalKeyIndex, byte xCount)
    {
        int y = globalKeyIndex / xCount;
        int x = globalKeyIndex % xCount;
        return new(x, y);
    }

    private static Vector2Int GetKeyPositionFromMatrixKey(string[,] matrixTable, string matrixKey)
    {
        for (int y = 0; y < matrixTable.GetLength(1); y++)
        {
            for (int x = 0; x < matrixTable.GetLength(0); x++)
            {
                // Matrix is horizontal y and vertical x.
                if (matrixTable[x, y] == matrixKey)
                {
                    return new(x, y);
                }
            }
        }
        return new(-1, -1);
    }

    // Matrix is horizontal y and vertical x.
    private readonly string[,] M708MatrixLightKeyTable =
    {
        {"L_BAR1",  "L_BAR2",   "L_BAR3",       "L_BAR4",       "L_BAR5",       "L_BAR6"},
        {"L_BAR7",  "L_BAR8",   "L_BAR9",       "",             "",             ""},
        {"ESC",     "TILDE",    "TAB",          "CAP",          "L_SHIFT",      "L_CTRL"},
        {"F1",      "1",        "Q",            "A",            "CODE45(EU)",   "L_WIN"},
        {"F2",      "2",        "W",            "S",            "Z",            "L_ALT"},
        {"F3",      "3",        "E",            "D",            "X",            "SPACE1"},
        {"F4",      "4",        "R",            "F",            "C",            "SPACE2"},
        {"F5",      "5",        "T",            "G",            "V",            "SPACE3"},
        {"F6",      "6",        "Y",            "H",            "B",            "SPACE4"},
        {"F7",      "7",        "U",            "J",            "N",            "SPACE5"},
        {"F8",      "8",        "I",            "K",            "M",            ""},
        {"F9",      "9",        "O",            "L",            "COMMA",        "R_ALT"},
        {"F10",     "0",        "P",            "SEMICOLON",    "DOT",          "FN"},
        {"F11",     "MINUS",    "L_BRACKETS",   "APOSTROPHE",   "SLASH",        "'R_CTRL"},
        {"F12",     "EQUAL",    "R_BRACKETS",   "CODE42(EU)",   "'R_SHIFT",     "L_ARROW"},
        {"",        "BACKSPACE","BACKSLASH",    "ENTER",        "UP_ARROW",     "DN_ARROW"},
        {"",        "INSERT",   "DEL",          "PGUP",         "PGDN",         "R_ARROW"},
        {"R_BAR1",  "R_BAR2",   "R_BAR3",       "R_BAR4",       "R_BAR5",       "R_BAR6"},
        {"R_BAR7",  "R_BAR8",   "R_BAR9",       "",             "",             ""}
    };

    private static readonly Dictionary<string, string> LayoutToMatrix = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Esc"] = "ESC",
        ["~\\n`"] = "TILDE",
        ["Tab"] = "TAB",
        ["Caps Lock"] = "CAP",
        ["L-Shift"] = "L_SHIFT",
        ["L-Ctrl"] = "L_CTRL",
        ["F1"] = "F1",
        ["F2"] = "F2",
        ["F3"] = "F3",
        ["F4"] = "F4",
        ["F5"] = "F5",
        ["F6"] = "F6",
        ["F7"] = "F7",
        ["F8"] = "F8",
        ["F9"] = "F9",
        ["F10"] = "F10",
        ["F11"] = "F11",
        ["F12"] = "F12",

        ["!\\n1"] = "1",
        ["@\\n2"] = "2",
        ["#\\n3"] = "3",
        ["$\\n4"] = "4",
        ["%\\n5"] = "5",
        ["^\\n6"] = "6",
        ["&\\n7"] = "7",
        ["*\\n8"] = "8",
        ["(\\n9"] = "9",
        [")\\n0"] = "0",

        ["Q"] = "Q",
        ["W"] = "W",
        ["E"] = "E",
        ["R"] = "R",
        ["T"] = "T",
        ["Y"] = "Y",
        ["U"] = "U",
        ["I"] = "I",
        ["O"] = "O",
        ["P"] = "P",

        ["A"] = "A",
        ["S"] = "S",
        ["D"] = "D",
        ["F"] = "F",
        ["G"] = "G",
        ["H"] = "H",
        ["J"] = "J",
        ["K"] = "K",
        ["L"] = "L",

        ["Z"] = "Z",
        ["X"] = "X",
        ["C"] = "C",
        ["V"] = "V",
        ["B"] = "B",
        ["N"] = "N",
        ["M"] = "M",

        ["_\\n-"] = "MINUS",
        ["+\\n="] = "EQUAL",
        ["{\\n["] = "L_BRACKETS",
        ["}\\n]"] = "R_BRACKETS",
        ["|\\n\\\\"] = "BACKSLASH",
        [":\\n;"] = "SEMICOLON",
        ["\\\"\\n'"] = "APOSTROPHE",
        ["<\\n,"] = "COMMA",
        [">\\n."] = "DOT",
        ["?\\n/"] = "SLASH",

        ["\\\\\\n|"] = "CODE45(EU)",
        ["#\\n~"] = "CODE42(EU)",

        ["bksp"] = "BACKSPACE",
        ["Enter"] = "ENTER",
        ["Insert"] = "INSERT",
        ["Delete"] = "DEL",
        ["PgUp"] = "PGUP",
        ["PgDn"] = "PGDN",

        ["L-Win"] = "L_WIN",
        ["L-Alt"] = "L_ALT",
        ["R-Alt"] = "R_ALT",
        ["Fn"] = "FN",
        ["R-Ctrl"] = "R_CTRL",
        ["R-Shift"] = "R_SHIFT",

        ["↑"] = "UP_ARROW",
        ["↓"] = "DN_ARROW",
        ["←"] = "L_ARROW",
        ["→"] = "R_ARROW",

        ["Space"] = "SPACE1",
        [""] = "SPACE1"
    };

    public static string ConvertToMatrixKey(string layoutKey)
    {
        return layoutKey == null
            ? throw new ArgumentNullException(nameof(layoutKey))
            : LayoutToMatrix.TryGetValue(layoutKey.Trim(), out string matrixKey)
            ? matrixKey
            :
        throw new KeyNotFoundException($"Unknown layout key: '{layoutKey}'.");
    }

    public static bool TryConvertToMatrixKey(string layoutKey, out string matrixKey)
    {
        matrixKey = string.Empty;

        return layoutKey == null ? false : LayoutToMatrix.TryGetValue(layoutKey.Trim(), out matrixKey);
    }

    #endregion
}