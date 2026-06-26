using Base.Core;

namespace KeyboardHallSensor
{
    [PageInfo("Top Base Line", Glyph = "\uE765", ShortName = "TBL", NavOrder = 6, Path = ["Keyboard", "Hall Effect"])]
    public class BaseLineTopPage : MFGKeyboardStreamingPage
    {
        protected override string MfgCmdName => "hall_baseline_top";
        protected override byte MfdCmdCode => 0x0D;
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