using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using ComputeSharp;

namespace Base.Components.Chart
{
    /// <summary>
    /// Provides high-performance chart rendering with GPU requirement validation using ComputeSharp.
    /// This class ensures GPU availability at construction time and throws 
    /// <see cref="GpuNotAvailableException"/> if no compatible GPU is found.
    /// Supports both CPU-based parallel processing and true GPU compute shader acceleration.
    /// Uses adaptive rendering strategy to automatically choose the best rendering path.
    /// </summary>
    public sealed class GpuChartRenderer : IDisposable
    {
        private static readonly Lazy<GpuChartRenderer> _instance = new Lazy<GpuChartRenderer>(
            () => new GpuChartRenderer(),
            LazyThreadSafetyMode.ExecutionAndPublication);

        private readonly GraphicsDevice _device;
        private readonly GpuAcceleratedRenderer _gpuAccelerated;
        private bool _disposed;

        /// <summary>
        /// Default render mode for all chart rendering operations.
        /// Can be overridden per-call.
        /// </summary>
        public RenderMode DefaultRenderMode { get; set; } = RenderMode.Adaptive;

        /// <summary>
        /// Gets the shared instance of the GPU chart renderer.
        /// </summary>
        /// <exception cref="GpuNotAvailableException">Thrown when no compatible GPU is available.</exception>
        public static GpuChartRenderer Instance => _instance.Value;

        /// <summary>
        /// Gets a value indicating whether a GPU is available for rendering.
        /// </summary>
        public static bool IsGpuAvailable
        {
            get
            {
                try
                {
                    return GraphicsDevice.GetDefault() != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Ensures a GPU is available, throwing an exception if not.
        /// </summary>
        /// <exception cref="GpuNotAvailableException">Thrown when no compatible GPU is available.</exception>
        public static void EnsureGpuAvailable()
        {
            if (!IsGpuAvailable)
            {
                throw new GpuNotAvailableException();
            }
        }

        private GpuChartRenderer()
        {
            try
            {
                _device = GraphicsDevice.GetDefault();
                if (_device == null)
                {
                    throw new GpuNotAvailableException();
                }
                
                // Initialize GPU-accelerated renderer
                _gpuAccelerated = GpuAcceleratedRenderer.Instance;
            }
            catch (Exception ex) when (ex is not GpuNotAvailableException)
            {
                throw new GpuNotAvailableException("Failed to initialize GPU device for chart rendering.", ex);
            }
        }

        /// <summary>
        /// Gets the underlying graphics device used for GPU validation.
        /// </summary>
        public GraphicsDevice Device
        {
            get
            {
                ThrowIfDisposed();
                return _device;
            }
        }

        /// <summary>
        /// Renders a line chart with adaptive mode selection (CPU vs GPU).
        /// Automatically chooses the best rendering path based on dataset size.
        /// </summary>
        public void RenderLineChartAdaptive(
            ReadOnlySpan<float> data,
            Span<byte> pixels,
            int width,
            int height,
            float minY,
            float maxY,
            byte colorR,
            byte colorG,
            byte colorB,
            float lineThickness,
            RenderMode mode = RenderMode.Adaptive,
            int updateFrequency = 0)
        {
            var selectedMode = AdaptiveRenderingStrategy.SelectRenderMode(
                data.Length,
                updateFrequency,
                mode == RenderMode.Adaptive ? DefaultRenderMode : mode,
                IsGpuAvailable);

            if (selectedMode == RenderMode.GPU && IsGpuAvailable)
            {
                _gpuAccelerated.RenderLineChartGpu(
                    data, pixels, width, height, minY, maxY,
                    colorR, colorG, colorB, lineThickness);
            }
            else
            {
                RenderLineChart(
                    data, pixels, width, height, minY, maxY,
                    colorR, colorG, colorB, lineThickness);
            }
        }

        /// <summary>
        /// Renders a scatter chart with adaptive mode selection (CPU vs GPU).
        /// Automatically chooses the best rendering path based on dataset size.
        /// </summary>
        public void RenderScatterChartAdaptive(
            ReadOnlySpan<float> xData,
            ReadOnlySpan<float> yData,
            Span<byte> pixels,
            int width,
            int height,
            float minX,
            float maxX,
            float minY,
            float maxY,
            byte colorR,
            byte colorG,
            byte colorB,
            float dotRadius,
            RenderMode mode = RenderMode.Adaptive,
            int updateFrequency = 0)
        {
            var selectedMode = AdaptiveRenderingStrategy.SelectRenderMode(
                xData.Length,
                updateFrequency,
                mode == RenderMode.Adaptive ? DefaultRenderMode : mode,
                IsGpuAvailable);

            if (selectedMode == RenderMode.GPU && IsGpuAvailable)
            {
                _gpuAccelerated.RenderScatterChartGpu(
                    xData, yData, pixels, width, height, minX, maxX, minY, maxY,
                    colorR, colorG, colorB, dotRadius);
            }
            else
            {
                RenderScatterChart(
                    xData, yData, pixels, width, height, minX, maxX, minY, maxY,
                    colorR, colorG, colorB, dotRadius);
            }
        }

        /// <summary>
        /// Gets a description of why a particular render mode was selected.
        /// </summary>
        public string GetRenderModeReason(int dataPointCount, int updateFrequency, RenderMode requestedMode)
        {
            var selectedMode = AdaptiveRenderingStrategy.SelectRenderMode(
                dataPointCount,
                updateFrequency,
                requestedMode == RenderMode.Adaptive ? DefaultRenderMode : requestedMode,
                IsGpuAvailable);

            return AdaptiveRenderingStrategy.GetRenderModeReason(
                dataPointCount,
                updateFrequency,
                selectedMode,
                IsGpuAvailable);
        }

        /// <summary>
        /// Renders a line chart to the specified BGRA pixel buffer using high-performance CPU parallel processing.
        /// This is the CPU-only rendering path, used when GPU acceleration is not beneficial or available.
        /// </summary>
        public void RenderLineChart(
            ReadOnlySpan<float> data,
            Span<byte> pixels,
            int width,
            int height,
            float minY,
            float maxY,
            byte colorR,
            byte colorG,
            byte colorB,
            float lineThickness)
        {
            ThrowIfDisposed();

            if (data.Length < 2 || width <= 0 || height <= 0)
                return;

            if (pixels.Length < width * height * 4)
                throw new ArgumentException("Pixel buffer is too small.", nameof(pixels));

            float yRange = maxY - minY;
            if (yRange <= 0) yRange = 1e-6f;

            int dataLength = data.Length;
            float[] dataArray = ArrayPool<float>.Shared.Rent(dataLength);
            data.CopyTo(dataArray);

            // Create a backing array for parallel write
            byte[] pixelArray = ArrayPool<byte>.Shared.Rent(pixels.Length);
            pixels.CopyTo(pixelArray);

            try
            {
                int halfThickness = Math.Max(1, (int)(lineThickness / 2));
                int pixelLen = pixels.Length;

                // Process each x column in parallel for high performance
                Parallel.For(0, width - 1, x =>
                {
                    float dataIdxF1 = (float)x / (width - 1) * (dataLength - 1);
                    float dataIdxF2 = (float)(x + 1) / (width - 1) * (dataLength - 1);

                    // Interpolate between data points for smooth lines
                    int dataIdx1Low = Math.Clamp((int)dataIdxF1, 0, dataLength - 1);
                    int dataIdx1High = Math.Clamp(dataIdx1Low + 1, 0, dataLength - 1);
                    float t1 = dataIdxF1 - dataIdx1Low;
                    float v1 = dataArray[dataIdx1Low] * (1 - t1) + dataArray[dataIdx1High] * t1;

                    int dataIdx2Low = Math.Clamp((int)dataIdxF2, 0, dataLength - 1);
                    int dataIdx2High = Math.Clamp(dataIdx2Low + 1, 0, dataLength - 1);
                    float t2 = dataIdxF2 - dataIdx2Low;
                    float v2 = dataArray[dataIdx2Low] * (1 - t2) + dataArray[dataIdx2High] * t2;

                    float ny1 = 1.0f - (v1 - minY) / yRange;
                    float ny2 = 1.0f - (v2 - minY) / yRange;

                    ny1 = Math.Clamp(ny1, 0f, 1f);
                    ny2 = Math.Clamp(ny2, 0f, 1f);

                    int py1 = (int)(ny1 * (height - 1));
                    int py2 = (int)(ny2 * (height - 1));

                    DrawLineSegmentArray(pixelArray, width, height, x, py1, x + 1, py2, colorR, colorG, colorB, halfThickness);
                });

                // Copy back to span
                pixelArray.AsSpan(0, pixels.Length).CopyTo(pixels);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(dataArray);
                ArrayPool<byte>.Shared.Return(pixelArray);
            }
        }

        /// <summary>
        /// Renders a scatter/dot chart to the specified BGRA pixel buffer using high-performance parallel processing.
        /// </summary>
        public void RenderScatterChart(
            ReadOnlySpan<float> xData,
            ReadOnlySpan<float> yData,
            Span<byte> pixels,
            int width,
            int height,
            float minX,
            float maxX,
            float minY,
            float maxY,
            byte colorR,
            byte colorG,
            byte colorB,
            float dotRadius)
        {
            ThrowIfDisposed();

            if (xData.Length == 0 || xData.Length != yData.Length || width <= 0 || height <= 0)
                return;

            if (pixels.Length < width * height * 4)
                throw new ArgumentException("Pixel buffer is too small.", nameof(pixels));

            float xRange = maxX - minX;
            float yRange = maxY - minY;
            if (xRange <= 0) xRange = 1e-6f;
            if (yRange <= 0) yRange = 1e-6f;

            int dataLength = xData.Length;
            float[] xArray = ArrayPool<float>.Shared.Rent(dataLength);
            float[] yArray = ArrayPool<float>.Shared.Rent(dataLength);
            xData.CopyTo(xArray);
            yData.CopyTo(yArray);

            // Create a backing array for parallel write
            byte[] pixelArray = ArrayPool<byte>.Shared.Rent(pixels.Length);
            pixels.CopyTo(pixelArray);

            try
            {
                int radius = Math.Max(1, (int)dotRadius);
                int radiusSq = radius * radius;

                Parallel.For(0, dataLength, i =>
                {
                    float nx = (xArray[i] - minX) / xRange;
                    float ny = 1.0f - (yArray[i] - minY) / yRange;

                    nx = Math.Clamp(nx, 0f, 1f);
                    ny = Math.Clamp(ny, 0f, 1f);

                    int px = (int)(nx * (width - 1));
                    int py = (int)(ny * (height - 1));

                    DrawDotArray(pixelArray, width, height, px, py, colorR, colorG, colorB, radius, radiusSq);
                });

                // Copy back to span
                pixelArray.AsSpan(0, pixels.Length).CopyTo(pixels);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(xArray);
                ArrayPool<float>.Shared.Return(yArray);
                ArrayPool<byte>.Shared.Return(pixelArray);
            }
        }

        /// <summary>
        /// Renders connected line segments between XY points to the specified BGRA pixel buffer.
        /// </summary>
        public void RenderXYLineChart(
            ReadOnlySpan<float> xData,
            ReadOnlySpan<float> yData,
            Span<byte> pixels,
            int width,
            int height,
            float minX,
            float maxX,
            float minY,
            float maxY,
            byte colorR,
            byte colorG,
            byte colorB,
            float lineThickness)
        {
            ThrowIfDisposed();

            if (xData.Length < 2 || xData.Length != yData.Length || width <= 0 || height <= 0)
                return;

            if (pixels.Length < width * height * 4)
                throw new ArgumentException("Pixel buffer is too small.", nameof(pixels));

            float xRange = maxX - minX;
            float yRange = maxY - minY;
            if (xRange <= 0) xRange = 1e-6f;
            if (yRange <= 0) yRange = 1e-6f;

            int dataLength = xData.Length;
            float[] xArray = ArrayPool<float>.Shared.Rent(dataLength);
            float[] yArray = ArrayPool<float>.Shared.Rent(dataLength);
            xData.CopyTo(xArray);
            yData.CopyTo(yArray);

            // Create a backing array for parallel write
            byte[] pixelArray = ArrayPool<byte>.Shared.Rent(pixels.Length);
            pixels.CopyTo(pixelArray);

            try
            {
                int halfThickness = Math.Max(1, (int)(lineThickness / 2));

                // Render line segments between consecutive points
                Parallel.For(0, dataLength - 1, i =>
                {
                    float nx1 = (xArray[i] - minX) / xRange;
                    float ny1 = 1.0f - (yArray[i] - minY) / yRange;
                    float nx2 = (xArray[i + 1] - minX) / xRange;
                    float ny2 = 1.0f - (yArray[i + 1] - minY) / yRange;

                    nx1 = Math.Clamp(nx1, 0f, 1f);
                    ny1 = Math.Clamp(ny1, 0f, 1f);
                    nx2 = Math.Clamp(nx2, 0f, 1f);
                    ny2 = Math.Clamp(ny2, 0f, 1f);

                    int px1 = (int)(nx1 * (width - 1));
                    int py1 = (int)(ny1 * (height - 1));
                    int px2 = (int)(nx2 * (width - 1));
                    int py2 = (int)(ny2 * (height - 1));

                    DrawLineSegmentArray(pixelArray, width, height, px1, py1, px2, py2, colorR, colorG, colorB, halfThickness);
                });

                // Copy back to span
                pixelArray.AsSpan(0, pixels.Length).CopyTo(pixels);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(xArray);
                ArrayPool<float>.Shared.Return(yArray);
                ArrayPool<byte>.Shared.Return(pixelArray);
            }
        }

        /// <summary>
        /// Renders a spectrogram to the specified BGRA pixel buffer using high-performance parallel processing.
        /// </summary>
        public void RenderSpectrogram(
            ReadOnlySpan<float> magnitudes,
            Span<byte> pixels,
            int width,
            int height,
            int pixelShift,
            float minDb,
            float maxDb,
            float minHz,
            float maxHz,
            int sampleRate,
            int fftLength)
        {
            ThrowIfDisposed();

            if (magnitudes.Length == 0 || width <= 0 || height <= 0)
                return;

            if (pixels.Length < width * height * 4)
                throw new ArgumentException("Pixel buffer is too small.", nameof(pixels));

            float dbRange = maxDb - minDb;
            if (dbRange <= 0.0001f) dbRange = 0.0001f;

            // Create a backing array for parallel write
            int pixelLen = pixels.Length;
            byte[] pixelArray = ArrayPool<byte>.Shared.Rent(pixelLen);
            pixels.CopyTo(pixelArray);

            // Shift existing pixels to the left using efficient memory operations
            if (pixelShift > 0 && pixelShift < width)
            {
                int stride = width * 4;
                for (int y = 0; y < height; y++)
                {
                    int rowStart = y * stride;
                    int srcOffset = rowStart + pixelShift * 4;
                    int dstOffset = rowStart;
                    int copyLen = (width - pixelShift) * 4;

                    Buffer.BlockCopy(pixelArray, srcOffset, pixelArray, dstOffset, copyLen);

                    // Clear the shifted-in area
                    Array.Clear(pixelArray, rowStart + copyLen, pixelShift * 4);
                }
            }
            else if (pixelShift >= width)
            {
                Array.Clear(pixelArray, 0, pixelLen);
            }

            int bins = magnitudes.Length;
            float[] magArray = ArrayPool<float>.Shared.Rent(bins);
            magnitudes.CopyTo(magArray);

            try
            {
                float nyquist = sampleRate * 0.5f;
                float actualMinHz = Math.Max(0f, minHz);
                float actualMaxHz = Math.Min(nyquist, maxHz);
                if (actualMaxHz <= actualMinHz) actualMaxHz = actualMinHz + 1e-6f;
                float hzRange = actualMaxHz - actualMinHz;

                // Render new column(s) in parallel
                Parallel.For(0, height, y =>
                {
                    float fy = 1.0f - (float)y / (height - 1);
                    float hz = actualMinHz + fy * hzRange;
                    int binIndex = Math.Clamp((int)(hz * fftLength / sampleRate), 0, bins - 1);

                    float mag = magArray[binIndex];
                    if (mag <= 0) mag = 1e-10f;

                    float db = 10f * MathF.Log10(mag);
                    float norm = Math.Clamp((db - minDb) / dbRange, 0f, 1f);

                    // Color mapping: black -> purple -> orange -> white
                    GetSpectrogramColor(norm, out byte r, out byte g, out byte b);

                    // Write to new column(s)
                    for (int x = width - pixelShift; x < width; x++)
                    {
                        if (x >= 0)
                        {
                            int idx = (y * width + x) * 4;
                            pixelArray[idx + 0] = b;
                            pixelArray[idx + 1] = g;
                            pixelArray[idx + 2] = r;
                            pixelArray[idx + 3] = 255;
                        }
                    }
                });

                // Copy back to span
                pixelArray.AsSpan(0, pixelLen).CopyTo(pixels);
            }
            finally
            {
                ArrayPool<float>.Shared.Return(magArray);
                ArrayPool<byte>.Shared.Return(pixelArray);
            }
        }

        /// <summary>
        /// Renders a strip chart to the specified BGRA pixel buffer using high-performance parallel processing.
        /// </summary>
        public void RenderStripChart(
            ReadOnlySpan<long> ticks,
            ReadOnlySpan<float> values,
            Span<byte> pixels,
            int width,
            int height,
            long startTick,
            long endTick,
            float minY,
            float maxY,
            byte lineColorR,
            byte lineColorG,
            byte lineColorB,
            float lineThickness,
            bool renderLines,
            bool renderDots,
            byte dotColorR,
            byte dotColorG,
            byte dotColorB,
            float dotRadius)
        {
            ThrowIfDisposed();

            if (ticks.Length == 0 || ticks.Length != values.Length || width <= 0 || height <= 0)
                return;

            if (pixels.Length < width * height * 4)
                throw new ArgumentException("Pixel buffer is too small.", nameof(pixels));

            float yRange = maxY - minY;
            if (yRange <= 0) yRange = 1e-6f;

            double tickSpan = Math.Max(1.0, (double)(endTick - startTick));

            int dataLength = ticks.Length;
            long[] tickArray = ArrayPool<long>.Shared.Rent(dataLength);
            float[] valueArray = ArrayPool<float>.Shared.Rent(dataLength);
            ticks.CopyTo(tickArray);
            values.CopyTo(valueArray);

            // Create a backing array for parallel write
            int pixelLen = pixels.Length;
            byte[] pixelArray = ArrayPool<byte>.Shared.Rent(pixelLen);
            pixels.CopyTo(pixelArray);

            try
            {
                // Render lines only if renderLines is true
                if (renderLines && dataLength > 1)
                {
                    int halfThickness = Math.Max(1, (int)(lineThickness / 2));

                    Parallel.For(0, dataLength - 1, i =>
                    {
                        long t1 = tickArray[i];
                        long t2 = tickArray[i + 1];
                        float v1 = valueArray[i];
                        float v2 = valueArray[i + 1];

                        if (t1 < startTick && t2 < startTick) return;

                        float nx1 = (float)((t1 - startTick) / tickSpan);
                        float nx2 = (float)((t2 - startTick) / tickSpan);
                        float ny1 = 1.0f - (v1 - minY) / yRange;
                        float ny2 = 1.0f - (v2 - minY) / yRange;

                        nx1 = Math.Clamp(nx1, 0f, 1f);
                        nx2 = Math.Clamp(nx2, 0f, 1f);
                        ny1 = Math.Clamp(ny1, 0f, 1f);
                        ny2 = Math.Clamp(ny2, 0f, 1f);

                        int x1 = (int)(nx1 * (width - 1));
                        int y1 = (int)(ny1 * (height - 1));
                        int x2 = (int)(nx2 * (width - 1));
                        int y2 = (int)(ny2 * (height - 1));

                        DrawLineSegmentArray(pixelArray, width, height, x1, y1, x2, y2, lineColorR, lineColorG, lineColorB, halfThickness);
                    });
                }

                // Render dots only if renderDots is true
                if (renderDots)
                {
                    int radius = Math.Max(1, (int)dotRadius);
                    int radiusSq = radius * radius;

                    Parallel.For(0, dataLength, i =>
                    {
                        long t = tickArray[i];
                        float v = valueArray[i];

                        if (t < startTick) return;

                        float nx = (float)((t - startTick) / tickSpan);
                        float ny = 1.0f - (v - minY) / yRange;

                        nx = Math.Clamp(nx, 0f, 1f);
                        ny = Math.Clamp(ny, 0f, 1f);

                        int px = (int)(nx * (width - 1));
                        int py = (int)(ny * (height - 1));

                        DrawDotArray(pixelArray, width, height, px, py, dotColorR, dotColorG, dotColorB, radius, radiusSq);
                    });
                }

                // Copy back to span
                pixelArray.AsSpan(0, pixelLen).CopyTo(pixels);
            }
            finally
            {
                ArrayPool<long>.Shared.Return(tickArray);
                ArrayPool<float>.Shared.Return(valueArray);
                ArrayPool<byte>.Shared.Return(pixelArray);
            }
        }

        // Helper methods for drawing primitives - Array versions for parallel operations
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawLineSegmentArray(byte[] pixels, int width, int height, int x0, int y0, int x1, int y1, byte r, byte g, byte b, int halfThickness)
        {
            // Use anti-aliased thick line rendering with circular endpoints
            float lineRadius = halfThickness;
            float lineRadiusSq = lineRadius * lineRadius;

            // Calculate line vector and length
            float dx = x1 - x0;
            float dy = y1 - y0;
            float lineLength = MathF.Sqrt(dx * dx + dy * dy);

            if (lineLength < 0.001f)
            {
                // Degenerate case - just draw a dot
                DrawDotArray(pixels, width, height, x0, y0, r, g, b, halfThickness, halfThickness * halfThickness);
                return;
            }

            // Normalize direction
            float ndx = dx / lineLength;
            float ndy = dy / lineLength;

            // Perpendicular vector for thickness
            float px = -ndy;
            float py = ndx;

            // Calculate bounding box with padding for thickness
            int minX = Math.Max(0, Math.Min(x0, x1) - halfThickness - 1);
            int maxX = Math.Min(width - 1, Math.Max(x0, x1) + halfThickness + 1);
            int minY = Math.Max(0, Math.Min(y0, y1) - halfThickness - 1);
            int maxY = Math.Min(height - 1, Math.Max(y0, y1) + halfThickness + 1);

            // For each pixel in the bounding box, calculate distance to line segment
            for (int iy = minY; iy <= maxY; iy++)
            {
                for (int ix = minX; ix <= maxX; ix++)
                {
                    // Vector from line start to pixel
                    float vx = ix - x0;
                    float vy = iy - y0;

                    // Project onto line direction
                    float t = vx * ndx + vy * ndy;

                    // Clamp t to line segment
                    t = Math.Clamp(t, 0f, lineLength);

                    // Closest point on line segment
                    float closestX = x0 + t * ndx;
                    float closestY = y0 + t * ndy;

                    // Distance from pixel to closest point
                    float distX = ix - closestX;
                    float distY = iy - closestY;
                    float distSq = distX * distX + distY * distY;

                    // Check if within line thickness (circular cross-section)
                    if (distSq <= lineRadiusSq)
                    {
                        int idx = (iy * width + ix) * 4;

                        // Apply soft edge anti-aliasing
                        float dist = MathF.Sqrt(distSq);
                        float edgeDist = lineRadius - dist;

                        if (edgeDist >= 1.0f)
                        {
                            // Fully inside - solid color
                            pixels[idx + 0] = b;
                            pixels[idx + 1] = g;
                            pixels[idx + 2] = r;
                            pixels[idx + 3] = 255;
                        }
                        else if (edgeDist > 0)
                        {
                            // Edge pixel - blend with existing
                            byte alpha = (byte)(edgeDist * 255);
                            byte existingB = pixels[idx + 0];
                            byte existingG = pixels[idx + 1];
                            byte existingR = pixels[idx + 2];
                            byte existingA = pixels[idx + 3];

                            // Alpha blend
                            int newA = alpha + existingA * (255 - alpha) / 255;
                            if (newA > 0)
                            {
                                pixels[idx + 0] = (byte)((b * alpha + existingB * existingA * (255 - alpha) / 255) / newA);
                                pixels[idx + 1] = (byte)((g * alpha + existingG * existingA * (255 - alpha) / 255) / newA);
                                pixels[idx + 2] = (byte)((r * alpha + existingR * existingA * (255 - alpha) / 255) / newA);
                                pixels[idx + 3] = (byte)Math.Min(255, newA);
                            }
                        }
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawDotArray(byte[] pixels, int width, int height, int cx, int cy, byte r, byte g, byte b, int radius, int radiusSq)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (dx * dx + dy * dy <= radiusSq)
                    {
                        int px = cx + dx;
                        int py = cy + dy;
                        if (px >= 0 && px < width && py >= 0 && py < height)
                        {
                            int idx = (py * width + px) * 4;
                            pixels[idx + 0] = b;
                            pixels[idx + 1] = g;
                            pixels[idx + 2] = r;
                            pixels[idx + 3] = 255;
                        }
                    }
                }
            }
        }

        // Helper methods for drawing primitives - Span versions (not used in parallel)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawLineSegment(Span<byte> pixels, int width, int height, int x0, int y0, int x1, int y1, byte r, byte g, byte b, int halfThickness)
        {
            // Use anti-aliased thick line rendering with circular endpoints
            float lineRadius = halfThickness;
            float lineRadiusSq = lineRadius * lineRadius;

            // Calculate line vector and length
            float dx = x1 - x0;
            float dy = y1 - y0;
            float lineLength = MathF.Sqrt(dx * dx + dy * dy);

            if (lineLength < 0.001f)
            {
                // Degenerate case - just draw a dot
                DrawDot(pixels, width, height, x0, y0, r, g, b, halfThickness, halfThickness * halfThickness);
                return;
            }

            // Normalize direction
            float ndx = dx / lineLength;
            float ndy = dy / lineLength;

            // Calculate bounding box with padding for thickness
            int minX = Math.Max(0, Math.Min(x0, x1) - halfThickness - 1);
            int maxX = Math.Min(width - 1, Math.Max(x0, x1) + halfThickness + 1);
            int minY = Math.Max(0, Math.Min(y0, y1) - halfThickness - 1);
            int maxY = Math.Min(height - 1, Math.Max(y0, y1) + halfThickness + 1);

            // For each pixel in the bounding box, calculate distance to line segment
            for (int iy = minY; iy <= maxY; iy++)
            {
                for (int ix = minX; ix <= maxX; ix++)
                {
                    // Vector from line start to pixel
                    float vx = ix - x0;
                    float vy = iy - y0;

                    // Project onto line direction
                    float t = vx * ndx + vy * ndy;

                    // Clamp t to line segment
                    t = Math.Clamp(t, 0f, lineLength);

                    // Closest point on line segment
                    float closestX = x0 + t * ndx;
                    float closestY = y0 + t * ndy;

                    // Distance from pixel to closest point
                    float distX = ix - closestX;
                    float distY = iy - closestY;
                    float distSq = distX * distX + distY * distY;

                    // Check if within line thickness (circular cross-section)
                    if (distSq <= lineRadiusSq)
                    {
                        int idx = (iy * width + ix) * 4;

                        // Apply soft edge anti-aliasing
                        float dist = MathF.Sqrt(distSq);
                        float edgeDist = lineRadius - dist;

                        if (edgeDist >= 1.0f)
                        {
                            // Fully inside - solid color
                            pixels[idx + 0] = b;
                            pixels[idx + 1] = g;
                            pixels[idx + 2] = r;
                            pixels[idx + 3] = 255;
                        }
                        else if (edgeDist > 0)
                        {
                            // Edge pixel - blend with existing
                            byte alpha = (byte)(edgeDist * 255);
                            byte existingB = pixels[idx + 0];
                            byte existingG = pixels[idx + 1];
                            byte existingR = pixels[idx + 2];
                            byte existingA = pixels[idx + 3];

                            // Alpha blend
                            int newA = alpha + existingA * (255 - alpha) / 255;
                            if (newA > 0)
                            {
                                pixels[idx + 0] = (byte)((b * alpha + existingB * existingA * (255 - alpha) / 255) / newA);
                                pixels[idx + 1] = (byte)((g * alpha + existingG * existingA * (255 - alpha) / 255) / newA);
                                pixels[idx + 2] = (byte)((r * alpha + existingR * existingA * (255 - alpha) / 255) / newA);
                                pixels[idx + 3] = (byte)Math.Min(255, newA);
                            }
                        }
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawDot(Span<byte> pixels, int width, int height, int cx, int cy, byte r, byte g, byte b, int radius, int radiusSq)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                {
                    if (dx * dx + dy * dy <= radiusSq)
                    {
                        int px = cx + dx;
                        int py = cy + dy;
                        if (px >= 0 && px < width && py >= 0 && py < height)
                        {
                            int idx = (py * width + px) * 4;
                            pixels[idx + 0] = b;
                            pixels[idx + 1] = g;
                            pixels[idx + 2] = r;
                            pixels[idx + 3] = 255;
                        }
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void GetSpectrogramColor(float t, out byte r, out byte g, out byte b)
        {
            // Color stops: black -> deep purple -> magenta -> orange -> yellow-white
            float r1, g1, b1;

            if (t < 0.2f)
            {
                float localT = t / 0.2f;
                r1 = localT * 0.118f;
                g1 = 0;
                b1 = localT * 0.235f;
            }
            else if (t < 0.4f)
            {
                float localT = (t - 0.2f) / 0.2f;
                r1 = 0.118f + localT * (0.471f - 0.118f);
                g1 = 0;
                b1 = 0.235f + localT * (0.471f - 0.235f);
            }
            else if (t < 0.6f)
            {
                float localT = (t - 0.4f) / 0.2f;
                r1 = 0.471f + localT * (0.863f - 0.471f);
                g1 = localT * 0.118f;
                b1 = 0.471f + localT * (0.314f - 0.471f);
            }
            else if (t < 0.8f)
            {
                float localT = (t - 0.6f) / 0.2f;
                r1 = 0.863f + localT * (1f - 0.863f);
                g1 = 0.118f + localT * (0.549f - 0.118f);
                b1 = 0.314f + localT * (0f - 0.314f);
            }
            else
            {
                float localT = (t - 0.8f) / 0.2f;
                r1 = 1f;
                g1 = 0.549f + localT * (1f - 0.549f);
                b1 = localT * 0.627f;
            }

            r = (byte)(r1 * 255);
            g = (byte)(g1 * 255);
            b = (byte)(b1 * 255);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GpuChartRenderer));
        }

        /// <summary>
        /// Releases the GPU resources used by this renderer.
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _device?.Dispose();
                _disposed = true;
            }
        }
    }
}
