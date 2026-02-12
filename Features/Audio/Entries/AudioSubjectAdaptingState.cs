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
                if(subject.handler == null) return;
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

                        leftUpper[i] = leftMeanCalculator[i].Max;
                        rightUpper[i] = rightMeanCalculator[i].Max;
                        leftLower[i] = leftMeanCalculator[i].Min;
                        rightLower[i] = rightMeanCalculator[i].Min;
                    }

                    subject.AudioDeviceEntry.Dispatcher.Invoke(() =>
                    {
                        subject.AudioDeviceEntry.LeftFFTUpperChart.SetData(leftUpper);
                        subject.AudioDeviceEntry.RightFFTUpperChart.SetData(rightUpper);
                        subject.AudioDeviceEntry.LeftFFTLowerChart.SetData(leftLower);
                        subject.AudioDeviceEntry.RightFFTLowerChart.SetData(rightLower);
                    });
                }
            }
        }
    }
}
