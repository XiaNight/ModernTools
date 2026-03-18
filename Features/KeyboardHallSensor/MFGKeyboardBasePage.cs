using Base.Services;
using Base.Services.APIService;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KeyboardHallSensor;

public abstract class MFGKeyboardBasePage : KeyboardPageBase
{
    protected readonly ConcurrentDictionary<ushort, Sample> data = new();
    protected abstract string MfgCmdName { get; }
    protected abstract byte MfdCmdCode { get; }
    protected abstract int MfgCmdPackageSize { get; }
    protected virtual int MaxValue { get; set; } = 1;
    protected virtual bool IsManualCmd { get; } = true;

    protected TextBlock receivedCountLabel;

    #region API

    public record KeyData(byte keyCode, string value);

    [POST("GetData")]
    public List<KeyData> GetData()
    {
        var records = new List<KeyData>();
        foreach (var keyData in data)
        {
            string merged = string.Join(" ", keyData.Value.values);
            records.Add(new(keyData.Value.keyCode, merged));
        }
        return records;
    }
    #endregion

    public override void Awake()
    {
        base.Awake();
        receivedCountLabel = AddTextProperty("Recieved Keys: 0");

        if (IsManualCmd)
            AddButton("Send Cmd", SendCmd);
    }

    protected override void OnEnable()
    {
        base.OnEnable();
    }

    protected override void OnDisable()
    {
        base.OnDisable();
    }

    protected override void Exit()
    {
        KeyboardCommonProtocol.Instance.OnInterfaceDisconnected -= Exit;
        if (ActiveInterface == null) return;
        ProtocolService.ExitHallProdTest(ActiveInterface);
    }

    protected override void Enter()
    {
        KeyboardCommonProtocol.Instance.OnInterfaceDisconnected += Exit;
        if (ActiveInterface == null) return;
        ProtocolService.EnterHallProdTest(ActiveInterface, true /*Main.IsBlockEvent*/);
        SendCmd();
    }

    protected virtual void SendCmd()
    {
        if (ActiveInterface == null) return;
        ProtocolService.AppendCmd(ActiveInterface, MfgCmdName, true);
    }

    public override void Parse(ReadOnlyMemory<byte> bytes, DateTime time)
    {
        var span = bytes.Span;

        if (span.Length < 4 || span[1] != 0x04 || span[2] != 0x20 || span[3] != MfdCmdCode)
            return;

        int index = 4;

        while (index + MfgCmdPackageSize <= span.Length)
        {
            byte rowByte = span[4];
            byte keyCode = span[index];
            if (keyCode == 0) break;

            var values = bytes.Slice(index, MfgCmdPackageSize);
            ushort keyHash = (ushort)((rowByte << 8) + keyCode);

            if (!data.TryGetValue(keyHash, out Sample sample))
            {
                data[keyHash] = new() { keyCode = keyCode, values = values.ToArray(), dirtyCounter = 1 };
            }
            else
            {
                sample.values = values.ToArray();
                sample.dirtyCounter++;
            }

            index += MfgCmdPackageSize;
        }

        if (IsManualCmd)
            Main.Dispatcher.Invoke(UpdateKeyDisplays);
    }

    protected abstract int ParseValue(ReadOnlyMemory<byte> values);

    protected override void Update()
    {
        base.Update();
        UpdateKeyDisplays();
    }

    protected override void ClearAll()
    {
        base.ClearAll();
        data.Clear();
        receivedCountLabel.Text = "Recieved Keys: 0";
        ResetMinMax();
    }

    private void UpdateKeyDisplays()
    {
        foreach (var item in data)
        {
            byte keyCode = item.Value.keyCode;
            KeyDisplay keyDisplay = item.Value.linkedKeyDisplay;
            if (keyDisplay == null)
            {
                if (!KeyDisplays.ContainsKey(keyCode))
                {
                    keyDisplay = keyCode == 0x2C ? AddKeyDisplay(Canvas.Children, keyCode, 350, 262, 50, 50) : AddKeyDisplay(keyCode, 50, 5);
                }
                else
                {
                    KeyDisplays.TryGetValue(keyCode, out keyDisplay);
                }
                item.Value.linkedKeyDisplay = keyDisplay;
            }

            if (keyDisplay == null) continue;

            UpdateKeyDisplay(item.Value);
        }

        receivedCountLabel.Text = $"Recieved Keys: {data.Count}";
    }

    protected virtual void UpdateKeyDisplay(Sample sample)
    {
        KeyDisplay keyDisplay = sample.linkedKeyDisplay;

        var value = ParseValue(sample.values);
        if (keyDisplay.IsMinMaxShown)
            keyDisplay.SetText(value.ToString());
        else
            keyDisplay.SetText($"{keyDisplay.Keycode:X2}\n{value}");

        double t = Math.Max(0, Math.Min(10, sample.dirtyCounter)) / 10.0;
        var baseBrush = (Brush)Application.Current.FindResource("SystemControlBackgroundBaseMediumBrush");
        var redBrush = Brushes.Red;

        if (baseBrush is SolidColorBrush b1 && redBrush is SolidColorBrush b2)
        {
            var c1 = b1.Color;
            var c2 = b2.Color;

            byte r = (byte)(c1.R + (c2.R - c1.R) * t);
            byte g = (byte)(c1.G + (c2.G - c1.G) * t);
            byte b = (byte)(c1.B + (c2.B - c1.B) * t);
            byte a = (byte)(c1.A + (c2.A - c1.A) * t);

            keyDisplay.SetBorderColor(new SolidColorBrush(Color.FromArgb(a, r, g, b)));
        }

        keyDisplay.SetFill(value, MaxValue);

        sample.dirtyCounter = 0;
    }

    public class Sample
    {
        public byte keyCode;
        public byte[] values;
        public int dirtyCounter;
        public KeyDisplay linkedKeyDisplay;
    }
}