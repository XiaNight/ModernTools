using System;
using System.Diagnostics;
using System.Text;

namespace Base.Components.Chart
{
    /// <summary>
    /// Utility class for benchmarking chart rendering performance.
    /// Compares CPU vs GPU rendering paths and provides performance metrics.
    /// </summary>
    public static class ChartRenderingBenchmark
    {
        /// <summary>
        /// Benchmarks line chart rendering with both CPU and GPU paths.
        /// </summary>
        /// <param name="dataSize">Number of data points to render</param>
        /// <param name="iterations">Number of iterations for averaging</param>
        /// <param name="width">Render width in pixels</param>
        /// <param name="height">Render height in pixels</param>
        /// <returns>Benchmark results as formatted string</returns>
        public static string BenchmarkLineChart(
            int dataSize,
            int iterations = 100,
            int width = 800,
            int height = 600)
        {
            if (!GpuChartRenderer.IsGpuAvailable)
            {
                return "GPU not available - cannot run benchmark";
            }

            var renderer = GpuChartRenderer.Instance;
            
            // Prepare test data
            float[] data = new float[dataSize];
            Random rand = new Random(42);
            for (int i = 0; i < dataSize; i++)
            {
                data[i] = (float)(Math.Sin(i * 0.1) + rand.NextDouble() * 0.1);
            }

            byte[] pixels = new byte[width * height * 4];

            // Warm-up
            renderer.RenderLineChart(data, pixels, width, height, -1.5f, 1.5f, 255, 100, 100, 2.0f);
            if (GpuAcceleratedRenderer.IsGpuAvailable)
            {
                renderer.RenderLineChartAdaptive(data, pixels, width, height, -1.5f, 1.5f, 255, 100, 100, 2.0f, RenderMode.GPU);
            }

            // Benchmark CPU path
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                renderer.RenderLineChart(data, pixels, width, height, -1.5f, 1.5f, 255, 100, 100, 2.0f);
            }
            sw.Stop();
            double cpuTimeMs = sw.Elapsed.TotalMilliseconds;
            double cpuAvgMs = cpuTimeMs / iterations;

            // Benchmark GPU path (if available)
            double gpuTimeMs = 0;
            double gpuAvgMs = 0;
            double speedup = 0;

            if (GpuAcceleratedRenderer.IsGpuAvailable)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                sw.Restart();
                for (int i = 0; i < iterations; i++)
                {
                    renderer.RenderLineChartAdaptive(data, pixels, width, height, -1.5f, 1.5f, 255, 100, 100, 2.0f, RenderMode.GPU);
                }
                sw.Stop();
                gpuTimeMs = sw.Elapsed.TotalMilliseconds;
                gpuAvgMs = gpuTimeMs / iterations;
                speedup = cpuAvgMs / gpuAvgMs;
            }

            // Format results
            var sb = new StringBuilder();
            sb.AppendLine("=== Line Chart Rendering Benchmark ===");
            sb.AppendLine($"Dataset Size:    {dataSize:N0} points");
            sb.AppendLine($"Canvas Size:     {width}x{height} pixels");
            sb.AppendLine($"Iterations:      {iterations}");
            sb.AppendLine();
            sb.AppendLine($"CPU Total Time:  {cpuTimeMs:F2} ms");
            sb.AppendLine($"CPU Avg Time:    {cpuAvgMs:F3} ms/frame");
            sb.AppendLine($"CPU Max FPS:     {1000.0 / cpuAvgMs:F1} fps");

            if (GpuAcceleratedRenderer.IsGpuAvailable)
            {
                sb.AppendLine();
                sb.AppendLine($"GPU Total Time:  {gpuTimeMs:F2} ms");
                sb.AppendLine($"GPU Avg Time:    {gpuAvgMs:F3} ms/frame");
                sb.AppendLine($"GPU Max FPS:     {1000.0 / gpuAvgMs:F1} fps");
                sb.AppendLine();
                sb.AppendLine($"Speedup:         {speedup:F2}x");
                sb.AppendLine($"Performance:     {(speedup > 1 ? "GPU FASTER" : "CPU FASTER")}");
            }

            // Add recommendation
            var selectedMode = AdaptiveRenderingStrategy.SelectRenderMode(
                dataSize, 0, RenderMode.Adaptive, GpuAcceleratedRenderer.IsGpuAvailable);
            var reason = renderer.GetRenderModeReason(dataSize, 0, RenderMode.Adaptive);
            
            sb.AppendLine();
            sb.AppendLine($"Recommended:     {selectedMode}");
            sb.AppendLine($"Reason:          {reason}");

            return sb.ToString();
        }

        /// <summary>
        /// Benchmarks scatter chart rendering with both CPU and GPU paths.
        /// </summary>
        public static string BenchmarkScatterChart(
            int pointCount,
            int iterations = 100,
            int width = 800,
            int height = 600)
        {
            if (!GpuChartRenderer.IsGpuAvailable)
            {
                return "GPU not available - cannot run benchmark";
            }

            var renderer = GpuChartRenderer.Instance;
            
            // Prepare test data
            float[] xData = new float[pointCount];
            float[] yData = new float[pointCount];
            Random rand = new Random(42);
            for (int i = 0; i < pointCount; i++)
            {
                xData[i] = (float)(rand.NextDouble() * 2 - 1);
                yData[i] = (float)(rand.NextDouble() * 2 - 1);
            }

            byte[] pixels = new byte[width * height * 4];

            // Warm-up
            renderer.RenderScatterChart(xData, yData, pixels, width, height, -1f, 1f, -1f, 1f, 100, 255, 100, 4.0f);
            if (GpuAcceleratedRenderer.IsGpuAvailable)
            {
                renderer.RenderScatterChartAdaptive(xData, yData, pixels, width, height, -1f, 1f, -1f, 1f, 100, 255, 100, 4.0f, RenderMode.GPU);
            }

            // Benchmark CPU path
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++)
            {
                renderer.RenderScatterChart(xData, yData, pixels, width, height, -1f, 1f, -1f, 1f, 100, 255, 100, 4.0f);
            }
            sw.Stop();
            double cpuTimeMs = sw.Elapsed.TotalMilliseconds;
            double cpuAvgMs = cpuTimeMs / iterations;

            // Benchmark GPU path (if available)
            double gpuTimeMs = 0;
            double gpuAvgMs = 0;
            double speedup = 0;

            if (GpuAcceleratedRenderer.IsGpuAvailable)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                sw.Restart();
                for (int i = 0; i < iterations; i++)
                {
                    renderer.RenderScatterChartAdaptive(xData, yData, pixels, width, height, -1f, 1f, -1f, 1f, 100, 255, 100, 4.0f, RenderMode.GPU);
                }
                sw.Stop();
                gpuTimeMs = sw.Elapsed.TotalMilliseconds;
                gpuAvgMs = gpuTimeMs / iterations;
                speedup = cpuAvgMs / gpuAvgMs;
            }

            // Format results
            var sb = new StringBuilder();
            sb.AppendLine("=== Scatter Chart Rendering Benchmark ===");
            sb.AppendLine($"Point Count:     {pointCount:N0} points");
            sb.AppendLine($"Canvas Size:     {width}x{height} pixels");
            sb.AppendLine($"Iterations:      {iterations}");
            sb.AppendLine();
            sb.AppendLine($"CPU Total Time:  {cpuTimeMs:F2} ms");
            sb.AppendLine($"CPU Avg Time:    {cpuAvgMs:F3} ms/frame");
            sb.AppendLine($"CPU Max FPS:     {1000.0 / cpuAvgMs:F1} fps");

            if (GpuAcceleratedRenderer.IsGpuAvailable)
            {
                sb.AppendLine();
                sb.AppendLine($"GPU Total Time:  {gpuTimeMs:F2} ms");
                sb.AppendLine($"GPU Avg Time:    {gpuAvgMs:F3} ms/frame");
                sb.AppendLine($"GPU Max FPS:     {1000.0 / gpuAvgMs:F1} fps");
                sb.AppendLine();
                sb.AppendLine($"Speedup:         {speedup:F2}x");
                sb.AppendLine($"Performance:     {(speedup > 1 ? "GPU FASTER" : "CPU FASTER")}");
            }

            // Add recommendation
            var selectedMode = AdaptiveRenderingStrategy.SelectRenderMode(
                pointCount, 0, RenderMode.Adaptive, GpuAcceleratedRenderer.IsGpuAvailable);
            var reason = renderer.GetRenderModeReason(pointCount, 0, RenderMode.Adaptive);
            
            sb.AppendLine();
            sb.AppendLine($"Recommended:     {selectedMode}");
            sb.AppendLine($"Reason:          {reason}");

            return sb.ToString();
        }

        /// <summary>
        /// Runs a comprehensive benchmark suite across various dataset sizes.
        /// </summary>
        public static string RunComprehensiveBenchmark()
        {
            var sb = new StringBuilder();
            sb.AppendLine("╔════════════════════════════════════════════════════════════╗");
            sb.AppendLine("║   GPU-Accelerated Chart Rendering - Benchmark Suite      ║");
            sb.AppendLine("╚════════════════════════════════════════════════════════════╝");
            sb.AppendLine();

            int[] testSizes = { 500, 1000, 2500, 5000, 10000, 25000, 50000 };

            foreach (var size in testSizes)
            {
                sb.AppendLine(BenchmarkLineChart(size, iterations: 50));
                sb.AppendLine();
                sb.AppendLine(new string('─', 60));
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
