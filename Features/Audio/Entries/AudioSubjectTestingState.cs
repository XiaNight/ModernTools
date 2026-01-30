namespace Audio.Entries
{
    public partial class AudioSubject : IDisposable
    {
        internal class AudioSubjectTestingState : State
        {
            public int failCount;
            public int ranCycles;
            public override void Enter()
            {
                failCount = 0;
                ranCycles = 0;
                subject.AudioDeviceEntry.TestText.Visibility = System.Windows.Visibility.Collapsed;
                subject.AudioDeviceEntry.SourceDeviceDropdown.IsEnabled = true;
                subject.handler?.StartRecorder();
            }
            public override void Exit()
            {
                subject.AudioDeviceEntry.TestText.Text = $"Test Finished. Failed Cycles: {failCount}/{ranCycles}";
                subject.handler?.StopRecorder();
            }
            public void TestTick()
            {
                ranCycles++;
                subject.AudioDeviceEntry.TestText.Visibility = System.Windows.Visibility.Visible;
                subject.AudioDeviceEntry.SourceDeviceDropdown.IsEnabled = false;

                if (subject.isTriggered || subject.spectrumTrigger.isLeftTriggered || subject.spectrumTrigger.isRightTriggered)
                {
                    failCount++;
                }
                subject.isTriggered = false;
                subject.AudioDeviceEntry.TestText.Text = $"Fails: {failCount}";
            }

            public override void SpectrumUpdate(TimedSpectrum spectrum)
            {
                base.SpectrumUpdate(spectrum);
                subject.spectrumTrigger.Parse(spectrum.leftSpectrum, spectrum.rightSpectrum);
            }
        }
    }
}
