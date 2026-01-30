using System.Windows.Controls;
using System.Windows.Media;

namespace KeyboardHallSensor
{
    public class BaseLineBottomPage : MFGKeyboardStreamingPage
    {
        public override string PageName => "Bottom Base Line";
        public override string ShortName => "BBL";
        public override int NavOrder => 5;
        protected override string MfgCmdName => "hall_baseline_bottom";
        protected override byte MfdCmdCode => 0x0E;
        protected override int MfgCmdPackageSize => 3;
        protected override int MaxValue { get; set; } = 3500;
        protected override bool CanRecord => true;
        protected override bool CanAdjustMax => true;

        public override void Awake()
        {
            base.Awake();
            Chart.AxisYLabelCount = 7;
        }

        protected override int ParseValue(ReadOnlyMemory<byte> values)
        {
            return values.Span[2] << 8 | values.Span[1];
        }
    }
}