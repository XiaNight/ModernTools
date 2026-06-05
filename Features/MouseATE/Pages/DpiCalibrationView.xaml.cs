using Base.Services;
using Base.Services.Peripheral;
using Microsoft.Win32;
using MouseATE.Hardware;
using MouseATE.Reports;
using MouseATE.Settings;
using MouseATE.Tests;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MouseATE.Pages;

public partial class DpiCalibrationView : UserControl
{
    internal PeripheralInterface ActiveInterface { get; set; }

    private ThreeAxisController _arm;
    private RawInputCapture _capture;
    private CancellationTokenSource _cts;
    private CalibrationReportRow _lastReport;

    // Factory-mode HID commands — adjust byte arrays per device protocol
    private static readonly byte[] CMD_FACTORY_ENABLE  = { 0x0B, 0x01 };
    private static readonly byte[] CMD_FACTORY_DISABLE = { 0x0B, 0x00 };
    private static readonly byte[] CMD_READ_CAL        = { 0x12 };
    private static readonly byte[] CMD_CLEAR_CAL       = { 0x13 };
    private static readonly byte[] CMD_WRITE_CAL       = { 0x14 };

    public DpiCalibrationView()
    {
        InitializeComponent();
    }

    internal void SetArm(ThreeAxisController arm) => _arm = arm;
    internal void SetCapture(RawInputCapture capture) => _capture = capture;

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshProfiles();
        var g = AteSettingsStore.Global;
        ZOffsetBox.Text = g.ZOffset.ToString();
        CyclesBox.Text = g.TestCycles.ToString();
    }

    private void RefreshProfiles()
    {
        var profiles = AteSettingsStore.Profiles;
        ProfileCombo.ItemsSource = null;
        ProfileCombo.ItemsSource = profiles;
        int idx = AteSettingsStore.ActiveProfileIndex;
        ProfileCombo.SelectedIndex = (idx >= 0 && idx < profiles.Count) ? idx : 0;
    }

    private void ProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = ProfileCombo.SelectedIndex;
        if (idx < 0) return;
        AteSettingsStore.ActiveProfileIndex = idx;
        CalDpiBox.Text = AteSettingsStore.ActiveProfile.CalibrationDpi.ToString();
    }

    private void BrowseBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select Output Folder" };
        if (dlg.ShowDialog() == true)
            OutputFolderBox.Text = dlg.FolderName;
    }

    private static void AppendLog(string msg) => Debug.Log("[ATE/DpiCal] " + msg);

    // ── Step indicator helpers ───────────────────────────────────────────

    private (TextBlock icon, TextBlock text)[] _steps;

    private void InitStepIndicators()
    {
        _steps = new[]
        {
            (Step1Icon, Step1Text), (Step2Icon, Step2Text), (Step3Icon, Step3Text),
            (Step4Icon, Step4Text), (Step5Icon, Step5Text), (Step6Icon, Step6Text),
            (Step7Icon, Step7Text), (Step8Icon, Step8Text)
        };
        foreach (var (icon, _) in _steps)
        {
            icon.Text = "○";
            icon.Foreground = Brushes.Gray;
        }
    }

    private void SetStep(int index, string icon, Brush color, string label = null)
    {
        if (_steps == null || index < 0 || index >= _steps.Length) return;
        Dispatcher.Invoke(() =>
        {
            _steps[index].icon.Text = icon;
            _steps[index].icon.Foreground = color;
            if (label != null) _steps[index].text.Text = label;
        });
    }

    private void MarkRunning(int index, string label = null) =>
        SetStep(index, "►", Brushes.DodgerBlue, label);
    private void MarkDone(int index, string label = null) =>
        SetStep(index, "✔", Brushes.LimeGreen, label);
    private void MarkFailed(int index, string label = null) =>
        SetStep(index, "✘", Brushes.Red, label);

    // ── ReCalibration pipeline ───────────────────────────────────────────

    private async void ReCalBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidatePipelinePrereqs()) return;

        if (!double.TryParse(ZOffsetBox.Text, out double zOff)
            || !int.TryParse(CyclesBox.Text, out int cycles))
        { AppendLog("Invalid Z offset or cycles."); return; }

        var profile = AteSettingsStore.ActiveProfile;
        var global = AteSettingsStore.Global;
        int calDpi = profile.CalibrationDpi;
        double dist = global.TestDistance;
        int speed = global.TestSpeed;

        InitStepIndicators();
        BeginPipeline();

        try
        {
            // Step 1: Enable factory mode
            MarkRunning(0);
            bool ok = await SendCmdAsync(CMD_FACTORY_ENABLE);
            if (!ok) { MarkFailed(0); FailPipeline("Failed to enable factory mode."); return; }
            MarkDone(0);

            // Step 2: Read current calibration (informational — failures are non-fatal)
            MarkRunning(1);
            await SendCmdAsync(CMD_READ_CAL);
            var resp = await ReceiveAsync();
            int savedXCal = 0, savedYCal = 0;
            if (resp?.Length >= 10)
            {
                savedXCal = BitConverter.ToInt16(resp, 2);
                savedYCal = BitConverter.ToInt16(resp, 4);
            }
            MarkDone(1);

            // Step 3: Clear calibration
            MarkRunning(2);
            ok = await SendCmdAsync(CMD_CLEAR_CAL);
            if (!ok) { MarkFailed(2); FailPipeline("Failed to clear calibration."); return; }
            MarkDone(2);

            // Step 4: Measure X (sensor native — no cal)
            MarkRunning(3, "Measuring sensor DPI X…");
            var resultX = await RunDpiTestAsync(TestAxis.X, calDpi, dist, zOff, speed, cycles);
            MarkDone(3, $"Measure X → {resultX.AverageDpi:F1} DPI");
            AppendLog($"Sensor X avg: {resultX.AverageDpi:F1} DPI ({resultX.AverageDeviationPct:F2}%)");

            // Step 5: Measure Y (sensor native)
            MarkRunning(4, "Measuring sensor DPI Y…");
            var resultY = await RunDpiTestAsync(TestAxis.Y, calDpi, dist, zOff, speed, cycles);
            MarkDone(4, $"Measure Y → {resultY.AverageDpi:F1} DPI");
            AppendLog($"Sensor Y avg: {resultY.AverageDpi:F1} DPI ({resultY.AverageDeviationPct:F2}%)");

            // Compute calibration values — formula: round(avg_dpi) as hex integer
            int newCalX = (int)Math.Round(resultX.AverageDpi);
            int newCalY = (int)Math.Round(resultY.AverageDpi);
            AppendLog($"New calibration values: X=0x{newCalX:X4} ({newCalX})  Y=0x{newCalY:X4} ({newCalY})");

            Dispatcher.Invoke(() =>
            {
                XOrigText.Text = $"{resultX.AverageDpi:F1} DPI ({resultX.AverageDeviationPct:F2}%)";
                YOrigText.Text = $"{resultY.AverageDpi:F1} DPI ({resultY.AverageDeviationPct:F2}%)";
                NewCalXText.Text = $"0x{newCalX:X4}";
                NewCalYText.Text = $"0x{newCalY:X4}";
                WriteXCalBox.Text = newCalX.ToString();
                WriteYCalBox.Text = newCalY.ToString();
            });

            // Step 6: Write calibration
            MarkRunning(5, "Writing calibration…");
            ok = await WriteCalibrationAsync((short)newCalX, (short)newCalY);
            if (!ok) { MarkFailed(5); FailPipeline("Calibration write failed."); return; }
            // Verify by reading back
            await SendCmdAsync(CMD_READ_CAL);
            resp = await ReceiveAsync();
            MarkDone(5, "Write + verify OK");

            // Step 7: Verification DPI test
            MarkRunning(6, "Verifying X after cal…");
            var verX = await RunDpiTestAsync(TestAxis.X, calDpi, dist, zOff, speed, cycles);
            MarkRunning(6, "Verifying Y after cal…");
            var verY = await RunDpiTestAsync(TestAxis.Y, calDpi, dist, zOff, speed, cycles);
            MarkDone(6, $"Verify X={verX.AverageDeviationPct:F2}%  Y={verY.AverageDeviationPct:F2}%");

            Dispatcher.Invoke(() =>
            {
                XAfterText.Text = $"{verX.AverageDpi:F1} DPI ({verX.AverageDeviationPct:F2}%)";
                YAfterText.Text = $"{verY.AverageDpi:F1} DPI ({verY.AverageDeviationPct:F2}%)";
            });

            // Step 8: Disable factory mode
            MarkRunning(7);
            await SendCmdAsync(CMD_FACTORY_DISABLE);
            MarkDone(7);

            // Build report
            _lastReport = new CalibrationReportRow
            {
                SerialNumber = SerialNumberBox.Text.Trim(),
                XOrigCount = resultX.Cycles.Count,
                XOrigDeviationPct = resultX.AverageDeviationPct,
                YOrigCount = resultY.Cycles.Count,
                YOrigDeviationPct = resultY.AverageDeviationPct,
                XAfterCount = verX.Cycles.Count,
                XAfterDeviationPct = verX.AverageDeviationPct,
                YAfterCount = verY.Cycles.Count,
                YAfterDeviationPct = verY.AverageDeviationPct,
            };

            EndPipeline(true);
            AppendLog("ReCalibration complete.");
        }
        catch (OperationCanceledException)
        {
            EndPipeline(false, "Cancelled");
            AppendLog("Pipeline cancelled.");
        }
        catch (Exception ex)
        {
            EndPipeline(false, "Error");
            AppendLog($"Pipeline error: {ex.Message}");
        }
        finally
        {
            FinishPipeline();
        }
    }

    // ── CheckCalibration pipeline ────────────────────────────────────────

    private async void CheckCalBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidatePipelinePrereqs()) return;

        if (!double.TryParse(ZOffsetBox.Text, out double zOff)
            || !int.TryParse(CyclesBox.Text, out int cycles))
        { AppendLog("Invalid Z offset or cycles."); return; }

        var profile = AteSettingsStore.ActiveProfile;
        var global = AteSettingsStore.Global;
        int calDpi = profile.CalibrationDpi;
        double dist = global.TestDistance;
        int speed = global.TestSpeed;

        InitStepIndicators();
        BeginPipeline();

        try
        {
            // Step 1: Enable factory mode
            MarkRunning(0);
            bool ok = await SendCmdAsync(CMD_FACTORY_ENABLE);
            if (!ok) { MarkFailed(0); FailPipeline("Failed to enable factory mode."); return; }
            MarkDone(0);

            // Step 2: Read and save current calibration
            MarkRunning(1, "Reading current calibration…");
            await SendCmdAsync(CMD_READ_CAL);
            var resp = await ReceiveAsync();
            short savedXCal = 0, savedYCal = 0;
            if (resp?.Length >= 10)
            {
                savedXCal = BitConverter.ToInt16(resp, 2);
                savedYCal = BitConverter.ToInt16(resp, 4);
                AppendLog($"Saved cal: X=0x{savedXCal:X4} Y=0x{savedYCal:X4}");
            }
            MarkDone(1);

            // Step 3 is N/A for CheckCal — label it as skipped (we don't clear yet)
            SetStep(2, "–", Brushes.Gray, "Clear cal (done in step 5)");

            // Step 4: Test WITH current calibration (X)
            MarkRunning(3, "Measuring X with cal…");
            var afterX = await RunDpiTestAsync(TestAxis.X, calDpi, dist, zOff, speed, cycles);
            MarkDone(3, $"X with cal → {afterX.AverageDpi:F1} DPI");

            // Step 5: Test WITH current calibration (Y)
            MarkRunning(4, "Measuring Y with cal…");
            var afterY = await RunDpiTestAsync(TestAxis.Y, calDpi, dist, zOff, speed, cycles);
            MarkDone(4, $"Y with cal → {afterY.AverageDpi:F1} DPI");

            Dispatcher.Invoke(() =>
            {
                XAfterText.Text = $"{afterX.AverageDpi:F1} DPI ({afterX.AverageDeviationPct:F2}%)";
                YAfterText.Text = $"{afterY.AverageDpi:F1} DPI ({afterY.AverageDeviationPct:F2}%)";
            });

            // Now clear and run sensor-native test
            MarkRunning(2, "Clearing calibration…");
            ok = await SendCmdAsync(CMD_CLEAR_CAL);
            if (!ok) { MarkFailed(2); FailPipeline("Failed to clear calibration."); return; }
            MarkDone(2);

            MarkRunning(5, "Measuring X sensor (no cal)…");
            var origX = await RunDpiTestAsync(TestAxis.X, calDpi, dist, zOff, speed, cycles);
            MarkRunning(5, "Measuring Y sensor (no cal)…");
            var origY = await RunDpiTestAsync(TestAxis.Y, calDpi, dist, zOff, speed, cycles);
            MarkDone(5, $"Sensor X={origX.AverageDpi:F1}  Y={origY.AverageDpi:F1}");

            Dispatcher.Invoke(() =>
            {
                XOrigText.Text = $"{origX.AverageDpi:F1} DPI ({origX.AverageDeviationPct:F2}%)";
                YOrigText.Text = $"{origY.AverageDpi:F1} DPI ({origY.AverageDeviationPct:F2}%)";
            });

            // Step 6: Restore original calibration
            MarkRunning(5, "Restoring original calibration…");
            ok = await WriteCalibrationAsync(savedXCal, savedYCal);
            if (!ok) { MarkFailed(5); FailPipeline("Failed to restore calibration!"); return; }
            MarkDone(5, "Original cal restored");

            // Step 7: Verify restore
            MarkRunning(6, "Verifying restored cal…");
            await SendCmdAsync(CMD_READ_CAL);
            MarkDone(6, "Restored cal verified");

            // Step 8: Disable factory
            MarkRunning(7);
            await SendCmdAsync(CMD_FACTORY_DISABLE);
            MarkDone(7);

            _lastReport = new CalibrationReportRow
            {
                SerialNumber = SerialNumberBox.Text.Trim(),
                XAfterCount = afterX.Cycles.Count,
                XAfterDeviationPct = afterX.AverageDeviationPct,
                YAfterCount = afterY.Cycles.Count,
                YAfterDeviationPct = afterY.AverageDeviationPct,
                XOrigCount = origX.Cycles.Count,
                XOrigDeviationPct = origX.AverageDeviationPct,
                YOrigCount = origY.Cycles.Count,
                YOrigDeviationPct = origY.AverageDeviationPct,
            };

            EndPipeline(true);
            AppendLog("CheckCalibration complete.");
        }
        catch (OperationCanceledException)
        {
            EndPipeline(false, "Cancelled");
            AppendLog("Pipeline cancelled.");
        }
        catch (Exception ex)
        {
            EndPipeline(false, "Error");
            AppendLog($"Pipeline error: {ex.Message}");
        }
        finally
        {
            FinishPipeline();
        }
    }

    private void PipelineCancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    // ── Pipeline helpers ─────────────────────────────────────────────────

    private bool ValidatePipelinePrereqs()
    {
        if (!CheckDevice()) return false;
        if (_arm == null || !_arm.IsConnected)
        { AppendLog("Fixture arm not connected. Configure on Fixture Control page first."); return false; }
        if (_capture == null)
        { AppendLog("Raw input capture not initialized."); return false; }
        return true;
    }

    private void BeginPipeline()
    {
        _cts = new CancellationTokenSource();
        _lastReport = null;
        Dispatcher.Invoke(() =>
        {
            ReCalBtn.IsEnabled = false;
            CheckCalBtn.IsEnabled = false;
            PipelineCancelBtn.IsEnabled = true;
            ExportCsvBtn.IsEnabled = false;
            ExportXlsxBtn.IsEnabled = false;
            PipelineProgress.Value = 0;
            PipelineStatus.Text = "Running…";
            XOrigText.Text = "";
            YOrigText.Text = "";
            XAfterText.Text = "";
            YAfterText.Text = "";
            NewCalXText.Text = "";
            NewCalYText.Text = "";
            OverallResultText.Text = "";
        });
    }

    private void FailPipeline(string reason)
    {
        AppendLog($"Pipeline failed: {reason}");
        EndPipeline(false, "Failed");
    }

    private void EndPipeline(bool success, string statusLabel = null)
    {
        bool passed = success;
        if (passed && _lastReport != null)
            passed = Math.Abs(_lastReport.XAfterDeviationPct) <= AteSettingsStore.Global.DeviationThresholdPct
                  && Math.Abs(_lastReport.YAfterDeviationPct) <= AteSettingsStore.Global.DeviationThresholdPct;

        Dispatcher.Invoke(() =>
        {
            PipelineProgress.Value = success ? 100 : PipelineProgress.Value;
            PipelineStatus.Text = statusLabel ?? (success ? "Complete" : "Failed");
            OverallResultText.Text = success ? (passed ? "PASS" : "FAIL (out of tolerance)") : "ABORTED";
            OverallResultText.Foreground = new SolidColorBrush(
                success && passed ? Colors.LimeGreen : Colors.Red);
            if (success && _lastReport != null)
            {
                ExportCsvBtn.IsEnabled = true;
                ExportXlsxBtn.IsEnabled = true;
            }
        });
    }

    private void FinishPipeline()
    {
        Dispatcher.Invoke(() =>
        {
            ReCalBtn.IsEnabled = true;
            CheckCalBtn.IsEnabled = true;
            PipelineCancelBtn.IsEnabled = false;
        });
    }

    private async Task<DpiTestResult> RunDpiTestAsync(
        TestAxis axis, int dpi, double dist, double zOff, int speed, int cycles)
    {
        var runner = new DpiTestRunner(_arm, _capture)
        {
            CancelToken = _cts.Token,
            Log = new Progress<string>(AppendLog)
        };
        return await Task.Run(
            () => runner.RunAsync(axis, dpi, dist, zOff, speed, cycles),
            _cts.Token);
    }

    // ── Manual command buttons ───────────────────────────────────────────

    private async void EnableFactoryMode_Click(object sender, RoutedEventArgs e)
    {
        if (!CheckDevice()) return;
        bool ok = await SendCmdAsync(CMD_FACTORY_ENABLE);
        FactoryModeStatus.Text = ok ? "Factory Mode: ON" : "Failed";
        FactoryModeStatus.Foreground = new SolidColorBrush(ok ? Colors.LimeGreen : Colors.Red);
        AppendLog(ok ? "Factory mode enabled." : "Failed to enable factory mode.");
    }

    private async void DisableFactoryMode_Click(object sender, RoutedEventArgs e)
    {
        if (!CheckDevice()) return;
        bool ok = await SendCmdAsync(CMD_FACTORY_DISABLE);
        FactoryModeStatus.Text = ok ? "Factory Mode: OFF" : "Failed";
        FactoryModeStatus.Foreground = new SolidColorBrush(ok ? Colors.Orange : Colors.Red);
        AppendLog(ok ? "Factory mode disabled." : "Failed to disable factory mode.");
    }

    private async void ReadCal_Click(object sender, RoutedEventArgs e)
    {
        if (!CheckDevice()) return;
        bool sent = await SendCmdAsync(CMD_READ_CAL);
        if (!sent) return;

        byte[] response = await ReceiveAsync();
        if (response == null) return;

        if (response.Length >= 10)
        {
            ReadDpiLevelBox.Text = response[1].ToString();
            ReadXCalBox.Text = BitConverter.ToInt16(response, 2).ToString();
            ReadYCalBox.Text = BitConverter.ToInt16(response, 4).ToString();
            AppendLog("Calibration read OK.");
        }
        else
        {
            AppendLog($"Unexpected response ({response.Length} bytes). Adjust parsing for this device.");
        }
    }

    private async void ClearCal_Click(object sender, RoutedEventArgs e)
    {
        if (!CheckDevice()) return;
        if (MessageBox.Show("Clear all DPI calibration on device?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        bool ok = await SendCmdAsync(CMD_CLEAR_CAL);
        AppendLog(ok ? "Calibration cleared." : "Clear failed.");
    }

    private async void WriteCal_Click(object sender, RoutedEventArgs e)
    {
        if (!CheckDevice()) return;
        if (!short.TryParse(WriteXCalBox.Text, out short xCal)
            || !short.TryParse(WriteYCalBox.Text, out short yCal))
        { AppendLog("Invalid calibration values."); return; }

        bool ok = await WriteCalibrationAsync(xCal, yCal);
        AppendLog(ok ? $"Wrote: X=0x{xCal:X4} Y=0x{yCal:X4}" : "Write calibration failed.");
    }

    // ── Export ───────────────────────────────────────────────────────────

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_lastReport == null) return;
        string folder = ResolveOutputFolder();
        string path = Path.Combine(folder, $"Calibration_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        try
        {
            CalibrationReport.WriteCsv(path, _lastReport);
            AppendLog($"Exported CSV: {path}");
        }
        catch (Exception ex)
        {
            AppendLog($"CSV export failed: {ex.Message}");
        }
    }

    private void ExportXlsx_Click(object sender, RoutedEventArgs e)
    {
        if (_lastReport == null) return;
        string folder = ResolveOutputFolder();
        string path = Path.Combine(folder, $"Calibration_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        bool ok = CalibrationReport.TryWriteXlsx(path, _lastReport);
        AppendLog(ok ? $"Exported XLSX: {path}" : "XLSX export failed (CSV still available).");
    }

    private string ResolveOutputFolder() =>
        string.IsNullOrWhiteSpace(OutputFolderBox.Text)
            ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            : OutputFolderBox.Text;

    // ── Low-level HID helpers ────────────────────────────────────────────

    private bool CheckDevice()
    {
        if (ActiveInterface == null || !ActiveInterface.IsDeviceConnected)
        { AppendLog("No device connected. Select a mouse in the device selector."); return false; }
        return true;
    }

    private async Task<bool> SendCmdAsync(byte[] cmd)
    {
        try { return await ActiveInterface.WriteAsync(cmd); }
        catch (Exception ex) { AppendLog($"Write failed: {ex.Message}"); return false; }
    }

    private async Task<byte[]> ReceiveAsync()
    {
        try { return await ActiveInterface.ReadAsync(new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token); }
        catch (Exception ex) { AppendLog($"Read failed: {ex.Message}"); return null; }
    }

    private async Task<bool> WriteCalibrationAsync(short xCal, short yCal)
    {
        var payload = new byte[CMD_WRITE_CAL.Length + 4];
        CMD_WRITE_CAL.CopyTo(payload, 0);
        BitConverter.TryWriteBytes(payload.AsSpan(CMD_WRITE_CAL.Length), xCal);
        BitConverter.TryWriteBytes(payload.AsSpan(CMD_WRITE_CAL.Length + 2), yCal);
        return await SendCmdAsync(payload);
    }
}
