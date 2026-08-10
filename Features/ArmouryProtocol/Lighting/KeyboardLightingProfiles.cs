using ArmouryProtocol.Lighting.Profiles;

namespace ArmouryProtocol.Lighting;

/// <summary>
/// Registry of the lighting profiles the app knows about.
///
/// Model switching (auto-detect / user selection) is not wired up yet; the
/// lighting page uses <see cref="Default"/>. When switching is added, resolve a
/// profile via <see cref="TryGet"/> using the connected device's model name.
/// To support a new keyboard, add its profile instance to <see cref="ByModel"/>.
/// </summary>
public static class KeyboardLightingProfiles
{
    /// <summary>The M708 profile (the model currently under test).</summary>
    public static KeyboardLightingProfile M708 { get; } = new M708LightingProfile();

    /// <summary>Profile used until model switching is implemented.</summary>
    public static KeyboardLightingProfile Default => M708;

    // Keyed by ModelName. Add new models here as their profiles come online.
    private static readonly Dictionary<string, KeyboardLightingProfile> ByModel =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [M708.ModelName] = M708,
        };

    /// <summary>Looks up a profile by model name.</summary>
    public static bool TryGet(string modelName, out KeyboardLightingProfile profile)
    {
        if (modelName != null)
            return ByModel.TryGetValue(modelName, out profile);

        profile = null;
        return false;
    }
}
