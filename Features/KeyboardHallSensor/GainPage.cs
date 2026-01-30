using System.Windows.Media;

namespace KeyboardHallSensor
{
    public class GainPage : MFGKeyboardBasePage
    {
        public override string PageName => "Gain";
        public override string ShortName => "GAN";
        public override int NavOrder => 3;
        protected override string MfgCmdName => "hall_gain";
        protected override byte MfdCmdCode => 0x05;
        protected override int MfgCmdPackageSize => 2;

        protected override void UpdateKeyDisplay(Sample sample)
        {
            KeyDisplay keyDisplay = sample.linkedKeyDisplay;

            var value = ParseValue(sample.values);
            keyDisplay.SetText($"{sample.keyCode:X2}\n{value}");
            keyDisplay.SetFill(value, 16.0);

            if (value > 7) keyDisplay.SetFillColor("Accent4Brush");
            else keyDisplay.SetFillColor("Accent2Brush");

            sample.isFresh = false;
        }

        protected override int ParseValue(ReadOnlyMemory<byte> values)
        {
            return values.Span[1];
        }
    }
}