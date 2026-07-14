using System;
using System.Globalization;

namespace Base.Helpers
{
    public static class Utilities
    {
        public static bool Is<T>(this string s) => Is<T>(s, out _);
        public static bool Is<T>(this string s, out T value)
        {
            try
            {
                value = (T)Convert.ChangeType(s, typeof(T), CultureInfo.CurrentCulture);
                return true;
            }
            catch
            {
                value = default;
                return false;
            }
        }

        public static byte LowByte(this short value) => (byte)(value & 0xFF);
        public static byte HighByte(this short value) => (byte)((value >> 8) & 0xFF);
    }
}
