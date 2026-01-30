using System;
using ComputeSharp;

namespace Base.Components.Chart.Shaders
{
    /// <summary>
    /// GPU compute shader for transforming and projecting line chart data points.
    /// This shader runs on the GPU to transform raw data into pixel coordinates.
    /// </summary>
    [GeneratedComputeShaderDescriptor]
    [ThreadGroupSize(64, 1, 1)] // Added to fix CMPS0047
    public readonly partial struct LineChartShader(ReadWriteBuffer<float> inputData, ReadWriteBuffer<float> outputNormalizedY, float minY, float maxY, int dataLength) : IComputeShader
    {
        /// <summary>
        /// Input: Raw data values to be plotted
        /// </summary>
        public readonly ReadWriteBuffer<float> inputData = inputData;

        /// <summary>
        /// Output: Transformed Y coordinates in normalized space [0, 1]
        /// </summary>
        public readonly ReadWriteBuffer<float> outputNormalizedY = outputNormalizedY;

        /// <summary>
        /// Minimum Y value for scaling
        /// </summary>
        public readonly float minY = minY;

        /// <summary>
        /// Maximum Y value for scaling
        /// </summary>
        public readonly float maxY = maxY;

        /// <summary>
        /// Total number of data points to process
        /// </summary>
        public readonly int dataLength = dataLength;

        /// <summary>
        /// Execute shader for each thread (data point)
        /// </summary>
        public void Execute()
        {
            int i = ThreadIds.X;
            if (i >= dataLength)
                return;

            float value = inputData[i];

            // Normalize to [0, 1] range
            float yRange = maxY - minY;
            if (yRange <= 0.0f)
                yRange = 1e-6f;

            float normalizedY = (value - minY) / yRange;

            // Clamp to valid range
            normalizedY = Hlsl.Clamp(normalizedY, 0.0f, 1.0f);

            // Store result (inverted for screen coordinates)
            outputNormalizedY[i] = 1.0f - normalizedY;
        }
    }
}
