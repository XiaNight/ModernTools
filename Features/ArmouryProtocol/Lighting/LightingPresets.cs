using System.Collections.Generic;
using System.Linq;
using ArmouryProtocol.Lighting.Presets;

namespace ArmouryProtocol.Lighting;

/// <summary>
/// Registry of all lighting presets, ordered Static -> Dynamic -> Developer
/// (which is also how they group in the picker). Add new effects by extending the
/// relevant *Presets class.
/// </summary>
public static class LightingPresets
{
    public static IReadOnlyList<LightingPreset> All { get; } =
        StaticPresets.Create()
            .Concat(DynamicPresets.Create())
            .Concat(DeveloperPresets.Create())
            .ToArray();

    /// <summary>Preset selected on load: the FrameScan developer strategy.</summary>
    public static LightingPreset Default { get; } =
        All.First(p => p.Category == LightingCategory.Developer);
}
