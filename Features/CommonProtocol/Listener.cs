using Base.Services;
using System.Buffers.Binary;
using System.Reflection;
using System.Text;

namespace CommonProtocol;

public class Listener(byte[] pattern)
{
    private readonly byte[] pattern = pattern;
    public event Action<Listener, ReadOnlyMemory<byte>, DateTime> OnTriggered = delegate { };
    public readonly List<Structure> structures = new();

    public void Match(ReadOnlyMemory<byte> data, DateTime time)
    {
        if (ProtocolService.IsCmdMatch(pattern, data.Span))
        {
            ReadOnlySpan<byte> span = data.Span[1..];
            foreach(Structure structure in structures)
            {
                structure.Parse(span);
            }
            OnTriggered(this, data, time);
        }
    }

    public bool TryGet<T>(out T structure) where T : Structure
    {
        foreach (var t in structures)
        {
            if (t is T to)
            {
                structure = to;
                return true;
            }
        }
        structure = null;
        return false;
    }
}

public abstract class Structure
{
    public abstract byte Command { get; }
    public abstract byte Key { get; }
    public short Index { get; } = 0;

    private readonly IData[] datas;
    public Structure()
    {
        datas = GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(f => typeof(IData).IsAssignableFrom(f.FieldType))
            .Select(f => (IData)f.GetValue(this))
            .Where(v => v != null)
            .ToArray();
    }

    public void Parse(ReadOnlySpan<byte> span)
    {
        foreach (var data in datas)
        {
            data.ParseValue(span);
        }
    }
}

public interface IData
{
    int Offset { get; }
    int Length { get; }
    object ParseValue(ReadOnlySpan<byte> span);
}

public abstract class Data<T>(int offset, int length) : IData
{
    public int Offset { get; } = offset;
    public int Length { get; } = length;
    public T Value { get; private set; }

    public abstract T GetTypedValue(ReadOnlySpan<byte> span);

    public object ParseValue(ReadOnlySpan<byte> span)
    {
        Value = GetTypedValue(span);
        return Value;
    }

    protected ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> span)
    {
        return (uint)Offset > (uint)span.Length || (uint)(Offset + Length) > (uint)span.Length
            ? throw new ArgumentOutOfRangeException(nameof(span), $"Not enough data.")
            : span.Slice(Offset, Length);
    }
}

public sealed class BoolData(int offset) : Data<bool>(offset, 1)
{
    public override bool GetTypedValue(ReadOnlySpan<byte> span)
        => Slice(span)[0] != 0;
}

public sealed class ByteData(int offset) : Data<byte>(offset, 1)
{
    public override byte GetTypedValue(ReadOnlySpan<byte> span)
        => Slice(span)[0];
}

public sealed class ShortData(int offset) : Data<short>(offset, sizeof(short))
{
    public override short GetTypedValue(ReadOnlySpan<byte> span)
        => BinaryPrimitives.ReadInt16LittleEndian(Slice(span));
}

public sealed class IntData(int offset) : Data<int>(offset, sizeof(int))
{
    public override int GetTypedValue(ReadOnlySpan<byte> span)
        => BinaryPrimitives.ReadInt32LittleEndian(Slice(span));
}

public sealed class LongData(int offset) : Data<long>(offset, sizeof(long))
{
    public override long GetTypedValue(ReadOnlySpan<byte> span)
        => BinaryPrimitives.ReadInt64LittleEndian(Slice(span));
}

public sealed class StringData(int offset, int length, Encoding encoding) : Data<string>(offset, length)
{
    private readonly Encoding encoding = encoding;

    public StringData(int offset, int length) : this(offset, length, Encoding.UTF8) { }

    public override string GetTypedValue(ReadOnlySpan<byte> span)
    {
        ReadOnlySpan<byte> bytes = Slice(span);

        int nullIndex = bytes.IndexOf((byte)0);
        if (nullIndex >= 0)
            bytes = bytes.Slice(0, nullIndex);

        return encoding.GetString(bytes);
    }
}

public sealed class ByteArrayData(int offset, int length) : Data<byte[]>(offset, length)
{
    public override byte[] GetTypedValue(ReadOnlySpan<byte> span)
    {
        ReadOnlySpan<byte> bytes = Slice(span);

        return bytes.ToArray();
    }

    public string ToHexString()
    {
        return Value != null ? Convert.ToHexString(Value) : string.Empty;
    }
}