using System;

namespace ArmouryProtocol.Lighting;

/// <summary>
/// Everything a lighting preset needs to know about one key, resolved once when
/// the keyboard is built and cached (keyed by <see cref="GlobalKeyIndex"/>).
///
/// It carries both worlds:
///   - the firmware light matrix cell (<see cref="MatrixX"/>/<see cref="MatrixY"/>
///     and <see cref="GlobalKeyIndex"/>), used by developer strategies, and
///   - the physical keycap position from the KLE layout, normalized to 0..1 across
///     the whole keyboard (<see cref="NormX"/>/<see cref="NormY"/>), used by the
///     spatial presets so effects line up with the real key positions.
/// </summary>
public readonly struct KeyLightInfo
{
    public int GlobalKeyIndex { get; }
    public int MatrixX { get; }
    public int MatrixY { get; }

    /// <summary>Physical center, normalized 0..1 left-to-right.</summary>
    public float NormX { get; }
    /// <summary>Physical center, normalized 0..1 top-to-bottom.</summary>
    public float NormY { get; }

    /// <summary>Physical center in raw KLE layout units (unnormalized).</summary>
    public float CenterX { get; }
    public float CenterY { get; }

    public KeyLightInfo(int globalKeyIndex, int matrixX, int matrixY,
                        float normX, float normY, float centerX, float centerY)
    {
        GlobalKeyIndex = globalKeyIndex;
        MatrixX = matrixX;
        MatrixY = matrixY;
        NormX = normX;
        NormY = normY;
        CenterX = centerX;
        CenterY = centerY;
    }

    // Position relative to the keyboard center (range roughly -0.5..0.5).
    private float Dx => NormX - 0.5f;
    private float Dy => NormY - 0.5f;

    /// <summary>Distance from center, ~0 at the middle and ~1 at the corners.</summary>
    public float Radius => MathF.Min(1f, MathF.Sqrt((Dx * Dx) + (Dy * Dy)) / 0.7071f);

    /// <summary>Angle around the center as a fraction of a full turn, 0..1.</summary>
    public float AngleTurns
    {
        get
        {
            float turns = MathF.Atan2(Dy, Dx) / (MathF.PI * 2f);
            return turns < 0f ? turns + 1f : turns;
        }
    }
}
