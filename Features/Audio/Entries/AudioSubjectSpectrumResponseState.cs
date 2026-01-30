using Audio.Util;

namespace Audio.Entries
{
    public partial class AudioSubject : IDisposable
    {
        internal class AudioSubjectSpectrumResponseState : State
        {
            private bool isCalculatingVolumeMean = false;

            private MeanCalculator[] leftMeanCalculator;
            private MeanCalculator[] rightMeanCalculator;

            public override void Enter()
            {
                if (subject.handler == null) return;

                int halfBin = subject.handler.HalfBins;
                leftMeanCalculator = new MeanCalculator[halfBin];
                rightMeanCalculator = new MeanCalculator[halfBin];

                for (int i = 0; i < halfBin; i++)
                {
                    leftMeanCalculator[i] = new MeanCalculator();
                    rightMeanCalculator[i] = new MeanCalculator();
                }
            }

            public override void Exit()
            {
                throw new NotImplementedException();
            }
        }
    }
}
