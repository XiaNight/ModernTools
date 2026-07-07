using Base.Core;

namespace KeyboardHallSensor
{
    [PageInfo("Analog", Glyph = "\uE765", ShortName = "ANA", NavOrder = 1, Path = ["Keyboard", "Hall Effect"])]
    public class AnalogPage : MFGKeyboardStreamingPage
    {
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
