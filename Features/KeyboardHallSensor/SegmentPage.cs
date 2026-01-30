using System.Windows.Controls;
using System.Windows.Media;

namespace KeyboardHallSensor
{
    public class SegmentPage : MFGKeyboardStreamingPage
    {
        public override string PageName => "Segment";
        public override string ShortName => "SEG";
        public override int NavOrder => 2;
        protected override string MfgCmdName => "hall_segment";
        public override string Description => "If data isn't showing properly, try to change packet size to match the responding data. i.e. M705: 2";
        protected override byte MfdCmdCode => 0x01;
        protected override int MfgCmdPackageSize => dynamicPackageSize;
        protected override int MaxValue { get; set; } = 350;
        protected override bool CanRecord => true;
        protected override bool CanAdjustMax => true;

        private int dynamicPackageSize = 3;

        private TextBox packetSizeTextBox;

        public override void Awake()
        {
            base.Awake();
            Chart.AxisYLabelCount = 7;

            packetSizeTextBox = AddTextBox("Packet Size:", dynamicPackageSize.ToString(), handler: SetMgfCmdPackageSize);
        }

        public void SetMgfCmdPackageSize(int size)
        {
            if (size < 2) size = 2;
            if (size > 5) size = 5;
            dynamicPackageSize = size;
            packetSizeTextBox.Text = size.ToString();
        }

        public bool SetMgfCmdPackageSize(string text)
        {
            if (int.TryParse(text, out int size))
            {
                if (size < 2) { size = 2; packetSizeTextBox.Text = size.ToString(); }
                if (size > 5) { size = 5; packetSizeTextBox.Text = size.ToString(); }

                dynamicPackageSize = size;
                return true;
            }
            else
            {
                if (string.IsNullOrEmpty(text)) return false;

                packetSizeTextBox.Text = dynamicPackageSize.ToString();
                packetSizeTextBox.SelectionStart = packetSizeTextBox.Text.Length;
                return false;
            }
        }

        protected override int ParseValue(ReadOnlyMemory<byte> values)
        {
            int length = values.Length;
            int finalValue = 0;
            for (int i = length - 1; i > 0; i--)
            {
                finalValue = (finalValue << 8) | values.Span[i];
            }
            return finalValue;
        }
    }
}