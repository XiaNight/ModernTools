using Base.Services;
using Microsoft.Win32;
using MouseATE.Hardware;
using MouseATE.Settings;
using MouseATE.Tests;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace MouseATE.Pages;

// Row shown in the results grid — one per (TargetDpi, Axis) combination
public record BatchResultRow(
    int TargetDpi,
    string Axis,
    double AverageDpi,
    double AverageDeviationPct,
    double ResolutionError,
    bool Pass,
    DpiTestResult Detail)
{
    public string PassText => Pass ? "PASS" : "FAIL";
}

public partial class DpiTestView : UserControl
{
    private ThreeAxisController _arm;
    private RawInputCapture _capture;
    private CancellationTokenSource _cts;
    private List<BatchResultRow> _lastResults;

    public DpiTestView()
    {
        InitializeComponent();
    }

    internal void SetArm(ThreeAxisController arm) => _arm = arm;
    internal void SetCapture(RawInputCapture capture) => _capture = capture;

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshProfiles();
        LoadDefaultParams();
    }

    private void RefreshProfiles()
    {
        var profiles = AteSettingsStore.Profiles;
        ProfileCombo.ItemsSource = null;
        ProfileCombo.ItemsSource = profiles;
        int idx = AteSettingsStore.ActiveProfileIndex;
        ProfileCombo.SelectedIndex = (idx >= 0 && idx < profiles.Count) ? idx : 0;
    }

    private void LoadDefaultParams()
    {
        var g = AteSettingsStore.Global;
        DistanceBox.Text = g.TestDistance.ToString();
        ZOffsetBox.Text = g.ZOffset.ToString();
        SpeedBox.Text = g.TestSpeed.ToString();
        CyclesBox.Text = g.TestCycles.ToString();
        ThresholdBox.Text = g.DeviationThresholdPct.ToString();
    }

    private void ProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int idx = ProfileCombo.SelectedIndex;
        if (idx < 0) return;
        AteSettingsStore.ActiveProfileIndex = idx;
        UpdateModeDisplay();
    }

    private void Mode_Checked(object sender, RoutedEventArgs e) => UpdateModeDisplay();

    private void UpdateModeDisplay()
    {
        // Guard: Checked event on ModeSingle fires during XAML parsing before sibling controls exist
        if (ModeMediaReview == null || SingleDpiLabel == null || FtPanel == null) return;

        bool isSingle = ModeSingle.IsChecked == true;
        bool isMR = ModeMediaReview.IsChecked == true;
        bool isFT = ModeFullTest.IsChecked == true;

        SingleDpiLabel.Visibility = isSingle ? Visibility.Visible : Visibility.Collapsed;
        SingleDpiBox.Visibility = isSingle ? Visibility.Visible : Visibility.Collapsed;

        MrDpiLabel.Visibility = isMR ? Visibility.Visible : Visibility.Collapsed;
        MrDpiListText.Visibility = isMR ? Visibility.Visible : Visibility.Collapsed;

        FtLabel.Visibility = isFT ? Visibility.Visible : Visibility.Collapsed;
        FtPanel.Visibility = isFT ? Visibility.Visible : Visibility.Collapsed;

        if (isMR || isFT)
        {
            var profile = AteSettingsStore.ActiveProfile;
            if (isMR)
                MrDpiListText.Text = string.Join(", ", profile.MediaReviewDpis) + " DPI";
            if (isFT)
            {
                FtMinBox.Text = profile.FullTestMinDpi.ToString();
                FtMaxBox.Text = profile.FullTestMaxDpi.ToString();
                FtStepBox.Text = profile.FullTestDpiStep.ToString();
            }
        }
    }

    private void LoadDefaults_Click(object sender, RoutedEventArgs e) => LoadDefaultParams();

    private void BrowseBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select Output Folder" };
        if (dlg.ShowDialog() == true)
            OutputFolderBox.Text = dlg.FolderName;
    }

    private static void AppendLog(string msg) => Debug.Log("[ATE/DpiTest] " + msg);

    private async void RunBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_arm == null || !_arm.IsConnected)
        { AppendLog("Fixture not connected. Configure on the Fixture Control page first."); return; }
        if (_capture == null)
        { AppendLog("Raw input capture not initialized."); return; }

        if (!double.TryParse(DistanceBox.Text, out double dist)
            || !double.TryParse(ZOffsetBox.Text, out double zOff)
            || !int.TryParse(SpeedBox.Text, out int speed)
            || !int.TryParse(CyclesBox.Text, out int cycles)
            || !double.TryParse(ThresholdBox.Text, out double threshold))
        { AppendLog("Invalid parameters."); return; }

        var dpis = BuildDpiList();
        if (dpis.Count == 0) { AppendLog("No DPI values to test."); return; }

        var axes = GetAxes();

        _cts = new CancellationTokenSource();
        RunBtn.IsEnabled = false;
        CancelBtn.IsEnabled = true;
        ExportCsvBtn.IsEnabled = false;
        ExportXlsxBtn.IsEnabled = false;
        _lastResults = new List<BatchResultRow>();
        ResultsGrid.ItemsSource = null;
        Progress.Value = 0;
        RunStatus.Text = "Running...";

        int totalRuns = dpis.Count * axes.Count;
        int completed = 0;

        try
        {
            foreach (int dpi in dpis)
            {
                foreach (var axis in axes)
                {
                    _cts.Token.ThrowIfCancellationRequested();

                    AppendLog($"Testing DPI={dpi} Axis={axis}...");
                    var runner = new DpiTestRunner(_arm, _capture)
                    {
                        CancelToken = _cts.Token,
                        Log = new Progress<string>(AppendLog)
                    };

                    var result = await Task.Run(
                        () => runner.RunAsync(axis, dpi, dist, zOff, speed, cycles),
                        _cts.Token);

                    bool pass = Math.Abs(result.AverageDeviationPct) <= threshold;
                    _lastResults.Add(new BatchResultRow(
                        dpi, axis.ToString(), result.AverageDpi,
                        result.AverageDeviationPct, result.ResolutionError, pass, result));

                    completed++;
                    Dispatcher.Invoke(() =>
                    {
                        ResultsGrid.ItemsSource = null;
                        ResultsGrid.ItemsSource = _lastResults;
                        Progress.Value = 100.0 * completed / totalRuns;
                    });
                    AppendLog($"  → Avg={result.AverageDpi:F1} Dev={result.AverageDeviationPct:F2}% {(pass ? "PASS" : "FAIL")}");
                }
            }

            Dispatcher.Invoke(() =>
            {
                Progress.Value = 100;
                RunStatus.Text = "Complete";
                ExportCsvBtn.IsEnabled = true;
                ExportXlsxBtn.IsEnabled = true;
            });
            AppendLog("All tests complete.");
        }
        catch (OperationCanceledException)
        {
            RunStatus.Text = "Cancelled";
            AppendLog("Test cancelled.");
        }
        catch (Exception ex)
        {
            RunStatus.Text = "Error";
            AppendLog($"Error: {ex.Message}");
        }
        finally
        {
            Dispatcher.Invoke(() => { RunBtn.IsEnabled = true; CancelBtn.IsEnabled = false; });
        }
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private List<int> BuildDpiList()
    {
        if (ModeSingle.IsChecked == true)
        {
            if (int.TryParse(SingleDpiBox.Text, out int dpi))
                return new List<int> { dpi };
            AppendLog("Invalid single DPI value.");
            return new();
        }
        if (ModeMediaReview.IsChecked == true)
            return new List<int>(AteSettingsStore.ActiveProfile.MediaReviewDpis);
        if (ModeFullTest.IsChecked == true)
        {
            var p = AteSettingsStore.ActiveProfile;
            var list = new List<int>();
            for (int d = p.FullTestMinDpi; d <= p.FullTestMaxDpi; d += p.FullTestDpiStep)
                list.Add(d);
            return list;
        }
        return new();
    }

    private List<TestAxis> GetAxes()
    {
        if (AxisX.IsChecked == true)  return new() { TestAxis.X };
        if (AxisY.IsChecked == true)  return new() { TestAxis.Y };
        return new() { TestAxis.X, TestAxis.Y };   // XY
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResults == null || _lastResults.Count == 0) return;
        string folder = string.IsNullOrWhiteSpace(OutputFolderBox.Text)
            ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            : OutputFolderBox.Text;

        string path = Path.Combine(folder, $"DPI_Test_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        WriteBatchCsv(path);
        AppendLog($"Exported CSV: {path}");
    }

    private void ExportXlsx_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResults == null || _lastResults.Count == 0) return;
        string folder = string.IsNullOrWhiteSpace(OutputFolderBox.Text)
            ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            : OutputFolderBox.Text;

        string path = Path.Combine(folder, $"DPI_Test_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        bool ok = TryWriteBatchXlsx(path);
        AppendLog(ok ? $"Exported XLSX: {path}" : "XLSX export failed (CSV still available).");
    }

    private void WriteBatchCsv(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Target DPI,Axis,Avg DPI,Avg Deviation%,Resolution Error%,Pass");
        foreach (var r in _lastResults)
            sb.AppendLine($"{r.TargetDpi},{r.Axis},{r.AverageDpi:F1},{r.AverageDeviationPct:F4},{r.ResolutionError:F4},{r.PassText}");

        // Append cycle detail
        sb.AppendLine();
        sb.AppendLine("--- Cycle Detail ---");
        sb.AppendLine("Target DPI,Axis,Cycle,Actual DPI,Deviation%,Angle Deg,Compensated DPI,Path DPI");
        foreach (var r in _lastResults)
            foreach (var c in r.Detail.Cycles)
                sb.AppendLine($"{r.TargetDpi},{r.Axis},{c.Cycle},{c.ActualDpi:F1},{c.DeviationPct:F4},{c.AngleDeg:F4},{c.CompensatedDpi:F1},{c.PathLengthDpi:F1}");

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private bool TryWriteBatchXlsx(string path)
    {
        try
        {
            using var wb = new ClosedXML.Excel.XLWorkbook();
            var summary = wb.Worksheets.Add("Summary");

            // Header row
            summary.Cell(1, 1).Value = "Target DPI";
            summary.Cell(1, 2).Value = "Axis";
            summary.Cell(1, 3).Value = "Avg DPI";
            summary.Cell(1, 4).Value = "Avg Deviation %";
            summary.Cell(1, 5).Value = "Resolution Error %";
            summary.Cell(1, 6).Value = "Pass";

            for (int col = 1; col <= 6; col++)
                summary.Cell(1, col).Style.Font.Bold = true;

            int row = 2;
            foreach (var r in _lastResults)
            {
                summary.Cell(row, 1).Value = r.TargetDpi;
                summary.Cell(row, 2).Value = r.Axis;
                summary.Cell(row, 3).Value = r.AverageDpi;
                summary.Cell(row, 4).Value = r.AverageDeviationPct;
                summary.Cell(row, 5).Value = r.ResolutionError;
                summary.Cell(row, 6).Value = r.PassText;
                if (!r.Pass) summary.Cell(row, 6).Style.Font.FontColor = ClosedXML.Excel.XLColor.Red;
                row++;
            }

            // Cycle detail sheet
            var detail = wb.Worksheets.Add("Cycle Detail");
            detail.Cell(1, 1).Value = "Target DPI";
            detail.Cell(1, 2).Value = "Axis";
            detail.Cell(1, 3).Value = "Cycle";
            detail.Cell(1, 4).Value = "Actual DPI";
            detail.Cell(1, 5).Value = "Deviation %";
            detail.Cell(1, 6).Value = "Angle Deg";
            detail.Cell(1, 7).Value = "Compensated DPI";
            detail.Cell(1, 8).Value = "Path DPI";
            for (int col = 1; col <= 8; col++)
                detail.Cell(1, col).Style.Font.Bold = true;

            row = 2;
            foreach (var r in _lastResults)
                foreach (var c in r.Detail.Cycles)
                {
                    detail.Cell(row, 1).Value = r.TargetDpi;
                    detail.Cell(row, 2).Value = r.Axis;
                    detail.Cell(row, 3).Value = c.Cycle;
                    detail.Cell(row, 4).Value = c.ActualDpi;
                    detail.Cell(row, 5).Value = c.DeviationPct;
                    detail.Cell(row, 6).Value = c.AngleDeg;
                    detail.Cell(row, 7).Value = c.CompensatedDpi;
                    detail.Cell(row, 8).Value = c.PathLengthDpi;
                    row++;
                }

            summary.Columns().AdjustToContents();
            detail.Columns().AdjustToContents();
            wb.SaveAs(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
