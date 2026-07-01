using Base.Core;
using System.Windows.Media;

namespace KeyboardHallSensor
{
    [PageInfo("Gain", Glyph = "\uE765", ShortName = "GAN", NavOrder = 3, Path = ["Keyboard", "Hall Effect"])]
    public class GainPage : MFGKeyboardBasePage
    {
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

            sample.dirtyCounter = 0;
        }

        protected override int ParseValue(ReadOnlyMemory<byte> values)
        {
            return values.Span[1];
        }
    }
}