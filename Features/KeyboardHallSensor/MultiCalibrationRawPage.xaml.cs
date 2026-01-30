using Base.Core;
using Base.Pages;
using Base.Services;
using Base.Services.Peripheral;
using System.Windows.Controls;

namespace KeyboardHallSensor
{
    public partial class MultiCalibrationRawPage : PageBase
    {
        public override string PageName => "Multi Calibration Raw";
        public override string ShortName => "MCL";
        public override int NavOrder => 3;
        protected string MfgCmdName => "get_raw_multi_calibration";
        protected PeripheralInterface ActiveInterface => KeyboardCommonProtocol.Instance.ActiveInterface;

        private readonly Dictionary<byte, StackPanel> createdStackPanels = new();
        private readonly Dictionary<StackPanel, List<Button>> createdButtonsMap = new();

        public override void Awake()
        {
            InitializeComponent();
            base.Awake();
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            Enter();
            if (ActiveInterface == null) return;

            ActiveInterface.OnDataReceived += Parse;
            DeviceSelection.Instance.OnActiveDeviceDisconnected += ClearAll;
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            Exit();
            if (ActiveInterface == null) return;

            ActiveInterface.OnDataReceived -= Parse;
            DeviceSelection.Instance.OnActiveDeviceDisconnected -= ClearAll;
        }

        protected void Enter()
        {
            KeyboardCommonProtocol.Instance.OnInterfaceDisconnected += Exit;
            SendCmd();
        }
        protected void Exit()
        {
            KeyboardCommonProtocol.Instance.OnInterfaceDisconnected -= Exit;
            if (ActiveInterface == null) return;
            ProtocalService.ExitHallProdTest(ActiveInterface);
        }

        [AppMenuItem("Send Command")]
        protected void SendCmd()
        {
            if (ActiveInterface == null) return;
            for(byte depth=0; depth<7; depth++)
            {
                for(byte row = 0; row < 8; row++)
                {
                    ProtocalService.AppendCmd(ActiveInterface, MfgCmdName, true, depth, row);
                }
            }
        }

        public void Parse(ReadOnlyMemory<byte> bytes)
        {
            var span = bytes.Span;

            if (span.Length < 4 || span[1] != 0xFA || span[2] != 0x10 || span[3] != 0x06)
                return;

            int dataStart = 7;
            int depth = span[5];
            byte row = span[6];
            int index = 0;

            if (!rowKeyDataMap.ContainsKey(row))
            {
                rowKeyDataMap[row] = new KeyData[64];
            }

            while (dataStart + index * 2 < span.Length)
            {
                rowKeyDataMap[row][index] ??= new KeyData();
                if(depth >= rowKeyDataMap[row][index].segmentValues.Count)
                {
                    rowKeyDataMap[row][index].segmentValues.Add(0);
                }
                byte highByte = span[dataStart + index * 2 + 1];
                byte lowByte = span[dataStart + index * 2];
                rowKeyDataMap[row][index].segmentValues[depth] = ParseValue(highByte, lowByte);
                index++;
            }
            Dispatcher.Invoke(UpdateKeyDisplay);
        }

        protected int ParseValue(byte highByte, byte lowByte)
        {
            return (highByte << 8) + lowByte;
        }

        private void UpdateKeyDisplay()
        {
            var keys = rowKeyDataMap.Keys;
            for (int row = 0; row < keys.Count; row++)
            {
                byte key = keys.ElementAt(row);
                if(!createdStackPanels.ContainsKey(key))
                {
                    StackPanel stackPanel = new StackPanel()
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new System.Windows.Thickness(5)
                    };
                    createdButtonsMap[stackPanel] = new List<Button>();
                    for (int i = 0; i < 64; i++)
                    {
                        int rowCopy = row;
                        int indexCopy = i;
                        Button newButton = new Button()
                        {
                            Content = $"{i:X2}",
                            Margin = new System.Windows.Thickness(2),
                            Width = 50,
                            Height = 30
                        };
                        newButton.Click += (s, e) => ButtonClicked(keys.ElementAt(rowCopy), (byte)indexCopy);
                        createdButtonsMap[stackPanel].Add(newButton);
                        stackPanel.Children.Add(newButton);
                    }
                    createdStackPanels[key] = stackPanel;
                    MainStackPanel.Children.Add(stackPanel);
                }

                StackPanel targetPanel = createdStackPanels[key];
                if (targetPanel == null) continue;

                List<Button> buttons = createdButtonsMap[targetPanel];
                for (int index = 0; index < rowKeyDataMap[key].Length; index++)
                {
                    KeyData keyData = rowKeyDataMap[key][index];
                    buttons[index].Content = $"{row}, {index}";
                }
            }
        }

        private void ButtonClicked(byte row, byte index)
        {
            if (!rowKeyDataMap.ContainsKey(row)) return;
            KeyData keyData = rowKeyDataMap[row][index];
            if (keyData == null) return;
            string message = $"Key ({row}, {index}) Raw Values:\n";
            for (int i = 0; i < keyData.segmentValues.Count; i++)
            {
                message += $"[{i}]: {keyData.segmentValues[i]}\n";
            }
            System.Windows.MessageBox.Show(message, "Key Raw Values", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        protected void ClearAll()
        {

        }

        Dictionary<byte, KeyData[]> rowKeyDataMap = new();

        public class KeyData
        {
            public List<int> segmentValues;

            public KeyData()
            {
                segmentValues = new List<int>();
            }
        }
    }
}
