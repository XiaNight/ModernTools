using MouseATE.Hardware;

namespace MouseATE.Tests;

public enum TestAxis { X, Y }

public record DpiCycleResult(
    int Cycle,
    double ActualCounts,
    double ActualDpi,
    double DeviationPct,
    double AngleDeg,
    double CompensatedDpi,
    double PathLengthDpi);

public record DpiTestResult(
    IReadOnlyList<DpiCycleResult> Cycles,
    double AverageDpi,
    double AverageDeviationPct,
    double ResolutionError);

public class DpiTestRunner
{
    private readonly ThreeAxisController _arm;
    private readonly RawInputCapture _capture;

    // Position to move to before starting test (boundary approach)
    private const double BoundaryMm = 100.0;

    public DpiTestRunner(ThreeAxisController arm, RawInputCapture capture)
    {
        _arm = arm;
        _capture = capture;
    }

    public IProgress<string> Log { get; set; }
    public CancellationToken CancelToken { get; set; }

    /// <summary>
    /// Runs a DPI deviation test on the specified axis.
    /// </summary>
    /// <param name="axis">Axis to test (X sweeps X, Y sweeps Y).</param>
    /// <param name="targetDpi">Target DPI value.</param>
    /// <param name="distanceMm">Sweep distance in mm.</param>
    /// <param name="zOffsetMm">Z height above surface in mm.</param>
    /// <param name="speed">Arm speed 0–800.</param>
    /// <param name="cycles">Number of forward+back cycles.</param>
    public async Task<DpiTestResult> RunAsync(
        TestAxis axis, int targetDpi, double distanceMm,
        double zOffsetMm, int speed, int cycles)
    {
        Log?.Report($"Moving to boundary ({BoundaryMm} mm)...");
        if (axis == TestAxis.X)
            await _arm.MoveXYAbsAsync(BoundaryMm, BoundaryMm, speed);
        else
            await _arm.MoveXYAbsAsync(BoundaryMm, BoundaryMm, speed);

        await _arm.MoveZAbsAsync(zOffsetMm, speed);

        var results = new List<DpiCycleResult>();

        for (int i = 0; i < cycles; i++)
        {
            CancelToken.ThrowIfCancellationRequested();
            Log?.Report($"Cycle {i + 1}/{cycles} — forward sweep...");

            _capture.Start();
            if (axis == TestAxis.X)
                await _arm.MoveXYIncAsync(distanceMm, 0, speed);
            else
                await _arm.MoveXYIncAsync(0, distanceMm, speed);
            _capture.Stop();

            var forwardPoints = _capture.Points.ToList();
            results.Add(CalcResult(i * 2 + 1, forwardPoints, distanceMm, targetDpi));

            Log?.Report($"Cycle {i + 1}/{cycles} — return sweep...");
            _capture.Start();
            if (axis == TestAxis.X)
                await _arm.MoveXYIncAsync(-distanceMm, 0, speed);
            else
                await _arm.MoveXYIncAsync(0, -distanceMm, speed);
            _capture.Stop();

            var returnPoints = _capture.Points.ToList();
            results.Add(CalcResult(i * 2 + 2, returnPoints, distanceMm, targetDpi));
        }

        double avgDpi = results.Average(r => r.ActualDpi);
        double avgDev = results.Average(r => r.DeviationPct);
        // Resolution error: peak-to-peak deviation across all cycles
        double resErr = results.Max(r => r.DeviationPct) - results.Min(r => r.DeviationPct);

        return new DpiTestResult(results, avgDpi, avgDev, resErr);
    }

    private static DpiCycleResult CalcResult(
        int cycle, List<(int dx, int dy)> points, double distanceMm, int targetDpi)
    {
        long sumX = 0, sumY = 0;
        foreach (var (dx, dy) in points) { sumX += dx; sumY += dy; }

        // Actual straight-line counts along primary axis
        double actualCounts = Math.Abs(sumX + sumY);

        // DPI: counts per inch
        double distInch = distanceMm / 25.4;
        double actualDpi = distInch > 0 ? actualCounts / distInch : 0;
        double deviationPct = targetDpi > 0 ? (actualDpi - targetDpi) / targetDpi * 100.0 : 0;

        // Angle deviation (cross-axis drift)
        double angleDeg = sumX != 0 ? Math.Atan2(Math.Abs(sumY), Math.Abs(sumX)) * 180.0 / Math.PI : 0;

        // Compensated DPI using Pythagorean path length (accounts for both axes)
        double compensatedCounts = Math.Sqrt((double)(sumX * sumX + sumY * sumY));
        double compensatedDpi = distInch > 0 ? compensatedCounts / distInch : 0;

        // Path-length DPI (sum of individual step magnitudes)
        double pathCounts = points.Sum(p => Math.Sqrt(p.dx * p.dx + p.dy * p.dy));
        double pathLengthDpi = distInch > 0 ? pathCounts / distInch : 0;

        return new DpiCycleResult(cycle, actualCounts, actualDpi, deviationPct, angleDeg, compensatedDpi, pathLengthDpi);
    }
}
