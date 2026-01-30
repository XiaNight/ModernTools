namespace KeyboardHallSensor
{
    public class BottomAveragePage : MFGKeyboardBasePage
    {
        public override string PageName => "Bottom Average";
        public override string ShortName => "BMA";
        public override int NavOrder => 4;
        protected override string MfgCmdName => "hall_bottom_average";
        protected override byte MfdCmdCode => 0x07;
        protected override int MfgCmdPackageSize => 4;

        protected override void UpdateKeyDisplay(Sample sample)
        {
            KeyDisplay keyDisplay = sample.linkedKeyDisplay;

            var value = ParseValue(sample.values);
            keyDisplay.SetText($"{keyDisplay.Keycode:X2}\n{value}\n{sample.values[3]:X2}");
            keyDisplay.SetFill(value, 65535.0);

            sample.isFresh = false;
        }

        protected override int ParseValue(ReadOnlyMemory<byte> values)
        {
            return values.Span[2] << 8 | values.Span[1];
        }
    }
}