using Base.Core;

namespace KeyboardHallSensor
{
    [PageInfo("Segment", Glyph = "\uE765", ShortName = "SEG", NavOrder = 2, Path = ["Keyboard", "Hall Effect"],
        Description = "If data isn't showing properly, try to change packet size to match the responding data. i.e. M705: 2")]
    public class SegmentPage : MFGKeyboardStreamingPage
    {
        protected override string MfgCmdName => "hall_segment";
        protected override byte MfdCmdCode => 0x01;
        protected override int MfgCmdPackageSize => dynamicPackageSize;
        protected override int MaxValue { get; set; } = 350;
        protected override bool CanRecord => true;
        protected override bool CanAdjustMax => true;

        [Config(Name = "Packet Size", Header = "Segment", Min = 2, Max = 5)]
        private int dynamicPackageSize = 3;

        public override void Awake()
        {
            base.Awake();
            Chart.AxisYLabelCount = 7;
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