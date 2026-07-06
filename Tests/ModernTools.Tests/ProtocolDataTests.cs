using System.Text;
using CommonProtocol;
using Xunit;

namespace ModernTools.Tests;

/// <summary>
/// Tests for the binary protocol decoders in <c>CommonProtocol.Listener</c>
/// (<see cref="Data{T}"/> subclasses and <see cref="Structure"/>). These decode
/// raw device byte streams, so offset/length/endianness bugs corrupt data
/// silently — the highest-value logic to pin down.
/// </summary>
public class ProtocolDataTests
{
    [Fact]
    public void ByteData_ReadsByteAtOffset()
    {
        var span = new byte[] { 0x00, 0x00, 0xAB, 0x00 };
        var d = new ByteData(2);
        Assert.Equal((byte)0xAB, (byte)d.ParseValue(span));
        Assert.Equal((byte)0xAB, d.Value);
    }

    [Theory]
    [InlineData(0x00, false)]
    [InlineData(0x01, true)]
    [InlineData(0xFF, true)]
    public void BoolData_TreatsAnyNonZeroAsTrue(byte raw, bool expected)
    {
        var span = new byte[] { raw };
        var d = new BoolData(0);
        Assert.Equal(expected, (bool)d.ParseValue(span));
    }

    [Fact]
    public void ShortData_ReadsLittleEndian()
    {
        var span = new byte[] { 0x34, 0x12 }; // 0x1234
        var d = new ShortData(0);
        Assert.Equal((short)0x1234, (short)d.ParseValue(span));
    }

    [Fact]
    public void IntData_ReadsLittleEndian()
    {
        var span = new byte[] { 0x78, 0x56, 0x34, 0x12 }; // 0x12345678
        var d = new IntData(0);
        Assert.Equal(0x12345678, (int)d.ParseValue(span));
    }

    [Fact]
    public void LongData_ReadsLittleEndian()
    {
        var span = new byte[] { 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01 };
        var d = new LongData(0);
        Assert.Equal(0x0102030405060708L, (long)d.ParseValue(span));
    }

    [Fact]
    public void IntData_RespectsOffset()
    {
        var span = new byte[] { 0xFF, 0xFF, 0x78, 0x56, 0x34, 0x12 };
        var d = new IntData(2);
        Assert.Equal(0x12345678, (int)d.ParseValue(span));
    }

    [Fact]
    public void StringData_StopsAtNullTerminator()
    {
        // "Hi" + null + trailing garbage within the declared length.
        var span = new byte[] { (byte)'H', (byte)'i', 0x00, (byte)'X', (byte)'Y' };
        var d = new StringData(0, 5);
        Assert.Equal("Hi", (string)d.ParseValue(span));
    }

    [Fact]
    public void StringData_WithoutNull_UsesFullLength()
    {
        var span = Encoding.UTF8.GetBytes("ABCD");
        var d = new StringData(0, 4);
        Assert.Equal("ABCD", (string)d.ParseValue(span));
    }

    [Fact]
    public void StringData_HonorsCustomEncoding()
    {
        var span = Encoding.Unicode.GetBytes("Hi"); // 4 bytes UTF-16LE
        var d = new StringData(0, 4, Encoding.Unicode);
        Assert.Equal("Hi", (string)d.ParseValue(span));
    }

    [Fact]
    public void ByteArrayData_CopiesSliceAndFormatsHex()
    {
        var span = new byte[] { 0x00, 0xDE, 0xAD, 0xBE, 0xEF };
        var d = new ByteArrayData(1, 4);
        var value = (byte[])d.ParseValue(span);

        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, value);
        Assert.Equal("DEADBEEF", d.ToHexString());
    }

    [Fact]
    public void ByteArrayData_ToHexString_EmptyBeforeParse()
    {
        var d = new ByteArrayData(0, 4);
        Assert.Equal(string.Empty, d.ToHexString());
    }

    [Fact]
    public void Slice_OffsetPlusLengthBeyondSpan_Throws()
    {
        var span = new byte[] { 0x01, 0x02 };
        var d = new IntData(0); // needs 4 bytes, only 2 available
        Assert.Throws<ArgumentOutOfRangeException>(() => d.ParseValue(span));
    }

    [Fact]
    public void Slice_OffsetBeyondSpan_Throws()
    {
        var span = new byte[] { 0x01, 0x02 };
        var d = new ByteData(5);
        Assert.Throws<ArgumentOutOfRangeException>(() => d.ParseValue(span));
    }

    [Fact]
    public void Structure_Parse_PopulatesAllFields()
    {
        // Layout: [cmd?][x?] version(2) flag(1) name(3)
        var span = new byte[]
        {
            0x00, 0x00,             // padding
            0x02, 0x01,             // version = 0x0102
            0x01,                   // flag = true
            (byte)'A', (byte)'B', 0x00, // name = "AB"
        };

        var s = new SampleStructure();
        s.Parse(span);

        Assert.Equal((short)0x0102, s.version.Value);
        Assert.True(s.flag.Value);
        Assert.Equal("AB", s.name.Value);
    }

    /// <summary>Minimal concrete <see cref="Structure"/> for exercising reflection-based parse.</summary>
    private sealed class SampleStructure : Structure
    {
        public override byte Command => 0x01;
        public override byte Key => 0x02;

        public ShortData version = new(2);
        public BoolData flag = new(4);
        public StringData name = new(5, 3);
    }
}
