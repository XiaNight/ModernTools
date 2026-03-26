using Audio.Util;

namespace Audio.Entries
{
    public partial class AudioSubject : IDisposable
    {
        internal class AudioSubjectSpectrumResponseState : State
        {
            private bool isCalculating = false;

            private MeanCalculator[] leftMeanCalculator;
            private MeanCalculator[] rightMeanCalculator;

            private float[] leftMean;
            private float[] rightMean;

            public override void Enter()
            {
                if (subject.handler == null) return;

                int halfBin = subject.handler.HalfBins;
                leftMeanCalculator = new MeanCalculator[halfBin];
                rightMeanCalculator = new MeanCalculator[halfBin];

                leftMean = new float[halfBin];
                rightMean = new float[halfBin];

                for (int i = 0; i < halfBin; i++)
                {
                    leftMeanCalculator[i] = new MeanCalculator();
                    rightMeanCalculator[i] = new MeanCalculator();
                }
            }

            public override void Exit()
            {
                if(subject.handler == null) return;
                Array.Copy(leftMean, subject.volumeSpectrumOffsetLeft, leftMean.Length);
                Array.Copy(rightMean, subject.volumeSpectrumOffsetRight, rightMean.Length);
            }

            public void StartSpectrumResponseCalculation()
            {
                isCalculating = true;
            }

            public void StopSpectrumResponseCalculation()
            {
                isCalculating = false;
            }

            public override void SpectrumUpdate(TimedSpectrum spectrum)
            {
                base.SpectrumUpdate(spectrum);
                if (isCalculating)
                {
                    for(int i=0; i<subject.handler.HalfBins; i++)
                    {
                        leftMeanCalculator[i].Push(spectrum.leftSpectrum[i]);
                        rightMeanCalculator[i].Push(spectrum.rightSpectrum[i]);

                        leftMean[i] = leftMeanCalculator[i].Mean;
                        rightMean[i] = rightMeanCalculator[i].Mean;
                    }

                    subject.AudioDeviceEntry.Dispatcher.Invoke(() =>
                    {
                        subject.AudioDeviceEntry.LeftFFTUpperChart.SetData(leftMean);
                        subject.AudioDeviceEntry.RightFFTUpperChart.SetData(rightMean);
                    });
                }
            }
        }
    }
} 
