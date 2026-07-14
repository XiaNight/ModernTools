using System;
using System.Collections.Generic;
using static ArmouryProtocol.Lighting.ColorMath;

namespace ArmouryProtocol.Lighting.Presets;

/// <summary>
/// The 8 static (single-frame) presets. All colors are a pure function of the
/// key's normalized physical position, so the result matches the real layout.
/// </summary>
public static class StaticPresets
{
    private const LightingCategory Cat = LightingCategory.Static;

    // Two-tone gradient endpoints.
    private static readonly (byte, byte, byte) Magenta = (255, 0, 200);
    private static readonly (byte, byte, byte) Cyan = (0, 220, 255);
    private static readonly (byte, byte, byte) Amber = (255, 150, 0);
    private static readonly (byte, byte, byte) Violet = (150, 0, 255);
    private static readonly (byte, byte, byte) White = (255, 255, 255);

    public static IEnumerable<LightingPreset> Create()
    {
        // Coverage/diagnostic preset: white on EVERY addressable key. Because the page
        // generates every key index (mapped or not), this is guaranteed to send white
        // to every possible key, including keys with multiple LED segments (e.g. the
        // space bar). If a key stays dark under this, it's a hardware/firmware issue
        // (or the key lives outside keyCount) rather than a mapping gap.
        yield return new FuncPreset("All Keys White (coverage)", Cat, 1,
            (f, fc, k) => White);

        yield return new FuncPreset("Static Rainbow", Cat, 1,
            (f, fc, k) => HsvToRgb(k.NormX * 360f, 1f, 1f));

        yield return new FuncPreset("Diagonal Rainbow", Cat, 1,
            (f, fc, k) => HsvToRgb((k.NormX + k.NormY) * 0.5f * 360f, 1f, 1f));

        yield return new FuncPreset("Radial Rainbow", Cat, 1,
            (f, fc, k) => HsvToRgb(k.Radius * 360f, 1f, 1f));

        yield return new FuncPreset("Horizontal Gradient", Cat, 1,
            (f, fc, k) => Lerp(Magenta, Cyan, k.NormX));

        yield return new FuncPreset("Vertical Gradient", Cat, 1,
            (f, fc, k) => Lerp(Amber, Violet, k.NormY));

        // Bright center fading to dark edges.
        yield return new FuncPreset("Center Spotlight", Cat, 1,
            (f, fc, k) => HsvToRgb(200f, 0.7f, Clamp01(1f - k.Radius)));

        // Dark center brightening toward the edges.
        yield return new FuncPreset("Edge Glow", Cat, 1,
            (f, fc, k) => HsvToRgb(280f, 0.8f, Clamp01(k.Radius)));

        // Spectrum quantized into vertical color blocks.
        yield return new FuncPreset("Color Bands", Cat, 1,
            (f, fc, k) =>
            {
                const int bands = 7;
                float band = MathF.Floor(Clamp01(k.NormX) * bands) / bands;
                return HsvToRgb(band * 360f, 1f, 1f);
            });
    }
}
