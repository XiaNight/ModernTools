using Base.Core;

namespace MouseATE.Settings;

public static class AteSettingsStore
{
    private const string KEY_GLOBAL        = "MouseATE.GlobalSettings";
    private const string KEY_RELAY         = "MouseATE.RelaySettings";
    private const string KEY_CONNECTION = "MouseATE.ConnectionSettings";
    private const string KEY_PROFILES = "MouseATE.DeviceProfiles";
    private const string KEY_ACTIVE_PROFILE = "MouseATE.ActiveProfileIndex";

    public static AteRelaySettings Relay
    {
        get => LocalAppDataStore.Instance.Get<AteRelaySettings>(KEY_RELAY) ?? new();
        set => LocalAppDataStore.Instance.Set(KEY_RELAY, value);
    }

    public static AteGlobalSettings Global
    {
        get => LocalAppDataStore.Instance.Get<AteGlobalSettings>(KEY_GLOBAL) ?? new();
        set => LocalAppDataStore.Instance.Set(KEY_GLOBAL, value);
    }

    public static AteConnectionSettings Connection
    {
        get => LocalAppDataStore.Instance.Get<AteConnectionSettings>(KEY_CONNECTION) ?? new();
        set => LocalAppDataStore.Instance.Set(KEY_CONNECTION, value);
    }

    public static List<AteDeviceProfile> Profiles
    {
        get => LocalAppDataStore.Instance.Get<List<AteDeviceProfile>>(KEY_PROFILES) ?? new() { new() };
        set => LocalAppDataStore.Instance.Set(KEY_PROFILES, value);
    }

    public static int ActiveProfileIndex
    {
        get => LocalAppDataStore.Instance.Get<int>(KEY_ACTIVE_PROFILE);
        set => LocalAppDataStore.Instance.Set(KEY_ACTIVE_PROFILE, value);
    }

    public static AteDeviceProfile ActiveProfile
    {
        get
        {
            var profiles = Profiles;
            int idx = ActiveProfileIndex;
            return (idx >= 0 && idx < profiles.Count) ? profiles[idx] : profiles[0];
        }
    }
}
