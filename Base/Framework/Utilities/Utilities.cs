using System;
using System.Globalization;

namespace Base.Helpers
{
    /// <summary>
    /// Helpers for conversion, formatting, and other miscellaneous tasks that don't belong in a specific class.
    /// </summary>
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
        public static byte LowByte(this ushort value) => (byte)(value & 0xFF);
        public static byte HighByte(this ushort value) => (byte)((value >> 8) & 0xFF);

        /// <summary>
        /// Formats a TimeSpan into a human-readable string with
        /// appropriate units (microseconds, milliseconds, seconds...).
        /// </summary>
        public static string FormatInterval(TimeSpan interval, int decimals = 3)
        {
            string[] subfix = { "us", "ms", "s", "m", "h", "d" };
            int[] gap = { 1000, 1000, 1000, 60, 60, 24 };

            double value = interval.TotalMilliseconds * 1000; // Start with microseconds
            int index = 0;

            while (index < gap.Length && value >= gap[index])
            {
                value /= gap[index];
                index++;
            }

            
            if (value == 0) return "0";

            double abs = Math.Abs(value);
            int digits = (int)Math.Floor(Math.Log10(abs)) + 1;
            decimals = Math.Max(0, decimals - digits);

            double rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
            string formatted = rounded.ToString($"0.{new string('#', decimals)}");

            return $"{formatted}{subfix[index]}";
        }
    }
}
