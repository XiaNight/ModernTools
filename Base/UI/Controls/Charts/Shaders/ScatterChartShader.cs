using System;
using ComputeSharp;

namespace Base.Components.Chart.Shaders
{
    /// <summary>
    /// GPU compute shader for transforming scatter/XY chart data points into pixel coordinates.
    /// This shader runs on the GPU to project data points into normalized screen space.
    /// </summary>
    [ThreadGroupSize(64, 1, 1)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct ScatterChartShader(ReadWriteBuffer<float> inputX, ReadWriteBuffer<float> inputY, ReadWriteBuffer<float> outputNormalizedX, ReadWriteBuffer<float> outputNormalizedY, float minX, float maxX, float minY, float maxY, int pointCount) : IComputeShader
    {
        /// <summary>
        /// Input: X coordinates
        /// </summary>
        public readonly ReadWriteBuffer<float> inputX = inputX;

        /// <summary>
        /// Input: Y coordinates
        /// </summary>
        public readonly ReadWriteBuffer<float> inputY = inputY;

        /// <summary>
        /// Output: Normalized X coordinates in [0, 1]
        /// </summary>
        public readonly ReadWriteBuffer<float> outputNormalizedX = outputNormalizedX;

        /// <summary>
        /// Output: Normalized Y coordinates in [0, 1] (inverted for screen space)
        /// </summary>
        public readonly ReadWriteBuffer<float> outputNormalizedY = outputNormalizedY;

        /// <summary>
        /// Minimum X value for scaling
        /// </summary>
        public readonly float minX = minX;

        /// <summary>
        /// Maximum X value for scaling
        /// </summary>
        public readonly float maxX = maxX;

        /// <summary>
        /// Minimum Y value for scaling
        /// </summary>
        public readonly float minY = minY;

        /// <summary>
        /// Maximum Y value for scaling
        /// </summary>
        public readonly float maxY = maxY;

        /// <summary>
        /// Total number of points to process
        /// </summary>
        public readonly int pointCount = pointCount;

        /// <summary>
        /// Execute shader for each thread (data point)
        /// </summary>
        public void Execute()
        {
            int i = ThreadIds.X;
            if (i >= pointCount)
                return;

            // Read input coordinates
            float x = inputX[i];
            float y = inputY[i];

            // Calculate ranges
            float xRange = maxX - minX;
            float yRange = maxY - minY;
            if (xRange <= 0.0f) xRange = 1e-6f;
            if (yRange <= 0.0f) yRange = 1e-6f;

            // Normalize to [0, 1] range
            float nx = (x - minX) / xRange;
            float ny = (y - minY) / yRange;

            // Clamp to valid range
            nx = Hlsl.Clamp(nx, 0.0f, 1.0f);
            ny = Hlsl.Clamp(ny, 0.0f, 1.0f);

            // Store results (Y inverted for screen coordinates)
            outputNormalizedX[i] = nx;
            outputNormalizedY[i] = 1.0f - ny;
        }
    }
}
