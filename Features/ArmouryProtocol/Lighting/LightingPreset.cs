using System;

namespace ArmouryProtocol.Lighting;

public enum LightingCategory
{
    Static,
    Dynamic,
    Developer,
}

/// <summary>
/// A named lighting effect. Given a frame and a key, it returns that key's color.
///
/// The color function takes the raw <paramref name="frameIndex"/> and total
/// <c>frameCount</c> so developer strategies can key off the exact frame number,
/// while spatial presets derive a normalized phase = frameIndex / frameCount.
/// </summary>
public abstract class LightingPreset
{
    public abstract string Name { get; }

    public abstract LightingCategory Category { get; }

    /// <summary>
    /// Number of frames the effect spans. 1 for a still image. 0 means "use the
    /// page's manual frame-count setting" (used by developer strategies).
    /// </summary>
    public abstract int FrameCount { get; }

    /// <summary>
    /// Returns the color for one key on one frame. Called for EVERY addressable key
    /// index by the page (keys not in the layout arrive with a default 0,0 position
    /// but a valid GlobalKeyIndex / matrix cell), so every preset inherently covers
    /// all keys and the packets are always complete.
    /// </summary>
    public abstract (byte r, byte g, byte b) GetColor(int frameIndex, int frameCount, in KeyLightInfo key);
}

/// <summary>
/// Concrete preset backed by a delegate, so effects can be declared as one-liners
/// in the registry instead of a class each.
/// </summary>
public sealed class FuncPreset : LightingPreset
{
    private readonly Func<int, int, KeyLightInfo, (byte r, byte g, byte b)> colorFunc;

    public override string Name { get; }
    public override LightingCategory Category { get; }
    public override int FrameCount { get; }

    public FuncPreset(string name, LightingCategory category, int frameCount,
                      Func<int, int, KeyLightInfo, (byte r, byte g, byte b)> colorFunc)
    {
        Name = name;
        Category = category;
        FrameCount = frameCount;
        this.colorFunc = colorFunc;
    }

    public override (byte r, byte g, byte b) GetColor(int frameIndex, int frameCount, in KeyLightInfo key)
        => colorFunc(frameIndex, frameCount, key);

    public override string ToString() => Name;
}
