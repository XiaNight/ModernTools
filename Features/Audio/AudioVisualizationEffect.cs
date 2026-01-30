using Assets.WasapiAudio.Scripts.Core;

namespace Audio
{
    internal class AudioVisualizationEffect
    {
        private float[] _spectrumData;

        // Inspector Properties
        public event Action OnSpectrumDataUpdated;
        public int SpectrumSize = 1024;
        public ScalingStrategy ScalingStrategy = ScalingStrategy.Linear;
        public WindowFunctionType WindowFunctionType = WindowFunctionType.BlackmannHarris;
        public int MinFrequency = 20;
        public int MaxFrequency = 5000;

        protected bool IsIdle => _spectrumData?.All(v => v < 0.001f) ?? true;

        public AudioVisualizationEffect(WasapiAudio wasapiAudio)
        {
            var receiver = new SpectrumReceiver(SpectrumSize, ScalingStrategy, WindowFunctionType, MinFrequency,
                MaxFrequency, SetSpectrumData);

            wasapiAudio.AddReceiver(receiver);
        }

        private void SetSpectrumData(float[] spectrumData)
        {
            _spectrumData = spectrumData;
            OnSpectrumDataUpdated?.Invoke();
        }

        public float[] GetSpectrumData()
        {
            // Get raw / unmodified spectrum data
            var spectrumData = _spectrumData;

            return spectrumData;
        }

        /// <summary>
        /// Return a sample with seg as width and compress the input using non-linear compression
        /// </summary>
        /// <param name="seg">Output sample length</param>
        /// <param name="offset">multiply starter segment width, 10 is recommended</param>
        /// <returns>A array of compressed sample</returns>
        public float[] GetSegments()
        {
            float[] output = new float[SpectrumSize];

            double k = 0;
            for (int i = 1; i <= SpectrumSize; i++)
                k += 1.0 / i;  // harmonic number H_n

            double progress = 0;

            for (int i = 1; i < SpectrumSize + 1; i++)
            {
                int start = (int)Math.Floor(progress);

                double range = SpectrumSize / (i * k);
                progress += range;

                int end = (int)Math.Ceiling(progress);

                int count = 0;
                float sum = 0f;
                for (int j = start; j < end && j < SpectrumSize; j++)
                {
                    sum += _spectrumData[j];
                    count++;
                }

                sum /= count;
                output[i - 1] = sum;
            }

            return output;
        }

        public float[] Smooth(float[] input, int windowSize = 5)
        {
            int n = input.Length;
            float[] output = new float[n];
            int half = windowSize / 2;

            for (int i = 0; i < n; i++)
            {
                float sum = 0f;

                for (int j = -half; j <= half; j++)
                {
                    int idx = i + j;
                    if (idx >= 0 && idx < n)
                    {
                        sum += input[idx];
                    }
                }

                output[i] = sum / windowSize;
            }

            return output;
        }


        public float CalculateLoudness()
        {
            float loudness = 0f;

            for (int i = 0; i < SpectrumSize; i++)
            {
                loudness += _spectrumData[i];
            }
            return loudness / SpectrumSize;
        }
    }
}
