using System;

namespace ArmouryProtocol.Lighting;

/// <summary>Small color/animation helpers shared by the lighting presets.</summary>
public static class ColorMath
{
    public const float TwoPi = MathF.PI * 2f;

    public static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);

    /// <summary>Fractional part in [0,1), handling negatives (for scrolling/wrapping).</summary>
    public static float Frac(float x)
    {
        float f = x - MathF.Floor(x);
        return f < 0f ? f + 1f : f;
    }

    /// <summary>
    /// HSV to RGB. Hue in degrees (wraps), saturation and value in 0..1.
    /// </summary>
    public static (byte r, byte g, byte b) HsvToRgb(float hueDegrees, float saturation, float value)
    {
        float h = Frac(hueDegrees / 360f) * 6f;   // 0..6
        float s = Clamp01(saturation);
        float v = Clamp01(value);

        float c = v * s;
        float x = c * (1f - MathF.Abs((h % 2f) - 1f));
        float m = v - c;

        float r, g, b;
        switch ((int)h)
        {
            case 0: r = c; g = x; b = 0; break;
            case 1: r = x; g = c; b = 0; break;
            case 2: r = 0; g = c; b = x; break;
            case 3: r = 0; g = x; b = c; break;
            case 4: r = x; g = 0; b = c; break;
            default: r = c; g = 0; b = x; break;   // 5 (and h==6 edge)
        }

        return (ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    public static (byte r, byte g, byte b) Lerp(
        (byte r, byte g, byte b) a, (byte r, byte g, byte b) b, float t)
    {
        t = Clamp01(t);
        return (LerpByte(a.r, b.r, t), LerpByte(a.g, b.g, t), LerpByte(a.b, b.b, t));
    }

    private static byte LerpByte(byte a, byte b, float t)
        => ToByteRaw(a + ((b - a) * t));

    private static byte ToByte(float unit) => ToByteRaw(unit * 255f);

    private static byte ToByteRaw(float scaled)
        => (byte)Math.Clamp((int)MathF.Round(scaled), 0, 255);
}
