using Audio.Util;

namespace Audio.Entries
{
    public partial class AudioSubject : IDisposable
    {
        internal class AudioSubjectAdaptingState : State
        {
            private bool isCalculating = false;

            private MeanCalculator[] leftMeanCalculator;
            private MeanCalculator[] rightMeanCalculator;

            private float[] leftUpper;
            private float[] rightUpper;
            private float[] leftLower;
            private float[] rightLower;

            public override void Enter()
            {
                if (subject.handler == null) return;

                int halfBin = subject.handler.HalfBins;
                leftMeanCalculator = new MeanCalculator[halfBin];
                rightMeanCalculator = new MeanCalculator[halfBin];

                leftUpper = new float[halfBin];
                rightUpper = new float[halfBin];
                leftLower = new float[halfBin];
                rightLower = new float[halfBin];

                for (int i = 0; i < halfBin; i++)
                {
                    leftMeanCalculator[i] = new MeanCalculator();
                    rightMeanCalculator[i] = new MeanCalculator();
                }
            }

            public override void Exit()
            {
                if (subject.handler == null) return;
                StopSpectrumResponseCalculation();
            }

            public void StartSpectrumResponseCalculation()
            {
                isCalculating = true;
            }

            public void StopSpectrumResponseCalculation()
            {
                isCalculating = false;

                MinBlurClamp(leftLower, subject.spectrumTrigger.leftLowerThresholds, 9);
                MinBlurClamp(rightLower, subject.spectrumTrigger.rightLowerThresholds, 9);
                MaxBlurClamp(leftUpper, subject.spectrumTrigger.leftUpperThresholds, 9);
                MaxBlurClamp(rightUpper, subject.spectrumTrigger.rightUpperThresholds, 9);

                subject.AudioDeviceEntry.Dispatcher.Invoke(() =>
                {
                    subject.AudioDeviceEntry.LeftFFTUpperChart.SetData(subject.spectrumTrigger.leftUpperThresholds);
                    subject.AudioDeviceEntry.RightFFTUpperChart.SetData(subject.spectrumTrigger.rightUpperThresholds);
                    subject.AudioDeviceEntry.LeftFFTLowerChart.SetData(subject.spectrumTrigger.leftLowerThresholds);
                    subject.AudioDeviceEntry.RightFFTLowerChart.SetData(subject.spectrumTrigger.rightLowerThresholds);
                });
            }

            public static void MaxBlurClamp(float[] src, float[] dst, int radius)
            {
                if (src == null || dst == null) throw new ArgumentNullException(src == null ? nameof(src) : nameof(dst));
                if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
                int n = Math.Min(src.Length, dst.Length);
                if (n == 0) return;

                int streamLen = n + 2 * radius;
                int[] dqPos = new int[streamLen];   // deque of stream positions
                int head = 0, tail = 0;

                int ClampIndex(int streamPos)
                {
                    int i = streamPos - radius;
                    if (i < 0) return 0;
                    if (i >= n) return n - 1;
                    return i;
                }

                // Initialize with first window in the stream [0 .. 2r]
                for (int p = 0; p <= 2 * radius; p++)
                {
                    int idx = ClampIndex(p);
                    while (tail > head && src[ClampIndex(dqPos[tail - 1])] <= src[idx]) tail--;
                    dqPos[tail++] = p;
                }

                for (int j = 0; j < n; j++)
                {
                    int centerStreamPos = j + radius;
                    int windowStart = centerStreamPos - radius;
                    int windowEnd = centerStreamPos + radius;

                    // Expire positions left of window
                    while (tail > head && dqPos[head] < windowStart) head++;

                    // Current max
                    dst[j] = src[ClampIndex(dqPos[head])];

                    // Push next position (advance windowEnd by 1 for next j)
                    int nextPos = windowEnd + 1;
                    if (nextPos < streamLen)
                    {
                        int nextIdx = ClampIndex(nextPos);
                        while (tail > head && src[ClampIndex(dqPos[tail - 1])] <= src[nextIdx]) tail--;
                        dqPos[tail++] = nextPos;
                    }
                }
            }

            public static void MinBlurClamp(float[] src, float[] dst, int radius)
            {
                if (src == null || dst == null) throw new ArgumentNullException(src == null ? nameof(src) : nameof(dst));
                if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
                int n = Math.Min(src.Length, dst.Length);
                if (n == 0) return;

                int streamLen = n + 2 * radius;
                int[] dqPos = new int[streamLen]; // deque of stream positions
                int head = 0, tail = 0;

                int ClampIndex(int streamPos)
                {
                    int i = streamPos - radius;
                    if (i < 0) return 0;
                    if (i >= n) return n - 1;
                    return i;
                }

                // Initialize first window in stream [0 .. 2r]
                for (int p = 0; p <= 2 * radius; p++)
                {
                    int idx = ClampIndex(p);
                    while (tail > head && src[ClampIndex(dqPos[tail - 1])] >= src[idx]) tail--;
                    dqPos[tail++] = p;
                }

                for (int j = 0; j < n; j++)
                {
                    int centerPos = j + radius;
                    int windowStart = centerPos - radius;
                    int windowEnd = centerPos + radius;

                    // Expire left-of-window positions
                    while (tail > head && dqPos[head] < windowStart) head++;

                    // Current min
                    dst[j] = src[ClampIndex(dqPos[head])];

                    // Push next stream position for next j
                    int nextPos = windowEnd + 1;
                    if (nextPos < streamLen)
                    {
                        int nextIdx = ClampIndex(nextPos);
                        while (tail > head && src[ClampIndex(dqPos[tail - 1])] >= src[nextIdx]) tail--;
                        dqPos[tail++] = nextPos;
                    }
                }
            }

            public override void SpectrumUpdate(TimedSpectrum spectrum)
            {
                base.SpectrumUpdate(spectrum);
                if (isCalculating)
                {
                    for (int i = 0; i < subject.handler.HalfBins; i++)
                    {
                        leftMeanCalculator[i].Push(spectrum.leftSpectrum[i]);
                        rightMeanCalculator[i].Push(spectrum.rightSpectrum[i]);

                        leftUpper[i] = leftMeanCalculator[i].Max;
                        rightUpper[i] = rightMeanCalculator[i].Max;
                        leftLower[i] = leftMeanCalculator[i].Min;
                        rightLower[i] = rightMeanCalculator[i].Min;
                    }
                }
            }
        }
    }
}
