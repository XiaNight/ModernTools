using ArmouryProtocol.Lighting;
using Base.Core;
using Base.Framework.Utilities;
using Base.Helpers;
using Base.Pages;
using Base.Services;
using Base.Services.Peripheral;
using KeyboardHallSensor;
using System.Windows.Controls;
using System.Windows.Data;
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
    // Precomputed once in BuildKeyboard: each rendered key paired with its cached
    // light info (matrix cell + normalized physical position), plus a lookup by index.
    private readonly List<(KeyDisplayRendered display, KeyLightInfo info)> renderedKeys = new();
    private readonly Dictionary<int, KeyLightInfo> keyInfoByGlobalIndex = new();
    public List<FrameData> frameDatas = new();
    private LightingPreset currentPreset;

    //- Animation Player
    private DispatcherTimer animationTimer;
    private bool repeatAll = true;

    // While sending, the preview scrubs to whichever frame is currently going out.
    private volatile bool sendPreviewActive;

    // Frames the current effect spans: the preset's own count, or the manual
    // frame-count config for developer strategies (FrameCount == 0).
    private int ActiveFrameCount =>
        currentPreset != null && currentPreset.FrameCount > 0 ? currentPreset.FrameCount : frameCount;

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
        if (currentFrame > ActiveFrameCount)
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
            currentFrame = ActiveFrameCount;
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
        // Only developer strategies follow the manual frame-count config; named
        // presets carry their own count. Regenerate so the change takes effect.
        if (currentPreset != null && currentPreset.FrameCount <= 0)
        {
            ApplyPreset(currentPreset);
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
        ProtocolService.OnCmdSent += HandleCmdSent;

        BuildKeyboard();
        PopulatePresets();
    }

    private void PopulatePresets()
    {
        // Group the flat preset list into Static / Dynamic / Developer sections.
        var view = new ListCollectionView(LightingPresets.All.ToList());
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LightingPreset.Category)));

        PresetSelector.ItemsSource = view;
        PresetSelector.SelectionChanged += PresetSelector_SelectionChanged;

        // Selecting fires SelectionChanged -> ApplyPreset (generates + plays).
        PresetSelector.SelectedItem = LightingPresets.Default;
    }

    private void PresetSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetSelector.SelectedItem is LightingPreset preset)
        {
            ApplyPreset(preset);
        }
    }

    private void ApplyPreset(LightingPreset preset)
    {
        currentPreset = preset;

        int count = ActiveFrameCount;
        RegenerateFrames(count);

        TimelineSlider.Maximum = count;
        TimelineSlider.Value = 1;
        ShowFrame(0);

        // Multi-frame effects animate; a single still frame just holds.
        if (count > 1)
        {
            StartAnimation();
            ShowPauseButton();
        }
        else
        {
            PauseAnimation();
            ShowPlayButton();
        }
    }

    // Rebuilds every frame's colors from the current preset.
    // EVERY addressable key index (0..keyCount-1) is generated and stored - including
    // keys with no on-screen keycap and matrix cells not present in the layout - so the
    // packets always carry a complete, correctly-indexed color set. Mapped keys use
    // their real cached physical position; unknown keys fall back to their matrix cell
    // with a default (0,0) position.
    private void RegenerateFrames(int count)
    {
        frameDatas.Clear();

        for (int frame = 0; frame < count; frame++)
        {
            FrameData frameData = new((short)frame);

            for (int globalKeyIndex = 0; globalKeyIndex < keyCount; globalKeyIndex++)
            {
                if (!keyInfoByGlobalIndex.TryGetValue(globalKeyIndex, out KeyLightInfo info))
                {
                    Vector2Int cell = InverseMatrixTransform(globalKeyIndex, xCount);
                    info = new KeyLightInfo(globalKeyIndex, cell.x, cell.y, 0f, 0f, 0f, 0f);
                }

                var (r, g, b) = currentPreset.GetColor(frame, count, info);
                frameData.SetKeyColor(globalKeyIndex, r, g, b);
            }

            frameDatas.Add(frameData);
        }
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
        float maxPxX = 0, maxPxY = 0;

        renderedKeys.Clear();
        keyInfoByGlobalIndex.Clear();

        // First pass: create the displays and resolve each mapped key's matrix cell
        // and physical center. Track layout bounds so we can normalize positions.
        var pending = new List<(KeyDisplayRendered display, int globalKeyIndex, int mx, int my, float cx, float cy)>();
        float minCx = float.MaxValue, minCy = float.MaxValue;
        float maxCx = float.MinValue, maxCy = float.MinValue;

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

            // Keys that don't map (unknown label / not in the matrix) are still
            // shown but won't be driven by the effects.
            if (lightingProfile.TryGetMatrixPosition(def.Label, out Vector2Int cell))
            {
                int globalKeyIndex = MatrixTransform(cell.x, cell.y, xCount);
                float cx = def.X + (def.W / 2f);
                float cy = def.Y + (def.H / 2f);

                pending.Add((display, globalKeyIndex, cell.x, cell.y, cx, cy));

                minCx = Math.Min(minCx, cx);
                maxCx = Math.Max(maxCx, cx);
                minCy = Math.Min(minCy, cy);
                maxCy = Math.Max(maxCy, cy);
            }
            else
            {
                Debug.Log($"[Keyboard] Unmapped layout key skipped: '{def.Label}'");
            }

            maxPxX = Math.Max(maxPxX, px + pw);
            maxPxY = Math.Max(maxPxY, py + ph);
        }

        // Second pass: normalize physical positions to 0..1 and cache the results.
        float rangeX = Math.Max(1e-3f, maxCx - minCx);
        float rangeY = Math.Max(1e-3f, maxCy - minCy);

        foreach (var p in pending)
        {
            var info = new KeyLightInfo(
                p.globalKeyIndex, p.mx, p.my,
                (p.cx - minCx) / rangeX, (p.cy - minCy) / rangeY,
                p.cx, p.cy);

            renderedKeys.Add((p.display, info));
            keyInfoByGlobalIndex[p.globalKeyIndex] = info;
        }

        KeyboardCanvas.Width = maxPxX + 8;
        KeyboardCanvas.Height = maxPxY + 8;
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
        if (currentFrame > ActiveFrameCount)
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
        // renderedKeys holds the cached key info, so this hot path does no
        // matrix lookups or conversions.
        foreach (var (display, info) in renderedKeys)
        {
            byte[] rgb = frameData.GetKeyColor(info.GlobalKeyIndex);
            display.SetColor(rgb[0], rgb[1], rgb[2]);
        }
    }

    public class FrameData
    {
        private static readonly byte[] Off = { 0x00, 0x00, 0x00 };

        public readonly short frameIndex;
        public Dictionary<int, byte[]> KeyColors { get; set; } = new();

        public FrameData(short index)
        {
            this.frameIndex = index;
        }

        public void SetKeyColor(int globalKeyIndex, byte r, byte g, byte b)
        {
            KeyColors[globalKeyIndex] = new[] { r, g, b };
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
        // Arm the timeline to follow the packets as they go out, and stop the
        // free-running preview so the two don't fight over the current frame.
        // Only when a device is actually connected (otherwise nothing is sent,
        // and we'd never get the Apply packet that disarms the preview).
        sendPreviewActive = ActiveInterface != null;
        if (sendPreviewActive)
        {
            PauseAnimation();
            ShowPlayButton();
        }

        int checksum = 0;
        byte[] initialPacket = Construct3_1Mode();
        List<byte[]> frameBytes = Construct3_2Data(ref checksum);
        List<byte[]> reactiveBytes = Construct3_3Reactive();
        byte[] brightnessPacket = Construct3_4Brightness();
        byte[] applyPacket = Construct3_5Apply(checksum);

        ProtocolService.AppendCmdTimeout(ActiveInterface, initialPacket, true, 5000);

        for (int framePacketId = 0; framePacketId < frameBytes.Count; framePacketId++)
        {
            byte[] packet = frameBytes[framePacketId];
            ProtocolService.AppendCmd(ActiveInterface, packet, framePacketId % 6 == 5);
        }

        for (int reactivePacketId = 0; reactivePacketId < reactiveBytes.Count; reactivePacketId++)
        {
            byte[] packet = reactiveBytes[reactivePacketId];
            ProtocolService.AppendCmdTimeout(ActiveInterface, packet, true, 5000);
        }
        ProtocolService.AppendCmdTimeout(ActiveInterface, brightnessPacket, true, 5000);
        ProtocolService.AppendCmdTimeout(ActiveInterface, applyPacket, true, 5000);

        return frameBytes;
    }

    // Fired (on the send worker thread) after each packet is written. We mirror
    // the 3-2 "Data" packets onto the timeline so the on-screen keyboard shows
    // exactly which frame is currently being uploaded.
    private void HandleCmdSent(ProtocolService.CmdData cmd)
    {
        if (!sendPreviewActive)
            return;

        byte[] bytes = cmd.Cmd;
        if (bytes == null || bytes.Length < 4 || bytes[0] != 0xC1)
            return;

        switch (bytes[1])
        {
            case 0x01: // 3-2 Data: frame index is little-endian at [2], [3].
                int frameIndex = bytes[2] | (bytes[3] << 8);
                Dispatcher.BeginInvoke((Action)(() => ScrubToSendingFrame(frameIndex)));
                break;

            case 0x04: // 3-5 Apply: the upload has finished.
                Dispatcher.BeginInvoke((Action)EndSendPreview);
                break;
        }
    }

    private void ScrubToSendingFrame(int frameIndex)
    {
        if (!sendPreviewActive)
            return;

        int display = frameIndex + 1; // timeline is 1-based
        if (display < 1)
            display = 1;
        if (display > TimelineSlider.Maximum)
            display = (int)TimelineSlider.Maximum;

        // Raises TimelineSlider_ValueChanged, which renders the frame.
        TimelineSlider.Value = display;
    }

    private void EndSendPreview()
    {
        if (!sendPreviewActive)
            return;
        sendPreviewActive = false;

        // Return to the live preview for animated effects.
        if (ActiveFrameCount > 1)
        {
            StartAnimation();
            ShowPauseButton();
        }
    }

    private byte[] Construct3_1Mode()
    {
        int activeFrameCount = ActiveFrameCount;
        byte frameCountLowByte = (byte)(activeFrameCount & 0xFF);
        byte frameCountHighByte = (byte)((activeFrameCount >> 8) & 0xFF);
        byte[] data = [0xC1, 0x00, 0x00, 0x00, frameCountLowByte, frameCountHighByte, xCount, yCount];
        return data;
    }

    private List<byte[]> Construct3_2Data(ref int checksum)
    {
        List<byte[]> dataList = new();

        int activeFrameCount = ActiveFrameCount;
        for (short currentFrameIndex = 0; currentFrameIndex < activeFrameCount; currentFrameIndex++)
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

    /// <param name="frameIndex">Current frame index in all frames.</param>
    /// <param name="packetIndex">Current packet index in a single frame.</param>
    /// <param name="keyIndex">Current key index in a single packet.</param>
    private byte[] GetKeyRGB(short frameIndex, byte packetIndex, byte keyIndex)
    {
        int keyGlobalIndex = (packetIndex * keysPerPacket) + keyIndex;

        // Read the already-generated frame so what's sent exactly matches the preview.
        if (frameIndex >= 0 && frameIndex < frameDatas.Count)
        {
            return frameDatas[frameIndex].GetKeyColor(keyGlobalIndex);
        }
        return [0x00, 0x00, 0x00];
    }

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