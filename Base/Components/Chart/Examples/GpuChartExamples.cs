using System;
using System.Windows;

namespace Base.Components.Chart.Examples
{
    /// <summary>
    /// Example demonstrating GPU-accelerated chart rendering usage.
    /// This class shows various scenarios and best practices.
    /// </summary>
    public static class GpuChartExamples
    {
        /// <summary>
        /// Example 1: Basic adaptive rendering (recommended approach)
        /// The renderer automatically chooses CPU or GPU based on dataset size.
        /// </summary>
        public static void Example1_AdaptiveRendering()
        {
            // Check GPU availability first
            if (!GpuChartRenderer.IsGpuAvailable)
            {
                Services.Debug.Log("GPU not available. Charts will use CPU rendering.");
                return;
            }

            var renderer = GpuChartRenderer.Instance;
            renderer.DefaultRenderMode = RenderMode.Adaptive;

            // Create sample data
            int dataSize = 10000; // Large dataset
            float[] data = GenerateSampleData(dataSize);

            // Allocate pixel buffer for 800x600 rendering
            int width = 800, height = 600;
            byte[] pixels = new byte[width * height * 4]; // BGRA format

            // Render with adaptive mode
            // For 10k points, GPU will be automatically selected
            renderer.RenderLineChartAdaptive(
                data: data,
                pixels: pixels,
                width: width,
                height: height,
                minY: -2.0f,
                maxY: 2.0f,
                colorR: 255,
                colorG: 100,
                colorB: 100,
                lineThickness: 2.0f,
                mode: RenderMode.Adaptive
            );

            // Check what mode was used
            string reason = renderer.GetRenderModeReason(dataSize, 0, RenderMode.Adaptive);
            Services.Debug.Log($"Rendered with: {reason}");
        }

        /// <summary>
        /// Example 2: Running benchmarks
        /// Compare CPU vs GPU performance for various dataset sizes.
        /// </summary>
        public static void Example2_RunBenchmarks()
        {
            if (!GpuChartRenderer.IsGpuAvailable)
            {
                Services.Debug.Log("GPU not available - cannot run benchmarks");
                return;
            }

            Services.Debug.Log("Starting benchmark...\n");

            string result = ChartRenderingBenchmark.BenchmarkLineChart(
                dataSize: 10000,
                iterations: 50,
                width: 800,
                height: 600
            );

            Services.Debug.Log(result);
        }

        // Helper methods

        private static float[] GenerateSampleData(int size)
        {
            float[] data = new float[size];
            Random rand = new Random(42);
            
            for (int i = 0; i < size; i++)
            {
                // Generate sine wave with noise
                float x = (float)i / size * 10.0f;
                data[i] = (float)(Math.Sin(x) + rand.NextDouble() * 0.2 - 0.1);
            }

            return data;
        }
    }
}
