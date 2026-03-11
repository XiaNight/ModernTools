using System;

namespace Base.Components.Chart
{
    /// <summary>
    /// Defines the GPU/CPU rendering strategy for chart data processing.
    /// Note: This is different from ChartRenderMode (Line/Dot/Combined).
    /// </summary>
    public enum RenderMode
    {
        /// <summary>
        /// Use CPU-based parallel rendering (Parallel.For)
        /// Best for small to medium datasets (< 5000 points)
        /// </summary>
        CPU,

        /// <summary>
        /// Use GPU compute shaders for data transformation and rendering
        /// Best for large datasets (> 5000 points) with high update frequency
        /// </summary>
        GPU,

        /// <summary>
        /// Automatically choose between CPU and GPU based on dataset size and update frequency.
        /// This is the recommended mode for most use cases.
        /// </summary>
        Adaptive
    }

    /// <summary>
    /// Strategy for choosing between CPU and GPU rendering based on workload characteristics.
    /// </summary>
    public static class AdaptiveRenderingStrategy
    {
        /// <summary>
        /// Default threshold for switching to GPU rendering.
        /// For datasets larger than this, GPU is preferred.
        /// </summary>
        public const int DEFAULT_GPU_THRESHOLD = 5000;

        /// <summary>
        /// Minimum update frequency (updates per second) where GPU becomes beneficial
        /// even for smaller datasets.
        /// </summary>
        public const int HIGH_FREQUENCY_THRESHOLD = 30;

        /// <summary>
        /// Determines the optimal render mode based on dataset characteristics.
        /// </summary>
        /// <param name="dataPointCount">Number of data points to render</param>
        /// <param name="updateFrequency">Estimated updates per second (0 if unknown)</param>
        /// <param name="requestedMode">User-requested render mode</param>
        /// <param name="isGpuAvailable">Whether a compatible GPU is available</param>
        /// <returns>The recommended render mode</returns>
        public static RenderMode SelectRenderMode(
            int dataPointCount,
            int updateFrequency,
            RenderMode requestedMode,
            bool isGpuAvailable)
        {
            // If GPU is explicitly requested but not available, fall back to CPU
            if (requestedMode == RenderMode.GPU && !isGpuAvailable)
            {
                return RenderMode.CPU;
            }

            // Honor explicit CPU/GPU requests when GPU is available
            if (requestedMode == RenderMode.CPU || 
                (requestedMode == RenderMode.GPU && isGpuAvailable))
            {
                return requestedMode;
            }

            // Adaptive mode: Choose based on workload
            if (requestedMode == RenderMode.Adaptive)
            {
                // GPU not available - use CPU
                if (!isGpuAvailable)
                {
                    return RenderMode.CPU;
                }

                // Large dataset - prefer GPU
                if (dataPointCount >= DEFAULT_GPU_THRESHOLD)
                {
                    return RenderMode.GPU;
                }

                // High frequency updates with moderate dataset - prefer GPU
                if (dataPointCount >= 1000 && updateFrequency >= HIGH_FREQUENCY_THRESHOLD)
                {
                    return RenderMode.GPU;
                }

                // Small dataset or low frequency - CPU is more efficient
                return RenderMode.CPU;
            }

            // Default fallback
            return isGpuAvailable && dataPointCount >= DEFAULT_GPU_THRESHOLD 
                ? RenderMode.GPU 
                : RenderMode.CPU;
        }

        /// <summary>
        /// Gets a human-readable explanation for why a particular render mode was chosen.
        /// </summary>
        public static string GetRenderModeReason(
            int dataPointCount,
            int updateFrequency,
            RenderMode selectedMode,
            bool isGpuAvailable)
        {
            if (!isGpuAvailable)
            {
                return "CPU mode: GPU not available";
            }

            if (selectedMode == RenderMode.GPU)
            {
                if (dataPointCount >= DEFAULT_GPU_THRESHOLD)
                {
                    return $"GPU mode: Large dataset ({dataPointCount:N0} points >= {DEFAULT_GPU_THRESHOLD:N0} threshold)";
                }
                if (updateFrequency >= HIGH_FREQUENCY_THRESHOLD)
                {
                    return $"GPU mode: High update frequency ({updateFrequency} Hz >= {HIGH_FREQUENCY_THRESHOLD} Hz threshold)";
                }
                return "GPU mode: Explicitly requested";
            }

            if (dataPointCount < 1000)
            {
                return $"CPU mode: Small dataset ({dataPointCount:N0} points) - GPU overhead not worthwhile";
            }

            return $"CPU mode: Moderate dataset ({dataPointCount:N0} points) with low update frequency";
        }
    }
}
