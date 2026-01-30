namespace Audio.Entries
{
    public partial class AudioSubject : IDisposable
    {
        internal class AudioSubjectAdaptingState : State
        {
            public override void Enter()
            {
                subject.spectrumTrigger.StartAdapting();
            }

            public override void Exit()
            {
                subject.spectrumTrigger.StartAdapting();
            }

            public override void SpectrumUpdate(TimedSpectrum spectrum)
            {
                base.SpectrumUpdate(spectrum);
                subject.spectrumTrigger.Adapt(spectrum.leftSpectrum, spectrum.rightSpectrum);
            }
        }
    }
}
