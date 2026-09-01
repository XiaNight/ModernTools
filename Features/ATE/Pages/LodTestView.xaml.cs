using Base.Services;
using Microsoft.Win32;
using MouseATE.Hardware;
using MouseATE.Tests;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MouseATE.Pages;

public partial class LodTestView : System.Windows.Controls.UserControl
{
    private ThreeAxisController _arm;
    private RawInputCapture _capture;
    private CancellationTokenSource _cts;
    private LodTestResult _lastResult;

    public LodTestView()
    {
        InitializeComponent();
    }

    internal void SetArm(ThreeAxisController arm) => _arm = arm;
    internal void SetCapture(RawInputCapture capture) => _capture = capture;

    private static void AppendLog(string msg) => Debug.Log("[ATE/LodTest] " + msg);

    private void BrowseBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select Output Folder" };
        if (dlg.ShowDialog() == true)
            OutputFolderBox.Text = dlg.FolderName;
    }

    private async void RunBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_arm == null || !_arm.IsConnected)
        { AppendLog("Fixture not connected. Configure on the Fixture Control page first."); return; }
        if (_capture == null)
        { AppendLog("Raw input capture not initialized."); return; }

        if (!int.TryParse(TargetDpiBox.Text, out int dpi)
            || !double.TryParse(StartHeightBox.Text, out double height)
            || !double.TryParse(IntervalBox.Text, out double interval)
            || !double.TryParse(ThresholdBox.Text, out double threshold))
        { AppendLog("Invalid parameters."); return; }

        int procedure = RadiusCombo.SelectedIndex + 1; // 1, 2, or 3

        _cts = new CancellationTokenSource();
        RunBtn.IsEnabled = false;
        CancelBtn.IsEnabled = true;
        ExportBtn.IsEnabled = false;
        ResultsGrid.ItemsSource = null;
        Progress.Value = 0;
        RunStatus.Text = "Running...";
        LodText.Text = "";
        OverallResultText.Text = "";

        int totalSteps = (int)(height / interval) + 1;

        var runner = new LodTestRunner(_arm, _capture)
        {
            CancelToken = _cts.Token,
            Log = new Progress<string>(msg =>
            {
                AppendLog(msg);
                Dispatcher.Invoke(() => Progress.Value = Math.Min(Progress.Value + 100.0 / totalSteps, 99));
            })
        };

        try
        {
            _lastResult = await Task.Run(() =>
                runner.RunAsync(dpi, height, interval, procedure, threshold), _cts.Token);

            Dispatcher.Invoke(() =>
            {
                ResultsGrid.ItemsSource = _lastResult.HeightResults;
                LodText.Text = $"{_lastResult.DeterminedLodMm:F2} mm";
                OverallResultText.Text = _lastResult.Passed ? "PASS" : "FAIL";
                OverallResultText.Foreground = new SolidColorBrush(
                    _lastResult.Passed ? Colors.LimeGreen : Colors.Red);
                Progress.Value = 100;
                RunStatus.Text = "Complete";
                ExportBtn.IsEnabled = true;
            });
            AppendLog($"LOD = {_lastResult.DeterminedLodMm:F2} mm");
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

    private void ExportBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_lastResult == null) return;

        string folder = string.IsNullOrWhiteSpace(OutputFolderBox.Text)
            ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            : OutputFolderBox.Text;

        string path = Path.Combine(folder, $"LOD_Test_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        var sb = new StringBuilder();
        sb.AppendLine("Height_mm,MeasuredRadius_mm,Pass");
        foreach (var r in _lastResult.HeightResults)
            sb.AppendLine($"{r.HeightMm:F3},{r.MeasuredRadiusMm:F2},{(r.Pass ? "PASS" : "FAIL")}");
        sb.AppendLine($"LOD,{_lastResult.DeterminedLodMm:F3},{(_lastResult.Passed ? "PASS" : "FAIL")}");

        File.WriteAllText(path, sb.ToString());
        AppendLog($"Exported: {path}");
    }
}
