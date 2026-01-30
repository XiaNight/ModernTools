namespace KeyboardHallSensor
{
    public class BaseLineTopPage : MFGKeyboardStreamingPage
    {
        public override string PageName => "Top Base Line";
        public override string ShortName => "TBL";
        public override int NavOrder => 6;
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