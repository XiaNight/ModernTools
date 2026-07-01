using Base.Core;
using System.Windows.Controls;
using System.Windows.Media;

namespace KeyboardHallSensor
{
    [PageInfo("Bottom Base Line", Glyph = "\uE765", ShortName = "BBL", NavOrder = 5, Path = ["Keyboard", "Hall Effect"])]
    public class BaseLineBottomPage : MFGKeyboardStreamingPage
    {
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