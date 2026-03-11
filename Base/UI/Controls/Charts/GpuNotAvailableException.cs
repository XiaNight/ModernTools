using System;

namespace Base.Components.Chart
{
    /// <summary>
    /// Exception thrown when GPU rendering is required but no compatible GPU device is available.
    /// </summary>
    public sealed class GpuNotAvailableException : InvalidOperationException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GpuNotAvailableException"/> class.
        /// </summary>
        public GpuNotAvailableException()
            : base("No compatible GPU device is available for chart rendering. A DirectX 12 compatible GPU is required.")
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GpuNotAvailableException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public GpuNotAvailableException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GpuNotAvailableException"/> class with a specified error message
        /// and a reference to the inner exception that is the cause of this exception.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The inner exception.</param>
        public GpuNotAvailableException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
