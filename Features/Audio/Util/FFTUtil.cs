using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Audio.Util
{
    internal class FFTUtil
    {
        public static int FrequencyToFftIndex(double frequencyHz, double sampleRateHz, int fftLength = 4096, bool oneSided = true)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fftLength);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRateHz);
            ArgumentOutOfRangeException.ThrowIfNegative(frequencyHz);

            int maxIndex = oneSided ? (fftLength / 2) : (fftLength - 1);
            int index = (int)Math.Round(frequencyHz * fftLength / sampleRateHz, MidpointRounding.AwayFromZero);

            if (index < 0) index = 0;
            if (index > maxIndex) index = maxIndex;

            return index;
        }
    }
}
