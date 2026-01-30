namespace KeyboardHallSensor
{
    public class AnalogPage : MFGKeyboardStreamingPage
    {
        public override string PageName => "Analog";
        public override string ShortName => "ANA";
        public override int NavOrder => 1;
        public override string Glyph => "\uE765";
        protected override string MfgCmdName => "hall_analog";
        protected override byte MfdCmdCode => 0x04;
        protected override int MfgCmdPackageSize => 3;
        protected override int MaxValue { get; set; } = 2560;
        protected override bool CanRecord => true;

        protected override int ParseValue(ReadOnlyMemory<byte> values)
        {
            return values.Span[2] << 8 | values.Span[1];
        }
    }
}
