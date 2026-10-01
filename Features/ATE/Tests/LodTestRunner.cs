using MouseATE.Hardware;

namespace MouseATE.Tests;

public record LodHeightResult(
    double HeightMm,
    bool Pass,
    double MeasuredRadiusMm);

public record LodTestResult(
    IReadOnlyList<LodHeightResult> HeightResults,
    double DeterminedLodMm,
    bool Passed);

public class LodTestRunner
{
    private readonly ThreeAxisController _arm;
    private readonly RawInputCapture _capture;

    // Radius sizes corresponding to auto-mode procedure indices
    public static readonly Dictionary<int, double> AutoProcedureRadius = new()
    {
        { 1, 50.0 },
        { 2, 100.0 },
        { 3, 150.0 }
    };

    public LodTestRunner(ThreeAxisController arm, RawInputCapture capture)
    {
        _arm = arm;
        _capture = capture;
    }

    public IProgress<string> Log { get; set; }
    public CancellationToken CancelToken { get; set; }

    /// <summary>
    /// Runs an LOD (Lift-Off Distance) test by stepping the Z axis down from
    /// <paramref name="startHeightMm"/> in <paramref name="intervalMm"/> steps
    /// while running circular auto-mode patterns and measuring mouse movement.
    /// </summary>
    /// <param name="targetDpi">Mouse DPI setting during test.</param>
    /// <param name="startHeightMm">Starting Z height in mm.</param>
    /// <param name="intervalMm">Z step size in mm (typically 0.1).</param>
    /// <param name="autoProcedure">Auto-mode procedure index (1=50mm radius, 2=100mm, 3=150mm).</param>
    /// <param name="passThresholdPct">Minimum % of expected radius counts to count as tracking.</param>
    public async Task<LodTestResult> RunAsync(
        int targetDpi, double startHeightMm, double intervalMm,
        int autoProcedure, double passThresholdPct)
    {
        double expectedRadiusMm = AutoProcedureRadius.GetValueOrDefault(autoProcedure, 50.0);

        // Switch to auto mode for circular path
        Log?.Report("Switching to auto mode...");
        await _arm.SwitchToAutoAsync();

        var heightResults = new List<LodHeightResult>();
        double currentZ = startHeightMm;
        int steps = (int)(startHeightMm / intervalMm) + 1;

        for (int i = 0; i < steps; i++)
        {
            CancelToken.ThrowIfCancellationRequested();
            Log?.Report($"Step {i + 1}/{steps} — Z = {currentZ:F2} mm...");

            await _arm.MoveZAbsAsync(currentZ, 50);
            await Task.Delay(200);

            _capture.Start();
            await _arm.AutoRunAsync(autoProcedure);
            _capture.Stop();

            double measuredRadius = CalcRadius(_capture.Points.ToList(), targetDpi);
            bool pass = measuredRadius >= expectedRadiusMm * passThresholdPct / 100.0;
            heightResults.Add(new LodHeightResult(currentZ, pass, measuredRadius));

            currentZ = Math.Round(currentZ - intervalMm, 4);
            if (currentZ < 0) break;
        }

        // LOD = highest Z where mouse stopped tracking (first Fail from bottom up)
        double lodMm = 0;
        for (int i = heightResults.Count - 1; i >= 0; i--)
        {
            if (!heightResults[i].Pass)
            {
                lodMm = heightResults[i].HeightMm;
                break;
            }
        }

        bool overallPass = lodMm > 0 || heightResults.All(r => r.Pass);
        return new LodTestResult(heightResults, lodMm, overallPass);
    }

    private static double CalcRadius(List<(int dx, int dy)> points, int dpi)
    {
        if (points.Count == 0) return 0;

        // Convert accumulated counts to mm distance
        double totalDx = points.Sum(p => (double)p.dx);
        double totalDy = points.Sum(p => (double)p.dy);

        // Approximate radius from total path extent
        double maxDx = points.Max(p => Math.Abs((double)p.dx));
        double maxDy = points.Max(p => Math.Abs((double)p.dy));
        double extentCounts = Math.Max(maxDx, maxDy);

        double inchesPerCount = 1.0 / dpi;
        double extentMm = extentCounts * inchesPerCount * 25.4;
        return extentMm;
    }
}
