using Base.Services;
using System.Buffers.Binary;
using System.Text;

namespace CommonProtocol
{
    public class Listener(byte[] pattern)
    {
        private readonly byte[] pattern = pattern;
        public event Action<Listener, ReadOnlyMemory<byte>, DateTime> OnTriggered = delegate { };
        public readonly Dictionary<string, IDataStructure> structure = new();

        public void Match(ReadOnlyMemory<byte> data, DateTime time)
        {
            if (ProtocolService.IsCmdMatch(pattern, data.Span))
            {
                OnTriggered(this, data, time);
            }
        }

        public interface IDataStructure
        {
            string Name { get; }
            int Offset { get; }
            int Length { get; }
            object GetValue(ReadOnlySpan<byte> span);
        }

        public abstract class DataStructure<T>(string name, int offset, int length) : IDataStructure
        {
            public string Name { get; } = name;
            public int Offset { get; } = offset;
            public int Length { get; } = length;

            public abstract T GetTypedValue(ReadOnlySpan<byte> span);

            public object GetValue(ReadOnlySpan<byte> span) => GetTypedValue(span);

            protected ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> span)
            {
                return (uint)Offset > (uint)span.Length || (uint)(Offset + Length) > (uint)span.Length
                    ? throw new ArgumentOutOfRangeException(nameof(span), $"Not enough data for '{Name}'.")
                    : span.Slice(Offset, Length);
            }
        }

        public sealed class ByteStructure(string name, int offset) : DataStructure<byte>(name, offset, 1)
        {
            public override byte GetTypedValue(ReadOnlySpan<byte> span)
                => Slice(span)[0];
        }

        public sealed class ShortStructure(string name, int offset) : DataStructure<short>(name, offset, sizeof(short))
        {
            public override short GetTypedValue(ReadOnlySpan<byte> span)
                => BinaryPrimitives.ReadInt16LittleEndian(Slice(span));
        }

        public sealed class IntStructure(string name, int offset) : DataStructure<int>(name, offset, sizeof(int))
        {
            public override int GetTypedValue(ReadOnlySpan<byte> span)
                => BinaryPrimitives.ReadInt32LittleEndian(Slice(span));
        }

        public sealed class LongStructure(string name, int offset) : DataStructure<long>(name, offset, sizeof(long))
        {
            public override long GetTypedValue(ReadOnlySpan<byte> span)
                => BinaryPrimitives.ReadInt64LittleEndian(Slice(span));
        }

        public sealed class StringStructure(string name, int offset, int length, Encoding encoding) : DataStructure<string>(name, offset, length)
        {
            private readonly Encoding encoding = encoding;

            public StringStructure(string name, int offset, int length) : this(name, offset, length, Encoding.UTF8) { }

            public override string GetTypedValue(ReadOnlySpan<byte> span)
            {
                ReadOnlySpan<byte> bytes = Slice(span);

                int nullIndex = bytes.IndexOf((byte)0);
                if (nullIndex >= 0)
                    bytes = bytes.Slice(0, nullIndex);

                return encoding.GetString(bytes);
            }
        }
    }
}
