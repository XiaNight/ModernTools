using ArmouryProtocol.Lighting;
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

    private const int Fps = 40;

    private PeripheralInterface ActiveInterface;

    // Per-model lighting tables (layout file, matrix, layout->matrix map).
    // Fixed to M708 for now; swap this out when model switching is added.
    private readonly KeyboardLightingProfile lightingProfile = KeyboardLightingProfiles.Default;

    //- Keyboard UI
    private readonly Dictionary<string, KeyDisplayRendered> keyDisplayByLayoutKey = new(StringComparer.Ordinal);
    // Precomputed once in BuildKeyboard: each rendered key paired with its static global key index.
    private readonly List<(KeyDisplayRendered display, int globalKeyIndex)> renderedKeys = new();
    private int[] uiGlobalIndices = Array.Empty<int>();
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

        // Render priority keeps ticks in step with the render loop instead of
        // being preempted by lower-priority (Background) dispatcher work.
        animationTimer = new DispatcherTimer(DispatcherPriority.Render);
        animationTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / Fps);
        animationTimer.Tick += AnimationTimer_Tick;

        PlayButton.Click += PlayButton_Click;
        PauseButton.Click += PauseButton_Click;
        PreviousButton.Click += PreviousButton_Click;
        NextButton.Click += NextButton_Click;
        RepeatAllButton.Click += RepeatAllButton_Click;
        RepeatOffButton.Click += RepeatOffButton_Click;

        // Sync the repeat toggle icon with the initial repeat state.
        SetRepeat(repeatAll);
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
        ShowPlayButton();
        int currentFrame = (int)TimelineSlider.Value;
        currentFrame++;
        if (currentFrame > frameCount)
        {
            currentFrame = 1;
        }
        // Setting Value raises TimelineSlider_ValueChanged, which renders the frame.
        TimelineSlider.Value = currentFrame;
    }

    private void PreviousButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        PauseAnimation();
        ShowPlayButton();
        int currentFrame = (int)TimelineSlider.Value;
        currentFrame--;
        if (currentFrame < 1)
        {
            currentFrame = frameCount;
        }
        // Setting Value raises TimelineSlider_ValueChanged, which renders the frame.
        TimelineSlider.Value = currentFrame;
    }

    private void PauseButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        PauseAnimation();
        ShowPlayButton();
    }

    private void ShowPlayButton()
    {
        PlayButton.Visibility = System.Windows.Visibility.Visible;
        PauseButton.Visibility = System.Windows.Visibility.Collapsed;
    }

    private void ShowPauseButton()
    {
        PlayButton.Visibility = System.Windows.Visibility.Collapsed;
        PauseButton.Visibility = System.Windows.Visibility.Visible;
    }

    private void PlayButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        StartAnimation();
        ShowPauseButton();
    }

    private void FrameCountChanged()
    {
        TimelineSlider.Maximum = frameCount;

        for (short i = (short)frameDatas.Count; i < frameCount; i++)
        {
            FrameData frameData = new(i);
            frameData.SetAllKeyColors(uiGlobalIndices, currentStrategy);
            frameDatas.Add(frameData);
        }

        // Drop stale frames if the count was lowered.
        if (frameDatas.Count > frameCount)
        {
            frameDatas.RemoveRange(frameCount, frameDatas.Count - frameCount);
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
        SetAllFramesColor(uiGlobalIndices, currentStrategy);
        ShowFrame(0);
        StartAnimation();
        ShowPauseButton();
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
        var keyDefs = LayoutConverter.Convert(lightingProfile.LayoutFileName);
        const float unit = 50f;
        float maxX = 0, maxY = 0;

        renderedKeys.Clear();
        var indices = new List<int>();

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

            // Resolve the static layout-key -> global-index mapping once, up front.
            // Keys that don't map (unknown label / not present in the matrix) are
            // still shown but simply won't be driven by the animation.
            if (TryGetGlobalKeyIndex(def.Label, out int globalKeyIndex))
            {
                renderedKeys.Add((display, globalKeyIndex));
                indices.Add(globalKeyIndex);
            }
            else
            {
                Debug.Log($"[Keyboard] Unmapped layout key skipped: '{def.Label}'");
            }

            maxX = Math.Max(maxX, px + pw);
            maxY = Math.Max(maxY, py + ph);
        }

        uiGlobalIndices = indices.ToArray();

        KeyboardCanvas.Width = maxX + 8;
        KeyboardCanvas.Height = maxY + 8;
    }

    private bool TryGetGlobalKeyIndex(string layoutKey, out int globalKeyIndex)
    {
        globalKeyIndex = -1;
        if (!lightingProfile.TryGetMatrixPosition(layoutKey, out Vector2Int vector))
            return false;

        globalKeyIndex = MatrixTransform(vector.x, vector.y, xCount);
        return true;
    }

    #endregion

    #region Animation Control

    private void StartAnimation()
    {
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
        if (currentFrame > frameCount)
        {
            if (!repeatAll)
            {
                // Reached the end and not repeating: stop on the last frame.
                PauseAnimation();
                ShowPlayButton();
                return;
            }
            currentFrame = 1;
        }
        // Setting Value raises TimelineSlider_ValueChanged, which renders the frame.
        TimelineSlider.Value = currentFrame;
    }

    #endregion

    #region Lighting Control

    public void ShowFrame(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= frameDatas.Count)
            return;
        FrameData frameData = frameDatas[frameIndex];
        // renderedKeys already holds the precomputed global index per key,
        // so this hot path does no matrix lookups or conversions.
        foreach (var (display, globalKeyIndex) in renderedKeys)
        {
            byte[] rgb = frameData.GetKeyColor(globalKeyIndex);
            display.SetColor(rgb[0], rgb[1], rgb[2]);
        }
    }

    public void SetAllFramesColor(int[] globalKeyIndices, FrameData.ColorStrategy colorStrategy)
    {
        foreach (var frameData in frameDatas)
        {
            frameData.SetAllKeyColors(globalKeyIndices, colorStrategy);
        }
    }

    public class FrameData
    {
        private static readonly byte[] Off = { 0x00, 0x00, 0x00 };

        public readonly short frameIndex;
        public Dictionary<int, byte[]> KeyColors { get; set; } = new();

        public delegate byte[] ColorStrategy(short frameIndex, int globalKeyIndex);

        public FrameData(short index)
        {
            this.frameIndex = index;
        }

        public void SetAllKeyColors(int[] globalKeyIndices, ColorStrategy colorStrategy)
        {
            foreach (int globalKeyIndex in globalKeyIndices)
            {
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
            // Shared "off" instance avoids per-key allocations on the render path.
            return KeyColors.TryGetValue(globalKeyIndex, out var rgb) ? rgb : Off;
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

        // Use the same strategy the UI renders with, so sent packets match the preview.
        return (currentStrategy ?? FrameScan)(frameIndex, keyGlobalIndex);
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

    // The model-specific light-key matrix and layout->matrix map now live in the
    // keyboard lighting profile (see ArmouryProtocol.Lighting). Resolve positions
    // through 'lightingProfile' instead.

    #endregion
}