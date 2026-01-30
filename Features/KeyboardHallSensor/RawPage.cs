using System.Windows.Controls;
using System.Windows.Media;

namespace KeyboardHallSensor
{
    public class RawPage : MFGKeyboardStreamingPage
    {
        public override string PageName => "Raw";
        public override string ShortName => "RAW";
        public override int NavOrder => 0;
        protected override string MfgCmdName => "hall_raw";
        protected override byte MfdCmdCode => 0x00;
        protected override int MfgCmdPackageSize => 3;
        protected override int MaxValue { get; set; } = 8192;
        protected override bool CanRecord => true;
        protected override bool CanAdjustMax => true;

        protected override int ParseValue(ReadOnlyMemory<byte> values)
        {
            return values.Span[2] << 8 | values.Span[1];
        }
    }
}
