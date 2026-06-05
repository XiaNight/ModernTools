using MouseATE.Settings;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MouseATE.Pages;

public partial class AteSettingsView : UserControl
{
    public AteSettingsView()
    {
        InitializeComponent();
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        LoadGlobalSettings();
        RefreshProfileList();
    }

    // ── Global settings ──────────────────────────────────────────────────

    private void LoadGlobalSettings()
    {
        var g = AteSettingsStore.Global;
        GTestDistanceBox.Text = g.TestDistance.ToString();
        GTestSpeedBox.Text    = g.TestSpeed.ToString();
        GTestCyclesBox.Text   = g.TestCycles.ToString();
        GZOffsetBox.Text      = g.ZOffset.ToString();
        GThresholdBox.Text    = g.DeviationThresholdPct.ToString();
        GLodHeightBox.Text    = g.LodHeight.ToString();
        GLodIntervalBox.Text  = g.LodInterval.ToString();
    }

    private void SaveGlobal_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(GTestDistanceBox.Text, out double dist)
            || !int.TryParse(GTestSpeedBox.Text, out int speed)
            || !int.TryParse(GTestCyclesBox.Text, out int cycles)
            || !double.TryParse(GZOffsetBox.Text, out double zOff)
            || !double.TryParse(GThresholdBox.Text, out double thr)
            || !double.TryParse(GLodHeightBox.Text, out double lodH)
            || !double.TryParse(GLodIntervalBox.Text, out double lodI))
        {
            SetStatus(GlobalSaveStatus, "Invalid values.", Colors.Red);
            return;
        }

        AteSettingsStore.Global = new AteGlobalSettings
        {
            TestDistance = dist,
            TestSpeed = speed,
            TestCycles = cycles,
            ZOffset = zOff,
            DeviationThresholdPct = thr,
            LodHeight = lodH,
            LodInterval = lodI
        };
        SetStatus(GlobalSaveStatus, "Saved.", Colors.LimeGreen);
    }

    // ── Device profiles ──────────────────────────────────────────────────

    private void RefreshProfileList()
    {
        var profiles = AteSettingsStore.Profiles;
        ProfileList.ItemsSource = null;
        ProfileList.ItemsSource = profiles;

        int active = AteSettingsStore.ActiveProfileIndex;
        ProfileList.SelectedIndex = (active >= 0 && active < profiles.Count) ? active : 0;
    }

    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var profile = ProfileList.SelectedItem as AteDeviceProfile;
        if (profile == null) { ProfileEditPanel.IsEnabled = false; return; }
        AteSettingsStore.ActiveProfileIndex = ProfileList.SelectedIndex;
        LoadProfileIntoForm(profile);
        ProfileEditPanel.IsEnabled = true;
    }

    private void LoadProfileIntoForm(AteDeviceProfile p)
    {
        PNameBox.Text      = p.Name;
        PDevPidBox.Text    = p.DevicePid.ToString("X4");
        PDonglePidBox.Text = p.DonglePid.ToString("X4");
        PUsbIfBox.Text     = p.UsbInterface;
        PCalDpiBox.Text    = p.CalibrationDpi.ToString();
        PDpiStepBox.Text   = p.DpiStep.ToString();
        PMaxLodBox.Text    = p.MaxLodLevel.ToString();
        PMrDpisBox.Text    = string.Join(",", p.MediaReviewDpis);
        PFtMinBox.Text     = p.FullTestMinDpi.ToString();
        PFtMaxBox.Text     = p.FullTestMaxDpi.ToString();
        PFtStepBox.Text    = p.FullTestDpiStep.ToString();
        ProfileSaveStatus.Text = "";
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var profiles = AteSettingsStore.Profiles;
        profiles.Add(new AteDeviceProfile { Name = $"Profile {profiles.Count + 1}" });
        AteSettingsStore.Profiles = profiles;
        RefreshProfileList();
        ProfileList.SelectedIndex = ProfileList.Items.Count - 1;
    }

    private void RemoveProfile_Click(object sender, RoutedEventArgs e)
    {
        var profiles = AteSettingsStore.Profiles;
        if (profiles.Count <= 1) { SetStatus(ProfileSaveStatus, "Cannot remove last profile.", Colors.Orange); return; }
        int idx = ProfileList.SelectedIndex;
        if (idx < 0 || idx >= profiles.Count) return;
        profiles.RemoveAt(idx);
        AteSettingsStore.Profiles = profiles;
        AteSettingsStore.ActiveProfileIndex = Math.Max(0, idx - 1);
        RefreshProfileList();
    }

    private void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        int idx = ProfileList.SelectedIndex;
        var profiles = AteSettingsStore.Profiles;
        if (idx < 0 || idx >= profiles.Count) return;

        if (!int.TryParse(PCalDpiBox.Text, out int calDpi)
            || !int.TryParse(PDpiStepBox.Text, out int dpiStep)
            || !int.TryParse(PMaxLodBox.Text, out int maxLod)
            || !int.TryParse(PFtMinBox.Text, out int ftMin)
            || !int.TryParse(PFtMaxBox.Text, out int ftMax)
            || !int.TryParse(PFtStepBox.Text, out int ftStep))
        {
            SetStatus(ProfileSaveStatus, "Invalid numeric values.", Colors.Red);
            return;
        }

        var mrDpis = new List<int>();
        foreach (var part in PMrDpisBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(part.Trim(), out int d))
                mrDpis.Add(d);

        int devPid = 0, donglePid = 0;
        int.TryParse(PDevPidBox.Text, System.Globalization.NumberStyles.HexNumber, null, out devPid);
        int.TryParse(PDonglePidBox.Text, System.Globalization.NumberStyles.HexNumber, null, out donglePid);

        profiles[idx] = new AteDeviceProfile
        {
            Name            = PNameBox.Text.Trim(),
            DevicePid       = devPid,
            DonglePid       = donglePid,
            UsbInterface    = PUsbIfBox.Text.Trim(),
            CalibrationDpi  = calDpi,
            DpiStep         = dpiStep,
            MaxLodLevel     = maxLod,
            MediaReviewDpis = mrDpis,
            FullTestMinDpi  = ftMin,
            FullTestMaxDpi  = ftMax,
            FullTestDpiStep = ftStep
        };
        AteSettingsStore.Profiles = profiles;
        RefreshProfileList();
        SetStatus(ProfileSaveStatus, "Saved.", Colors.LimeGreen);
    }

    private static void SetStatus(TextBlock tb, string msg, Color color)
    {
        tb.Text = msg;
        tb.Foreground = new SolidColorBrush(color);
    }
}
