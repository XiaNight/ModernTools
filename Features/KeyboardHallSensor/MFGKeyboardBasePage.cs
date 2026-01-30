using Base.Services;
using Base.Services.APIService;
using System.Collections.Concurrent;
using System.Windows.Controls;

namespace KeyboardHallSensor
{
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
            ProtocalService.ExitHallProdTest(ActiveInterface);
        }

        protected override void Enter()
        {
            KeyboardCommonProtocol.Instance.OnInterfaceDisconnected += Exit;
            if (ActiveInterface == null) return;

            ProtocalService.EnterHallProdTest(ActiveInterface, true /*Main.IsBlockEvent*/);
            SendCmd();
        }

        protected virtual void SendCmd()
        {
            if (ActiveInterface == null) return;
            ProtocalService.AppendCmd(ActiveInterface, MfgCmdName, false);
        }

        public override void Parse(ReadOnlyMemory<byte> bytes)
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
                    data[keyHash] = new() { keyCode = keyCode, values = values.ToArray(), isFresh = true };
                }
                else
                {
                    sample.values = values.ToArray();
                    sample.isFresh = true;
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
                        if (keyCode == 0x2C)
                            keyDisplay = AddKeyDisplay(Canvas.Children, keyCode, 350, 262, 50, 50);
                        else
                            keyDisplay = AddKeyDisplay(keyCode, 50, 5);
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
            keyDisplay.SetBorderColor(sample.isFresh ? "SystemControlBackgroundBaseMediumBrush" : "Red");
            keyDisplay.SetFill(value, MaxValue);

            sample.isFresh = false;
        }

        public class Sample
        {
            public byte keyCode;
            public byte[] values;
            public bool isFresh;
            public KeyDisplay linkedKeyDisplay;
        }
    }
}