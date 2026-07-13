using System;
using System.Collections.Generic;
using static ArmouryProtocol.Lighting.ColorMath;

namespace ArmouryProtocol.Lighting.Presets;

/// <summary>
/// The 8 dynamic (multi-frame) presets. Every color function is periodic in
/// phase (frameIndex / frameCount), so each animation loops seamlessly. Effects
/// use the key's normalized physical position, so motion tracks the real layout.
/// </summary>
public static class DynamicPresets
{
    private const LightingCategory Cat = LightingCategory.Dynamic;

    private static float Phase(int frameIndex, int frameCount)
        => frameCount > 0 ? (float)frameIndex / frameCount : 0f;

    public static IEnumerable<LightingPreset> Create()
    {
        // Rainbow scrolling left -> right.
        yield return new FuncPreset("Cycling Rainbow", Cat, 96,
            (f, fc, k) => HsvToRgb((k.NormX * 360f) + (Phase(f, fc) * 360f), 1f, 1f));

        // Rainbow scrolling top -> bottom.
        yield return new FuncPreset("Vertical Rainbow Flow", Cat, 96,
            (f, fc, k) => HsvToRgb((k.NormY * 360f) + (Phase(f, fc) * 360f), 1f, 1f));

        // Whole board a single hue, cycling through the spectrum.
        yield return new FuncPreset("Spectrum Cycle", Cat, 120,
            (f, fc, k) => HsvToRgb(Phase(f, fc) * 360f, 1f, 1f));

        // Board fades in and out while the hue slowly drifts.
        yield return new FuncPreset("Breathing", Cat, 120,
            (f, fc, k) =>
            {
                float phase = Phase(f, fc);
                float v = 0.5f - (0.5f * MathF.Cos(phase * TwoPi)); // 0 -> 1 -> 0
                return HsvToRgb(phase * 360f, 1f, v);
            });

        // A smooth brightness wave travelling across X (single hue).
        yield return new FuncPreset("Wave", Cat, 96,
            (f, fc, k) =>
            {
                float w = 0.5f + (0.5f * MathF.Sin((k.NormX - Phase(f, fc)) * TwoPi));
                return HsvToRgb(210f, 1f, w);
            });

        // A bright bar sweeping left <-> right and bouncing.
        yield return new FuncPreset("Scanner", Cat, 192,
            (f, fc, k) =>
            {
                float phase = Phase(f, fc);
                float bar = 1f - MathF.Abs(1f - (2f * phase)); // triangle 0 -> 1 -> 0
                float dist = MathF.Abs(k.NormX - bar);
                float v = Clamp01(1f - (dist * 20f));           // narrow bar
                return HsvToRgb(0f, 1f, v);
            });

        // Brightness rings pulsing outward from the center.
        yield return new FuncPreset("Radial Ripple", Cat, 120,
            (f, fc, k) =>
            {
                float v = 0.5f + (0.5f * MathF.Sin(((k.Radius * 3f) - Phase(f, fc)) * TwoPi));
                return HsvToRgb(160f, 1f, Clamp01(v));
            });

        // Hue rotates around the center like a pinwheel.
        yield return new FuncPreset("Pinwheel", Cat, 120,
            (f, fc, k) => HsvToRgb((k.AngleTurns + Phase(f, fc)) * 360f, 1f, 1f));
    }
}
