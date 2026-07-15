using Base.Core;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace Base.Components;

/// <summary>
/// Shared parsing, formatting and input-filtering helpers used by the per-type config editors.
/// </summary>
internal static class ConfigEditorUtil
{
    public static readonly HashSet<Type> IntegerTypes = new()
    {
        typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
        typeof(int), typeof(uint), typeof(long), typeof(ulong),
    };

    public static readonly HashSet<Type> FloatTypes = new()
    {
        typeof(float), typeof(double), typeof(decimal),
    };

    public static readonly HashSet<Type> SignedTypes = new()
    {
        typeof(sbyte), typeof(short), typeof(int), typeof(long),
        typeof(float), typeof(double), typeof(decimal),
    };

    #region Formatting

    public static string FormatValue(object value)
    {
        if (value == null) return string.Empty;
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    /// <summary>
    /// Formats a value as a <c>0x</c>-prefixed hexadecimal string. When <paramref name="minDigits"/>
    /// is positive the digits are left-padded with zeros to that width, preserving leading zeros
    /// (used by the <see cref="ConfigType.Short"/> / <see cref="ConfigType.Hex_RGB"/> /
    /// <see cref="ConfigType.Hex_RGBA"/> modes); otherwise leading zeros are collapsed.
    /// </summary>
    public static string FormatHex(object value, int minDigits = 0)
    {
        if (value == null)
            return "0x" + (minDigits > 0 ? new string('0', minDigits) : "0");

        try
        {
            string digits = string.Format(CultureInfo.InvariantCulture, "{0:X}", value);
            if (minDigits > 0 && digits.Length < minDigits)
                digits = digits.PadLeft(minDigits, '0');
            return "0x" + digits;
        }
        catch { return FormatValue(value); }
    }

    /// <summary><c>true</c> when the config type is one of the hexadecimal editor modes.</summary>
    public static bool IsHex(ConfigType type)
        => type is ConfigType.Hex or ConfigType.Short or ConfigType.Hex_RGB or ConfigType.Hex_RGBA;

    /// <summary>
    /// Number of hex digits the display is zero-padded to for a given mode. Plain
    /// <see cref="ConfigType.Hex"/> pads to 2 (a full byte, e.g. <c>0x00</c> / <c>0x01</c>); the
    /// wider modes pad further. Returns 0 for non-hex modes.
    /// </summary>
    public static int HexDigits(ConfigType type) => type switch
    {
        ConfigType.Hex => 2,
        ConfigType.Short => 4,
        ConfigType.Hex_RGB => 6,
        ConfigType.Hex_RGBA => 8,
        _ => 0,
    };

    #endregion

    #region Parsing / validation

    public static bool TryParseNumeric(string text, Type type, FieldAttribute attr, out object value, out string error)
    {
        value = null;
        error = null;
        text = text?.Trim() ?? string.Empty;

        // An empty (or partial) entry is not an error — default it to 0, then range-check.
        if (text.Length == 0 || text == "-" || text == "." || text == "-.")
        {
            value = Convert.ChangeType(0, type, CultureInfo.InvariantCulture);
            return CheckRange(0, attr, out error);
        }

        try
        {
            value = Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
        }
        catch
        {
            error = "Not a valid number.";
            return false;
        }

        double d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        return CheckRange(d, attr, out error);
    }

    public static bool TryParseHex(string text, Type type, FieldAttribute attr, out object value, out string error)
    {
        value = null;
        error = null;
        text = (text ?? string.Empty).Trim();

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text.Substring(2);

        // An empty entry is not an error — default it to 0.
        if (text.Length == 0)
            text = "0";

        if (!Regex.IsMatch(text, "^[0-9A-Fa-f]+$"))
        {
            error = "Not a valid hexadecimal value.";
            return false;
        }

        try
        {
            const NumberStyles hex = NumberStyles.HexNumber;
            var inv = CultureInfo.InvariantCulture;
            value = type switch
            {
                _ when type == typeof(byte) => byte.Parse(text, hex, inv),
                _ when type == typeof(sbyte) => sbyte.Parse(text, hex, inv),
                _ when type == typeof(short) => short.Parse(text, hex, inv),
                _ when type == typeof(ushort) => ushort.Parse(text, hex, inv),
                _ when type == typeof(int) => int.Parse(text, hex, inv),
                _ when type == typeof(uint) => uint.Parse(text, hex, inv),
                _ when type == typeof(long) => long.Parse(text, hex, inv),
                _ when type == typeof(ulong) => ulong.Parse(text, hex, inv),
                _ => Convert.ChangeType(ulong.Parse(text, hex, inv), type, inv),
            };
        }
        catch (OverflowException)
        {
            error = $"Value is out of range for {type.Name}.";
            return false;
        }
        catch
        {
            error = "Not a valid hexadecimal value.";
            return false;
        }

        double d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
        return CheckRange(d, attr, out error);
    }

    public static bool TryParseText(string text, Type type, FieldAttribute attr, out object value, out string error)
    {
        value = null;
        error = null;
        text ??= string.Empty;

        if (type == typeof(string))
        {
            if (attr.HasMin && text.Length < (int)attr.Min)
            {
                error = $"Must be at least {(int)attr.Min} character(s).";
                return false;
            }
            if (attr.HasMax && text.Length > (int)attr.Max)
            {
                error = $"Must be at most {(int)attr.Max} character(s).";
                return false;
            }
            if (!string.IsNullOrEmpty(attr.Regex))
            {
                try
                {
                    if (!Regex.IsMatch(text, attr.Regex))
                    {
                        error = "Value does not match the required format.";
                        return false;
                    }
                }
                catch (ArgumentException)
                {
                    // Invalid regex in the attribute — treat as no constraint rather than blocking.
                }
            }
            value = text;
            return true;
        }

        // Unknown type: best-effort conversion.
        try
        {
            value = Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            error = $"Cannot convert to {type.Name}.";
            return false;
        }
    }

    public static bool CheckRange(double d, FieldAttribute attr, out string error)
    {
        error = null;
        if (attr.HasMin && d < attr.Min)
        {
            error = $"Must be at least {attr.Min:0.###}.";
            return false;
        }
        if (attr.HasMax && d > attr.Max)
        {
            error = $"Must be at most {attr.Max:0.###}.";
            return false;
        }
        return true;
    }

    #endregion

    #region Numeric helpers

    public static double ToDouble(object value)
    {
        try { return value == null ? 0 : Convert.ToDouble(value, CultureInfo.InvariantCulture); }
        catch { return 0; }
    }

    public static double Clamp(double value, double min, double max)
        => value < min ? min : value > max ? max : value;

    public static int ReadInt(TextBox box, int fallback)
        => int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    #endregion

    #region Input filtering

    public static void AttachNumericFilter(TextBox box, bool allowNegative, bool allowDecimal)
    {
        box.PreviewTextInput += (s, e) =>
        {
            if (!IsAcceptableNumericText(ProjectedText(box, e.Text), allowNegative, allowDecimal))
                e.Handled = true;
        };
        AttachPasteGuard(box, text => IsAcceptableNumericText(text, allowNegative, allowDecimal));
    }

    public static void AttachHexFilter(TextBox box)
    {
        box.PreviewTextInput += (s, e) =>
        {
            if (!IsAcceptableHexText(ProjectedText(box, e.Text)))
                e.Handled = true;
        };
        AttachPasteGuard(box, IsAcceptableHexText);
    }

    private static void AttachPasteGuard(TextBox box, Func<string, bool> accept)
    {
        DataObject.AddPastingHandler(box, (s, e) =>
        {
            if (e.DataObject.GetDataPresent(DataFormats.UnicodeText))
            {
                var pasted = (string)e.DataObject.GetData(DataFormats.UnicodeText);
                if (!accept(ProjectedText(box, pasted)))
                    e.CancelCommand();
            }
            else
            {
                e.CancelCommand();
            }
        });
    }

    /// <summary>Computes what the text box would contain if <paramref name="insert"/> replaced the selection.</summary>
    private static string ProjectedText(TextBox box, string insert)
    {
        string text = box.Text ?? string.Empty;
        int start = box.SelectionStart;
        int length = box.SelectionLength;
        return text.Substring(0, start) + insert + text.Substring(start + length);
    }

    private static bool IsAcceptableNumericText(string text, bool allowNegative, bool allowDecimal)
    {
        if (text.Length == 0) return true;

        string pattern = allowNegative ? "^-?" : "^";
        pattern += allowDecimal ? @"\d*\.?\d*$" : @"\d*$";
        return Regex.IsMatch(text, pattern);
    }

    private static bool IsAcceptableHexText(string text)
    {
        if (text.Length == 0) return true;
        // Allow an optional 0x prefix followed by hex digits, partial input included.
        return Regex.IsMatch(text, "^(0[xX])?[0-9A-Fa-f]*$");
    }

    #endregion
}
