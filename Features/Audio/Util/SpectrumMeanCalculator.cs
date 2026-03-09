using static Audio.Entries.AudioSubject;

namespace Audio.Util
{
    internal class SpectrumMeanCalculator
    {
        private readonly int halfBin;

        private readonly MeanCalculator[] leftMeanCalculator;
        private readonly MeanCalculator[] rightMeanCalculator;

        public float[] LeftMeans { get; private set; }
        public float[] RightMeans { get; private set; }

        public SpectrumMeanCalculator(int halfBin)
        {
            this.halfBin = halfBin;

            leftMeanCalculator = new MeanCalculator[halfBin];
            rightMeanCalculator = new MeanCalculator[halfBin];

            LeftMeans = new float[halfBin];
            RightMeans = new float[halfBin];

            for (int i = 0; i < halfBin; i++)
            {
                leftMeanCalculator[i] = new MeanCalculator();
                rightMeanCalculator[i] = new MeanCalculator();
            }
        }

        public void Push(TimedSpectrum spectrum)
        {
            for (int i = 0; i < halfBin; i++)
            {
                leftMeanCalculator[i].Push(spectrum.leftSpectrum[i]);
                rightMeanCalculator[i].Push(spectrum.rightSpectrum[i]);

                LeftMeans[i] = leftMeanCalculator[i].Mean;
                RightMeans[i] = rightMeanCalculator[i].Mean;
            }
        }
    }
}
