using System;
using System.Collections.Generic;
using Base.Framework.Utilities;
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

        // Water caustics: a Voronoi (Worley) cell network whose sample space is
        // domain-warped by smooth value noise, so the bright cell-edge veins ripple
        // like light on a pool floor. Time enters as a circular path (cos/sin of the
        // phase) through the warp noise, so the animation loops seamlessly.
        yield return new FuncPreset("Caustics", Cat, 480,
            (f, fc, k) =>
            {
                float caustic = CausticBrightness(k.MatrixX / 4.0f, k.MatrixY / 4.0f, Phase(f, fc));
                float v = caustic; // faint teal base + bright veins
                return HsvToRgb(160f, 1f, Clamp01(v));
            });

        // Hue rotates around the center like a pinwheel.
        yield return new FuncPreset("Pinwheel", Cat, 120,
            (f, fc, k) => HsvToRgb((k.AngleTurns + Phase(f, fc)) * 360f, 1f, 1f));
    }

    // --- Caustics composition ---------------------------------------------------

    // Brightness (0..1) of the caustic network at a position and phase.
    // Composes the Base noise primitives: Perlin domain-warp feeding a Voronoi lookup.
    private static float CausticBrightness(float px, float py, float phase)
    {
        // Time as a circular path through noise space -> seamless loop.
        float ang = phase * TwoPi;
        float c = MathF.Cos(ang) * 0.6f;
        float s = MathF.Sin(ang) * 0.6f;

        // Domain warp with Perlin, then look up cellular (Voronoi) noise.
        const float warpFreq = 1.3f;
        const float warpAmp = 0.9f;
        float wx = Noise.Perlin((px * warpFreq) + 11.3f + c, (py * warpFreq) + 4.7f + s);
        float wy = Noise.Perlin((px * warpFreq) + 23.1f - s, (py * warpFreq) + 9.2f + c);
        float qx = px + (warpAmp * ((wx * 2f) - 1f));
        float qy = py + (warpAmp * ((wy * 2f) - 1f));

        (float f1, float f2) = Noise.Voronoi(qx, qy);

        // Bright thin veins along the cell borders (where f2 ~ f1).
        float veins = 1f - Clamp01((f2 - f1) / 0.50f);
        return veins * veins; // sharpen
    }
}
