using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Base.Infrastructure.Protocol
{
    public class PSegmentDefinition
    {
        public int Size;
        public PSegmentDefinition[] AvailableContinuations;

        public ValidValue[] ValidValues;



        public class ShortValue : ValidValue
        {
            public override int Size => 2;
            public short value;

            public override bool IsValid(byte[] bytes)
            {
                if (bytes.Length != Size) return false;
                short val = (short)(bytes[0] | (bytes[1] << 8));
                return val == value;
            }
        }

        public class UshortRange : ValidValue
        {
            public override int Size => 2;
            public ushort from;
            public ushort to;
            public override bool IsValid(byte[] bytes)
            {
                if (bytes.Length != Size) return false;
                ushort value = (ushort)(bytes[0] | (bytes[1] << 8));

                return value >= from && value <= to;
            }
        }

        public class ByteValue : ValidValue
        {
            public override int Size => 1;
            public byte value;

            public override bool IsValid(byte[] bytes)
            {
                if (bytes.Length != Size) return false;
                return bytes[0] == value;
            }
        }

        public class ByteRange : ValidValue
        {
            public override int Size => 1;
            public byte from;
            public byte to;

            public override bool IsValid(byte[] bytes)
            {
                if (bytes.Length != Size) return false;
                return bytes[0] >= from && bytes[0] <= to;
            }
        }

        public abstract class ValidValue
        {
            public abstract int Size { get; }

            public abstract bool IsValid(byte[] bytes);
        }
    }
}
