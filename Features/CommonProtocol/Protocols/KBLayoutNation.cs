namespace CommonProtocol.Protocols.KBLayoutNation;

//Get KB layout & nation
internal class KBLayoutNation : Structure
{
    public override byte Command => 0x12;
    public override byte Key => 0x12;

    public ByteData layout = new(4);
    public ByteData nation = new(5);

    public Layout GetLayout() => (Layout)layout.Value;

    public enum Layout : byte
    {
        US_104 = 0x01,
        UK_EU_105 = 0x02,
        JP_107 = 0x03
    }
}

internal class KBNationID : Structure
{
    public override byte Command => 0x12;
    public override byte Key => 0x13;
    public ByteData nationID = new(4);
}