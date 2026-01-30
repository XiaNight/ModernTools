using Audio.Receiver;
using Audio.Trigger;
using Audio.Util;

namespace Audio.Entries
{
    public partial class AudioSubject : IDisposable
    {
        internal class AudioSubjectTimeAlignmentState : State
        {
            private bool isWaitingForTrigger = false;
            public bool isTriggered = false;
            private readonly VolumeTrigger volumeTrigger = new();

            private bool isCalculatingNoiseMean = false;
            private readonly MeanCalculator leftNoiseMeanCalculator = new();
            private readonly MeanCalculator rightNoiseMeanCalculator = new();

            private bool isCalculatingVolumeMean = false;
            private readonly MeanCalculator leftMeanCalculator = new();
            private readonly MeanCalculator rightMeanCalculator = new();

            public float LeftVolumeOffset => leftMeanCalculator.Mean;
            public float RightVolumeOffset => rightMeanCalculator.Mean;

            public override void Enter()
            {
                subject.AudioDeviceEntry.Dispatcher.Invoke(() =>
                {
                    subject.AudioDeviceEntry.TestText.Visibility = System.Windows.Visibility.Visible;
                });
                volumeTrigger.triggerType = VolumeTrigger.TriggerType.UpperOnly;
            }

            public override void Exit()
            {
                volumeTrigger.OnVolumeTriggered -= VolumeTriggered;
            }


            public override void VolumeUpdate(List<AudioChannelHandler.TimedValue<float>> volumes)
            {
                base.VolumeUpdate(volumes);
                if (isCalculatingNoiseMean)
                {
                    foreach (var volume in volumes)
                    {
                        leftNoiseMeanCalculator.Push(volume.Left);
                        rightNoiseMeanCalculator.Push(volume.Right);
                    }
                }
                if (isCalculatingVolumeMean)
                {
                    foreach (var volume in volumes)
                    {
                        leftMeanCalculator.Push(volume.Left);
                        rightMeanCalculator.Push(volume.Right);
                    }
                }
                if (isWaitingForTrigger)
                {
                    volumeTrigger.Parse(volumes);
                }
            }
            public void StartNoiseCalculation()
            {
                isCalculatingNoiseMean = true;
                subject.AudioDeviceEntry.Dispatcher.Invoke(() =>
                {
                    subject.AudioDeviceEntry.TestText.Text = $"Calculating...";
                });
            }

            public void StopNoiseCalculation()
            {
                isCalculatingNoiseMean = false;
                subject.AudioDeviceEntry.Dispatcher.Invoke(() =>
                {
                    subject.AudioDeviceEntry.TestText.Text = $"Noise Calculated {leftNoiseMeanCalculator.Mean:E2}, {rightNoiseMeanCalculator.Mean:E2}";
                });
            }

            public void StartVolumeCalculation()
            {
                isCalculatingVolumeMean = true;
                subject.AudioDeviceEntry.Dispatcher.Invoke(() =>
                {
                    subject.AudioDeviceEntry.TestText.Text = $"Calculating Volume Mean...";
                });
            }

            public void StopVolumeCalculation()
            {
                isCalculatingVolumeMean = false;
                subject.AudioDeviceEntry.Dispatcher.Invoke(() =>
                {
                    subject.AudioDeviceEntry.TestText.Text = $"Volume Mean Calculated {leftMeanCalculator.Mean:E2}, {rightMeanCalculator.Mean:E2}";
                });

                subject.volumeOffsetLeft = leftMeanCalculator.Mean;
                subject.volumeOffsetRight = rightMeanCalculator.Mean;
            }

            public void SetupVolumeTrigger(float stfFactor)
            {
                float leftMean = leftNoiseMeanCalculator.Mean;
                float leftMax = leftNoiseMeanCalculator.Max;
                float leftMin = leftNoiseMeanCalculator.Min;
                volumeTrigger.leftUpperThreshold = leftMean + stfFactor * (leftMax - leftMean);
                volumeTrigger.leftLowerThreshold = leftMean - stfFactor * (leftMean - leftMin);

                float rightMean = rightNoiseMeanCalculator.Mean;
                float rightMax = rightNoiseMeanCalculator.Max;
                float rightMin = rightNoiseMeanCalculator.Min;
                volumeTrigger.rightUpperThreshold = rightMean + stfFactor * (rightMax - rightMean);
                volumeTrigger.rightLowerThreshold = rightMean - stfFactor * (rightMean - rightMin);

                isTriggered = false;
                isWaitingForTrigger = true;

                volumeTrigger.OnVolumeTriggered += VolumeTriggered;
            }

            private void VolumeTriggered(long timestamp)
            {
                volumeTrigger.OnVolumeTriggered -= VolumeTriggered;

                isTriggered = true;
                subject.AlignmentTimestamp = timestamp;

                subject.AudioDeviceEntry.Dispatcher.Invoke(() =>
                {
                    subject.AudioDeviceEntry.TestText.Text = $"Triggered at {new DateTime(timestamp):HH:mm:ss.fff}";
                });
            }
        }
    }
}
