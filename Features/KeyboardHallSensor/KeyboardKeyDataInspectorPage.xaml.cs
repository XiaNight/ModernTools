using Base.Services;
using Base.Services.Peripheral;

namespace KeyboardHallSensor
{
    /// <summary>
    /// Interaction logic for KeyboardKeyDataInspectorPage.xaml
    /// </summary>
    public partial class KeyboardKeyDataInspectorPage : KeyboardPageBase
    {
        public override string PageName => "Key Data";
        private new PeripheralInterface ActiveInterface => KeyboardCommonProtocol.Instance.ActiveInterface;

        public KeyboardKeyDataInspectorPage() : base()
        {
            InitializeComponent();
        }

        public override void Awake()
        {
            base.Awake();

            AddButton("Send FF Command", SendFFCmd);

            ProtocalService.CommandDictionary.Add("kdi_key_data", [0xFA, 0x10, 0x0B, 0x00]);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            foreach(var key in KeyDisplays)
            {
                key.Value.ShowLabel();
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
        }

        protected override void Enter()
        {

        }

        protected override void Exit()
        {

        }

        private void SendFFCmd() => ProtocalService.AppendCmd(ActiveInterface, "kdi_key_data", true, 0xff);
        private void SendKDICmd(byte keycode) => ProtocalService.AppendCmd(ActiveInterface, "kdi_key_data", true, keycode);

        protected override void OnKeyDisplayClicked(byte keycode)
        {
            base.OnKeyDisplayClicked(keycode);
            if (ActiveInterface == null) return;

            SendKDICmd(keycode);
        }

        public override void Parse(ReadOnlyMemory<byte> bytes)
        {
            byte[] cmd = [0xFA, 0x10, 0x0B, 0x00];
            var span = bytes.Span;

            if (span.Length < cmd.Length || !span.Slice(1, cmd.Length).SequenceEqual(cmd))
                return;
            int average = span[9] | (span[10] << 8);
            string message = $"Key Code: 0x{span[5]:X2}\nGain: {span[6]}\nSwitch Type: {span[7]}\nswitchACT_type: {span[8]}\nAverage: {average}\nNoise: {span[11]}";
            System.Windows.MessageBox.Show(message, "Key Segment Values", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
    }
}
