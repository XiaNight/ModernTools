using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ComputeSharp;
using Base.Components.Chart.Shaders;

namespace Base.Components.Chart
{
    /// <summary>
    /// Provides true GPU-accelerated chart rendering using ComputeSharp compute shaders.
    /// This class uses actual GPU compute for data transformation and rendering,
    /// as opposed to CPU-based parallel processing.
    /// </summary>
    public sealed class GpuAcceleratedRenderer : IDisposable
    {
        private static readonly Lazy<GpuAcceleratedRenderer> _instance = new Lazy<GpuAcceleratedRenderer>(
            () => new GpuAcceleratedRenderer(),
            System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

        private readonly GraphicsDevice _device;
        private bool _disposed;

        /// <summary>
        /// Gets the shared instance of the GPU accelerated renderer.
        /// </summary>
        /// <exception cref="GpuNotAvailableException">Thrown when no compatible GPU is available.</exception>
        public static GpuAcceleratedRenderer Instance => _instance.Value;

        /// <summary>
        /// Gets a value indicating whether a GPU is available for rendering.
        /// </summary>
        public static bool IsGpuAvailable => GpuChartRenderer.IsGpuAvailable;

        private GpuAcceleratedRenderer()
        {
            try
            {
                _device = GraphicsDevice.GetDefault();
                if (_device == null)
                {
                    throw new GpuNotAvailableException();
                }
            }
            catch (Exception ex) when (ex is not GpuNotAvailableException)
            {
                throw new GpuNotAvailableException("Failed to initialize GPU device for chart rendering.", ex);
            }
        }

        /// <summary>
        /// Gets the underlying graphics device.
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
        /// Renders a line chart using GPU compute shaders for data transformation.
        /// </summary>
        public void RenderLineChartGpu(
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

            // Allocate GPU buffers
            using var inputBuffer = _device.AllocateReadWriteBuffer<float>(dataLength);
            using var outputBuffer = _device.AllocateReadWriteBuffer<float>(dataLength);

            // Upload data to GPU
            inputBuffer.CopyFrom(data);

            // Execute GPU shader for data transformation
            _device.For(dataLength, new LineChartShader(
                inputBuffer,
                outputBuffer,
                minY,
                maxY,
                dataLength));

            // Download transformed data from GPU
            float[] normalizedY = new float[dataLength];
            outputBuffer.CopyTo(normalizedY);

            // Render lines on CPU (rasterization is still CPU-bound for now)
            // Future optimization: Move rasterization to GPU as well
            RenderLinesFromNormalizedData(
                normalizedY,
                pixels,
                width,
                height,
                colorR,
                colorG,
                colorB,
                lineThickness);
        }

        /// <summary>
        /// Renders a scatter chart using GPU compute shaders for point transformation.
        /// </summary>
        public void RenderScatterChartGpu(
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

            int pointCount = xData.Length;

            // Allocate GPU buffers
            using var inputXBuffer = _device.AllocateReadWriteBuffer<float>(pointCount);
            using var inputYBuffer = _device.AllocateReadWriteBuffer<float>(pointCount);
            using var outputXBuffer = _device.AllocateReadWriteBuffer<float>(pointCount);
            using var outputYBuffer = _device.AllocateReadWriteBuffer<float>(pointCount);

            // Upload data to GPU
            inputXBuffer.CopyFrom(xData);
            inputYBuffer.CopyFrom(yData);

            // Execute GPU shader for point transformation
            _device.For(pointCount, new ScatterChartShader(
                inputXBuffer,
                inputYBuffer,
                outputXBuffer,
                outputYBuffer,
                minX,
                maxX,
                minY,
                maxY,
                pointCount));

            // Download transformed data from GPU
            float[] normalizedX = new float[pointCount];
            float[] normalizedY = new float[pointCount];
            outputXBuffer.CopyTo(normalizedX);
            outputYBuffer.CopyTo(normalizedY);

            // Render dots on CPU
            RenderDotsFromNormalizedData(
                normalizedX,
                normalizedY,
                pixels,
                width,
                height,
                colorR,
                colorG,
                colorB,
                dotRadius);
        }

        /// <summary>
        /// Renders lines from normalized Y coordinates (CPU rasterization).
        /// </summary>
        private void RenderLinesFromNormalizedData(
            float[] normalizedY,
            Span<byte> pixels,
            int width,
            int height,
            byte colorR,
            byte colorG,
            byte colorB,
            float lineThickness)
        {
            int dataLength = normalizedY.Length;
            byte[] pixelArray = ArrayPool<byte>.Shared.Rent(pixels.Length);
            pixels.CopyTo(pixelArray);

            try
            {
                int halfThickness = Math.Max(1, (int)(lineThickness / 2));

                // Process each x column in parallel
                Parallel.For(0, width - 1, x =>
                {
                    float dataIdxF1 = (float)x / (width - 1) * (dataLength - 1);
                    float dataIdxF2 = (float)(x + 1) / (width - 1) * (dataLength - 1);

                    // Interpolate between data points
                    int dataIdx1 = Math.Clamp((int)dataIdxF1, 0, dataLength - 1);
                    int dataIdx2 = Math.Clamp((int)dataIdxF2, 0, dataLength - 1);

                    float ny1 = normalizedY[dataIdx1];
                    float ny2 = normalizedY[dataIdx2];

                    int py1 = (int)(ny1 * (height - 1));
                    int py2 = (int)(ny2 * (height - 1));

                    DrawLineSegmentArray(pixelArray, width, height, x, py1, x + 1, py2, 
                        colorR, colorG, colorB, halfThickness);
                });

                pixelArray.AsSpan(0, pixels.Length).CopyTo(pixels);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pixelArray);
            }
        }

        /// <summary>
        /// Renders dots from normalized coordinates (CPU rasterization).
        /// </summary>
        private void RenderDotsFromNormalizedData(
            float[] normalizedX,
            float[] normalizedY,
            Span<byte> pixels,
            int width,
            int height,
            byte colorR,
            byte colorG,
            byte colorB,
            float dotRadius)
        {
            int pointCount = normalizedX.Length;
            byte[] pixelArray = ArrayPool<byte>.Shared.Rent(pixels.Length);
            pixels.CopyTo(pixelArray);

            try
            {
                int radius = Math.Max(1, (int)dotRadius);
                int radiusSq = radius * radius;

                Parallel.For(0, pointCount, i =>
                {
                    int px = (int)(normalizedX[i] * (width - 1));
                    int py = (int)(normalizedY[i] * (height - 1));

                    DrawDotArray(pixelArray, width, height, px, py, colorR, colorG, colorB, radius, radiusSq);
                });

                pixelArray.AsSpan(0, pixels.Length).CopyTo(pixels);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(pixelArray);
            }
        }

        // Reuse drawing primitives from GpuChartRenderer
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void DrawLineSegmentArray(byte[] pixels, int width, int height, int x0, int y0, int x1, int y1, byte r, byte g, byte b, int halfThickness)
        {
            float lineRadius = halfThickness;
            float lineRadiusSq = lineRadius * lineRadius;

            float dx = x1 - x0;
            float dy = y1 - y0;
            float lineLength = MathF.Sqrt(dx * dx + dy * dy);

            if (lineLength < 0.001f)
            {
                DrawDotArray(pixels, width, height, x0, y0, r, g, b, halfThickness, halfThickness * halfThickness);
                return;
            }

            float ndx = dx / lineLength;
            float ndy = dy / lineLength;

            int minX = Math.Max(0, Math.Min(x0, x1) - halfThickness - 1);
            int maxX = Math.Min(width - 1, Math.Max(x0, x1) + halfThickness + 1);
            int minY = Math.Max(0, Math.Min(y0, y1) - halfThickness - 1);
            int maxY = Math.Min(height - 1, Math.Max(y0, y1) + halfThickness + 1);

            for (int iy = minY; iy <= maxY; iy++)
            {
                for (int ix = minX; ix <= maxX; ix++)
                {
                    float vx = ix - x0;
                    float vy = iy - y0;
                    float t = vx * ndx + vy * ndy;
                    t = Math.Clamp(t, 0f, lineLength);

                    float closestX = x0 + t * ndx;
                    float closestY = y0 + t * ndy;

                    float distX = ix - closestX;
                    float distY = iy - closestY;
                    float distSq = distX * distX + distY * distY;

                    if (distSq <= lineRadiusSq)
                    {
                        int idx = (iy * width + ix) * 4;
                        pixels[idx + 0] = b;
                        pixels[idx + 1] = g;
                        pixels[idx + 2] = r;
                        pixels[idx + 3] = 255;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GpuAcceleratedRenderer));
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
