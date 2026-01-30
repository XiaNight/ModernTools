using static Audio.Entries.AudioSubject;

namespace Audio.Util
{
    public class SpectrumGaussianBlurRingBuffer : RingBuffer<TimedSpectrum>
    {
        private readonly int kernelSize;
        private readonly float[] kernel;
        private readonly int midOffset;
        private readonly int halfBinSize;
        private readonly TimedSpectrum spectrumBuffer;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="bufferSize"></param>
        /// <param name="blurSize"> Blur </param>
        /// <param name="sigma"></param>
        /// <param name="fill"></param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public SpectrumGaussianBlurRingBuffer(int bufferSize, int blurSize, int halfBinSize, float sigma = 1.0f, bool fill = false) : base(bufferSize, fill)
        {
            kernelSize = blurSize * 2 + 1;
            this.halfBinSize = halfBinSize;
            if (bufferSize < kernelSize) throw new ArgumentOutOfRangeException(nameof(blurSize), "buffer size must be >= blur * 2 + 1 size.");

            midOffset = blurSize;
            kernel = CreateGaussianKernel(kernelSize, sigma);
            spectrumBuffer = new TimedSpectrum()
            {
                leftSpectrum = new float[halfBinSize],
                rightSpectrum = new float[halfBinSize],
            };
        }

        public override bool TryDequeue(out TimedSpectrum result)
        {
            result = spectrumBuffer;
            if (Count < kernel.Length) return false;
            if(!base.TryPeek(out TimedSpectrum mid, midOffset)) return false;

            result.timestampTick = mid.timestampTick;
            ClearSpectrumBuffer();

            for (int i = 0; i < kernel.Length; i++)
            {
                float weight = kernel[i];
                if(!base.TryPeek(out TimedSpectrum value, i))
                {
                    result = default;
                    return false;
                }
                Apply(result, value, weight);
            }
            AdvanceTail(); // Remove the oldest item
            return true;
        }

        public bool TryPeekTime(out long timestampTick, int offset = 0)
        {
            if (Count < kernel.Length)
            {
                timestampTick = 0;
                return false;
            }
            if (!TryPeek(out TimedSpectrum mid, midOffset + offset))
            {
                timestampTick = 0;
                return false;
            }
            timestampTick = mid.timestampTick;
            return true;
        }

        public static void Apply(TimedSpectrum left, TimedSpectrum right, float scale)
        {
            int length = left.leftSpectrum.Length;
            if (length != right.leftSpectrum.Length || length != left.rightSpectrum.Length || length != right.rightSpectrum.Length)
            {
                throw new InvalidOperationException("Spectrum lengths do not match.");
            }

            for (int i = 0; i < length; i++)
            {
                left.leftSpectrum[i] += right.leftSpectrum[i] * scale;
                left.rightSpectrum[i] += right.rightSpectrum[i] * scale;
            }
        }

        public static void Mul(TimedSpectrum spectrum, float scalar)
        {
            int length = spectrum.leftSpectrum.Length;
            for (int i = 0; i < length; i++)
            {
                spectrum.leftSpectrum[i] *= scalar;
                spectrum.rightSpectrum[i] *= scalar;
            }
        }

        private void ClearSpectrumBuffer()
        {
            for (int i = 0; i < halfBinSize; i++)
            {
                spectrumBuffer.leftSpectrum[i] = 0f;
                spectrumBuffer.rightSpectrum[i] = 0f;
            }
        }

        private float[] CreateGaussianKernel(int size, float sigma = 1.0f)
        {
            float[] kernel = new float[size];
            float mean = size / 2f;
            float sum = 0f;
            for (int i = 0; i < size; i++)
            {
                kernel[i] = (float)Math.Exp(-0.5f * Math.Pow((i - mean) / sigma, 2));
                sum += kernel[i];
            }
            // Normalize the kernel
            for (int i = 0; i < size; i++)
            {
                kernel[i] /= sum;
            }
            return kernel;
        }
    }
}
