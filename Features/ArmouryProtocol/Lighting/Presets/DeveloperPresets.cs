using System.Collections.Generic;
using Base.Framework.Utilities;

namespace ArmouryProtocol.Lighting.Presets;

/// <summary>
/// Developer diagnostic strategies. These span the manually configured frame
/// count (FrameCount = 0 => use the page's frame-count setting) and key off the
/// firmware matrix rather than physical position.
/// </summary>
public static class DeveloperPresets
{
    private const LightingCategory Cat = LightingCategory.Developer;

    private static readonly (byte, byte, byte) White = (0xFF, 0xFF, 0xFF);
    private static readonly (byte, byte, byte) Red = (0xFF, 0x00, 0x00);
    private static readonly (byte, byte, byte) Off = (0x00, 0x00, 0x00);

    // Matrix cells that spell out each decimal digit of the current frame number.
    private static readonly Vector2Int[] TensMap =
        { new(3, 1), new(4, 1), new(5, 1), new(6, 1), new(7, 1), new(8, 1), new(9, 1), new(10, 1), new(11, 1), new(12, 1) };
    private static readonly Vector2Int[] HundredsMap =
        { new(3, 2), new(4, 2), new(5, 2), new(6, 2), new(7, 2), new(8, 2), new(9, 2), new(10, 2), new(11, 2), new(12, 2) };
    private static readonly Vector2Int[] ThousandsMap =
        { new(3, 3), new(4, 3), new(5, 3), new(6, 3), new(7, 3), new(8, 3), new(9, 3), new(10, 3), new(11, 3), new(12, 3) };
    private static readonly Vector2Int[] TenThousandsMap =
        { new(4, 4), new(5, 4), new(6, 4), new(7, 4), new(8, 4), new(9, 4), new(10, 4), new(11, 4), new(12, 4), new(13, 4) };

    public static IEnumerable<LightingPreset> Create()
    {
        // Lights the matrix cells representing each digit of the frame number.
        yield return new FuncPreset("FrameScan (frame #)", Cat, 0,
            (frameIndex, frameCount, k) =>
            {
                int tens = frameIndex % 10;
                int hundreds = (frameIndex / 10) % 10;
                int thousands = (frameIndex / 100) % 10;
                int tenThousands = (frameIndex / 1000) % 10;

                bool on = MatchesCell(TensMap[tens], k)
                       || MatchesCell(HundredsMap[hundreds], k)
                       || MatchesCell(ThousandsMap[thousands], k)
                       || MatchesCell(TenThousandsMap[tenThousands], k);

                return on ? White : Off;
            });

        // Lights a single key per frame, sweeping through the global key index. Because
        // the page generates every key index, this probes EVERY LED - including ones
        // with no on-screen keycap (light bars, extra space-bar segments). Scrub and
        // watch the real keyboard to discover which frame index drives a physical LED.
        yield return new FuncPreset("KeyScan (index sweep)", Cat, 0,
            (frameIndex, frameCount, k) => k.GlobalKeyIndex == frameIndex ? Red : Off);
    }

    private static bool MatchesCell(Vector2Int cell, in KeyLightInfo key)
        => cell.x == key.MatrixX && cell.y == key.MatrixY;
}
