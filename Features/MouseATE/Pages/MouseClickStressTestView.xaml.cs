using Base.Services;
using Microsoft.Win32;
using MouseATE.Hardware;
using MouseATE.Settings;
using MouseATE.Tests;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MouseATE.Pages;

public partial class MouseClickStressTestView : UserControl
{
    private static readonly RelayApiClient _relay = new();

    private MouseHookService         _hook;
    private CancellationTokenSource  _cts;
    private ClickTestResult          _lastLeftResult;
    private ClickTestResult          _lastRightResult;

    public MouseClickStressTestView()
    {
        InitializeComponent();
    }

    internal void SetHook(MouseHookService hook) => _hook = hook;

    internal void StopIfRunning() => _cts?.Cancel();

    private static void AppendLog(string msg) => Debug.Log("[ATE/ClickTest] " + msg);

    // ── Lifecycle ─────────────────────────────────────────────────────────

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        foreach (string label in RelayApiClient.SlotLabels)
        {
            LeftRelayCombo.Items.Add(label);
            RightRelayCombo.Items.Add(label);
        }

        LoadSettings();
        UpdateEstimate();

        CyclesBox.TextChanged    += (_, _) => UpdateEstimate();
        SolenoidOnBox.TextChanged += (_, _) => UpdateEstimate();
        WindowBox.TextChanged    += (_, _) => UpdateEstimate();
        CooldownBox.TextChanged  += (_, _) => UpdateEstimate();
        TestLeftChk.Checked      += (_, _) => UpdateEstimate();
        TestLeftChk.Unchecked    += (_, _) => UpdateEstimate();
        TestRightChk.Checked     += (_, _) => UpdateEstimate();
        TestRightChk.Unchecked   += (_, _) => UpdateEstimate();
    }

    private void LoadSettings()
    {
        var s = AteSettingsStore.Relay;
        LeftRelayCombo.SelectedIndex  = Math.Clamp(s.LeftRelaySlot,  0, 5);
        RightRelayCombo.SelectedIndex = Math.Clamp(s.RightRelaySlot, 0, 5);
        CyclesBox.Text        = s.TotalClicks.ToString();
        SolenoidOnBox.Text    = s.SolenoidOnMs.ToString();
        WindowBox.Text        = s.ClickWindowMs.ToString();
        CooldownBox.Text      = s.CoolDownMs.ToString();
        TestLeftChk.IsChecked  = s.TestLeftButton;
        TestRightChk.IsChecked = s.TestRightButton;
    }

    private void SaveSettings()
    {
        AteSettingsStore.Relay = new AteRelaySettings
        {
            LeftRelaySlot   = LeftRelayCombo.SelectedIndex,
            RightRelaySlot  = RightRelayCombo.SelectedIndex,
            TotalClicks     = int.TryParse(CyclesBox.Text, out int cyc)       ? cyc : 1000,
            SolenoidOnMs    = int.TryParse(SolenoidOnBox.Text, out int son)   ? son : 100,
            ClickWindowMs   = int.TryParse(WindowBox.Text, out int win)       ? win : 50,
            CoolDownMs      = int.TryParse(CooldownBox.Text, out int cd)      ? cd  : 200,
            TestLeftButton  = TestLeftChk.IsChecked == true,
            TestRightButton = TestRightChk.IsChecked == true,
        };
    }

    private void UpdateEstimate()
    {
        if (!int.TryParse(CyclesBox.Text, out int cyc) || cyc <= 0) { EstimateText.Text = ""; return; }
        if (!int.TryParse(SolenoidOnBox.Text, out int son)) son = 100;
        if (!int.TryParse(WindowBox.Text, out int win))     win = 50;
        if (!int.TryParse(CooldownBox.Text, out int cd))    cd  = 200;
        int buttons = ((TestLeftChk.IsChecked == true) ? 1 : 0) + ((TestRightChk.IsChecked == true) ? 1 : 0);
        if (buttons == 0) buttons = 1;
        double totalSec = (double)(son + win + cd) * cyc * buttons / 1000.0;
        EstimateText.Text = $"Est. time: {FormatSeconds(totalSec)}";
    }

    // ── API check ─────────────────────────────────────────────────────────

    private async void CheckApiBtn_Click(object sender, RoutedEventArgs e)
    {
        CheckApiBtn.IsEnabled = false;
        ApiStatusText.Text = "Checking…";
        bool ok = await _relay.IsAvailableAsync();
        ApiStatusText.Text       = ok ? "API available" : "API unavailable";
        ApiStatusText.Foreground = new SolidColorBrush(ok ? Colors.LimeGreen : Colors.OrangeRed);
        CheckApiBtn.IsEnabled    = true;
        AppendLog($"[API] {ApiStatusText.Text}");
    }

    // ── Test execution ───────────────────────────────────────────────────

    private async void StartBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_hook == null || !_hook.IsInstalled)
        { AppendLog("Mouse hook not active."); return; }

        if (!int.TryParse(CyclesBox.Text, out int cycles)   || cycles <= 0 ||
            !int.TryParse(SolenoidOnBox.Text, out int sonMs) ||
            !int.TryParse(WindowBox.Text, out int winMs)     ||
            !int.TryParse(CooldownBox.Text, out int cdMs))
        { AppendLog("Invalid parameters."); return; }

        bool doLeft  = TestLeftChk.IsChecked  == true;
        bool doRight = TestRightChk.IsChecked == true;
        if (!doLeft && !doRight) { AppendLog("No buttons selected."); return; }

        int leftSlot  = LeftRelayCombo.SelectedIndex;
        int rightSlot = RightRelayCombo.SelectedIndex;

        SaveSettings();

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        SetRunning(true);
        ResetStats();
        _lastLeftResult  = null;
        _lastRightResult = null;
        OverallResultText.Text       = "Testing…";
        OverallResultText.Foreground = SystemColors.ControlTextBrush;

        int totalCycles = cycles * (doLeft ? 1 : 0) + cycles * (doRight ? 1 : 0);
        Progress.Maximum = totalCycles;
        Progress.Value   = 0;

        string leftLabel  = RelayApiClient.SlotLabels[leftSlot];
        string rightLabel = RelayApiClient.SlotLabels[rightSlot];

        AppendLog($"=== Click Stress Test Started ===");
        AppendLog($"  Buttons: {(doLeft && doRight ? "Left + Right" : doLeft ? "Left" : "Right")}  Cycles: {cycles}");
        AppendLog($"  Left relay: {leftLabel}  Right relay: {rightLabel}");
        AppendLog($"  Solenoid ON: {sonMs}ms  Window: {winMs}ms  Cooldown: {cdMs}ms");

        bool allPassed = true;

        try
        {
            if (doLeft)
            {
                AppendLog($"[START] Left Button (relay {leftLabel})");
                ProgressText.Text = "Testing Left Button…";

                var leftTask = new MouseClickTask(MouseButton.Left, leftSlot, _relay, _hook)
                {
                    TotalClicks   = cycles,
                    SolenoidOnMs  = sonMs,
                    ClickWindowMs = winMs,
                    CoolDownMs    = cdMs,
                    Log           = new Progress<string>(AppendLog),
                    Progress      = new Progress<ClickCycleProgress>(p =>
                    {
                        UpdateLeftStats(p.Pass, p.Miss, p.Extra);
                        Progress.Value = p.Attempted;
                    })
                };

                _lastLeftResult = await leftTask.RunAsync(ct);
                if (!_lastLeftResult.IsPassed) allPassed = false;
                AppendLog($"[{(_lastLeftResult.IsPassed ? "PASS" : "FAIL")}] Left  — Pass:{_lastLeftResult.Pass}  Miss:{_lastLeftResult.Miss}  Extra:{_lastLeftResult.Extra}");
            }

            if (doRight)
            {
                ct.ThrowIfCancellationRequested();
                AppendLog($"[START] Right Button (relay {rightLabel})");
                ProgressText.Text = "Testing Right Button…";

                int rightBase = doLeft ? cycles : 0;
                var rightTask = new MouseClickTask(MouseButton.Right, rightSlot, _relay, _hook)
                {
                    TotalClicks   = cycles,
                    SolenoidOnMs  = sonMs,
                    ClickWindowMs = winMs,
                    CoolDownMs    = cdMs,
                    Log           = new Progress<string>(AppendLog),
                    Progress      = new Progress<ClickCycleProgress>(p =>
                    {
                        UpdateRightStats(p.Pass, p.Miss, p.Extra);
                        Progress.Value = rightBase + p.Attempted;
                    })
                };

                _lastRightResult = await rightTask.RunAsync(ct);
                if (!_lastRightResult.IsPassed) allPassed = false;
                AppendLog($"[{(_lastRightResult.IsPassed ? "PASS" : "FAIL")}] Right — Pass:{_lastRightResult.Pass}  Miss:{_lastRightResult.Miss}  Extra:{_lastRightResult.Extra}");
            }

            Progress.Value         = totalCycles;
            ProgressText.Text      = "Complete";
            OverallResultText.Text = allPassed ? "PASS" : "FAIL";
            OverallResultText.Foreground = new SolidColorBrush(allPassed ? Colors.LimeGreen : Colors.OrangeRed);
            ExportCsvBtn.IsEnabled = true;
            AppendLog($"=== Test {(allPassed ? "PASS" : "FAIL")} ===");
        }
        catch (OperationCanceledException)
        {
            ProgressText.Text      = "Cancelled";
            OverallResultText.Text = "Cancelled";
            AppendLog("Test cancelled.");
        }
        catch (Exception ex)
        {
            ProgressText.Text      = "Error";
            OverallResultText.Text = "Error";
            AppendLog($"Error: {ex.Message}");
        }
        finally
        {
            // Best-effort: turn off whichever relays were in use
            int[] usedSlots = (doLeft && doRight) ? [leftSlot, rightSlot]
                            : doLeft  ? [leftSlot]
                            : [rightSlot];
            _ = _relay.AllOffAsync(usedSlots);

            SetRunning(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void StopBtn_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    // ── Export ────────────────────────────────────────────────────────────

    private void BrowseBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select Output Folder" };
        if (dlg.ShowDialog() == true)
            OutputFolderBox.Text = dlg.FolderName;
    }

    private void ExportCsvBtn_Click(object sender, RoutedEventArgs e)
    {
        string folder = string.IsNullOrWhiteSpace(OutputFolderBox.Text)
            ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            : OutputFolderBox.Text;

        string path = Path.Combine(folder, $"ClickStressTest_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        var sb = new StringBuilder();
        sb.AppendLine("Button,Cycles,Pass,Miss,Extra,Pass%,Overall");

        void Row(string btn, ClickTestResult r)
        {
            if (r != null)
                sb.AppendLine($"{btn},{r.Attempted},{r.Pass},{r.Miss},{r.Extra},{r.PassRatePct:F2},{(r.IsPassed ? "PASS" : "FAIL")}");
        }

        Row("Left",  _lastLeftResult);
        Row("Right", _lastRightResult);

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        AppendLog($"Exported: {path}");
    }

    // ── UI helpers ────────────────────────────────────────────────────────

    private void SetRunning(bool running)
    {
        StartBtn.IsEnabled     = !running;
        StopBtn.IsEnabled      = running;
        CheckApiBtn.IsEnabled  = !running;
        ExportCsvBtn.IsEnabled = false;
    }

    private void ResetStats()
    {
        LeftTestedText.Text   = RightTestedText.Text   = "0";
        LeftPassText.Text     = RightPassText.Text     = "0";
        LeftMissText.Text     = RightMissText.Text     = "0";
        LeftExtraText.Text    = RightExtraText.Text    = "0";
        LeftPassPctText.Text  = RightPassPctText.Text  = "—";
        Progress.Value        = 0;
        ProgressText.Text     = "Running…";
    }

    private void UpdateLeftStats(int pass, int miss, int extra)
    {
        int attempted = pass + miss + extra;
        LeftTestedText.Text  = attempted.ToString();
        LeftPassText.Text    = pass.ToString();
        LeftMissText.Text    = miss.ToString();
        LeftExtraText.Text   = extra.ToString();
        LeftPassPctText.Text = attempted > 0 ? $"{(double)pass / attempted * 100:F1}%" : "—";
        LeftMissText.Foreground  = miss  > 0 ? new SolidColorBrush(Colors.OrangeRed) : SystemColors.ControlTextBrush;
        LeftExtraText.Foreground = extra > 0 ? new SolidColorBrush(Colors.OrangeRed) : SystemColors.ControlTextBrush;
    }

    private void UpdateRightStats(int pass, int miss, int extra)
    {
        int attempted = pass + miss + extra;
        RightTestedText.Text  = attempted.ToString();
        RightPassText.Text    = pass.ToString();
        RightMissText.Text    = miss.ToString();
        RightExtraText.Text   = extra.ToString();
        RightPassPctText.Text = attempted > 0 ? $"{(double)pass / attempted * 100:F1}%" : "—";
        RightMissText.Foreground  = miss  > 0 ? new SolidColorBrush(Colors.OrangeRed) : SystemColors.ControlTextBrush;
        RightExtraText.Foreground = extra > 0 ? new SolidColorBrush(Colors.OrangeRed) : SystemColors.ControlTextBrush;
    }

    private static string FormatSeconds(double sec)
        => sec < 60 ? $"{sec:F0}s" : $"{(int)(sec / 60)}m {sec % 60:F0}s";
}
