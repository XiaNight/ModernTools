using Audio.Generator;
using Audio.Receiver;
using Audio.Trigger;
using Audio.Util;
using Base.Components.Chart;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Windows.Controls;
using TimedFloat = Audio.Receiver.AudioChannelHandler.TimedValue<float>;

namespace Audio.Entries
{
    public partial class AudioSubject : IDisposable
    {
        public AudioGenerator AudioGenerator { get; private set; }
        public AudioDeviceEntry AudioDeviceEntry { get; private set; }
        private AudioChannelHandler handler;
        private bool isStreaming = false;
        private bool isTriggered = false;
        private readonly int frameSize;
        private readonly int fftLength;
        public long AlignmentTimestamp { get; private set; }

        private readonly SpectrumTrigger spectrumTrigger;

        //- Spectrum Ring Buffer
        private const int MaxSpectrumLength = 256;
        private SpectrumGaussianBlurRingBuffer spectrumRing;

        //- Compare Device
        private AudioSubject comparingSubject;
        public delegate IEnumerable<AudioSubject> CompareableDeviceRequestDelegate();
        public CompareableDeviceRequestDelegate RequestComparableDevice;
        public Action<MMDevice> onSourceSelected;
        private IEnumerable<AudioSubject> audioSubjects;
        private long offsetTime = 0;
        private float volumeOffsetLeft = 1;
        private float volumeOffsetRight = 1;
        private float manualVolumeOffset = 1;
        private Task comparingTask;
        private CancellationTokenSource comparingCancellationTokenSource;
        private bool showComparison = true;

        // compare ring buffer
        private SpectrumGaussianBlurRingBuffer compareRing;

        // difference ring buffer
        private readonly RingBuffer<TimedSpectrum> differenceRing = new(MaxSpectrumLength, true);

        public event Action<TimedSpectrum> OnSpectrumUpdated;

        private State currentState;
        public MMDevice SelectedSource { get; private set; }

        public AudioSubject(int frameSize, int fftLength, long debounceDuration)
        {
            this.frameSize = frameSize;
            this.fftLength = fftLength;
            spectrumTrigger = new SpectrumTrigger(fftLength / 2, 5);
            spectrumTrigger.OnTriggered += () =>
            {
                string name = AudioDeviceEntry.SourceDeviceDropdown.SelectedItem is MenuItem item && item.Tag is MMDevice device
                    ? device.FriendlyName
                    : "Unknown Device";
                isTriggered = true;
                Base.Services.Debug.Log($"Volume Triggered at {DateTime.Now:HH:mm:ss.fff} for device: {name}");
            };

            AudioDeviceEntry = new();
            handler = null;

            AudioDeviceEntry.OnSourceDeviceSelected += SourceDeviceSelected;
            AudioDeviceEntry.OnCompareDeviceSelected += CompareDeviceSelected;
            AudioDeviceEntry.OnCompareableDeviceRequest = OnComparableDeviceRequest;
            AudioDeviceEntry.OnShowComparisonToggled += (show) =>
            {
                showComparison = show;
            };

            AudioDeviceEntry.OnOffsetChanged += (offset) => offsetTime = offset;
            AudioDeviceEntry.OnVolumeOffsetChanged += (magnitude) => manualVolumeOffset = (float)magnitude;
        }

        public void SourceDeviceSelected(MMDevice device)
        {
            SelectedSource = device;
            if (handler != null)
            {
                handler.SpectrumFrameAvailable -= OnSpectrumFrame;
                handler.VolumeAvailable -= OnVolumeFrame;

                handler.Dispose();
            }

            AudioGenerator?.Dispose();
            if (device.DataFlow == DataFlow.Render)
            {
                AudioGenerator = new AudioGenerator(device, 44100, 440.0);
                AudioGenerator.Start();
                AudioGenerator.Pause();
            }

            var capture = device.DataFlow switch
            {
                DataFlow.Render => new WasapiLoopbackCapture(device),
                DataFlow.Capture => new WasapiCapture(device),
                _ => throw new InvalidOperationException("Unsupported device data flow."),
            };
            handler = new AudioChannelHandler(capture, fftLength, frameSize, device.FriendlyName);
            handler.SpectrumFrameAvailable += OnSpectrumFrame;
            handler.VolumeAvailable += OnVolumeFrame;

            AudioDeviceEntry.SampleRateText.Text = $"Sample Rate: {handler.SampleRate} Hz";
            AudioDeviceEntry.SampleRateText.Visibility = System.Windows.Visibility.Visible;

            spectrumRing = new(MaxSpectrumLength, 7, handler.HalfBins, sigma: 3, fill: true);
            compareRing = new(MaxSpectrumLength, 7, handler.HalfBins, sigma: 3, fill: true);
            differenceRing.Fill((ref TimedSpectrum value) =>
            {
                value.leftSpectrum = new float[handler.HalfBins];
                value.rightSpectrum = new float[handler.HalfBins];
                value.timestampTick = 0;
            });

            if (isStreaming)
            {
                handler.StartAudioStream();
            }
            onSourceSelected?.Invoke(device);
        }

        private IEnumerable<MMDevice> OnComparableDeviceRequest()
        {
            audioSubjects = RequestComparableDevice() ?? [];
            return audioSubjects.Select((V) => V.SelectedSource);
        }

        private void CompareDeviceSelected(MMDevice device)
        {
            if (comparingSubject != null)
            {
                comparingSubject.OnSpectrumUpdated -= CompareDeviceSpectrumUpdated;
            }

            bool isComparingTaskRunning = comparingTask != null && !comparingTask.IsCompleted;
            if (isComparingTaskRunning)
            {
                comparingCancellationTokenSource.Cancel();
            }

            comparingSubject = null;
            foreach (var subjects in audioSubjects)
            {
                if (subjects.SelectedSource.ID == device.ID)
                {
                    comparingSubject = subjects;
                    comparingSubject.OnSpectrumUpdated += CompareDeviceSpectrumUpdated;
                    break;
                }
            }

            YScaleMode scaleMode = comparingSubject == null ? YScaleMode.Log10 : YScaleMode.Linear;
            AudioDeviceEntry.LeftFFTChart.ScaleMode = scaleMode;
            AudioDeviceEntry.RightFFTChart.ScaleMode = scaleMode;

            double minY = comparingSubject == null ? -60 : -60;
            double maxY = comparingSubject == null ? 0 : 60;
            AudioDeviceEntry.RightFFTChart.MinY = minY;
            AudioDeviceEntry.RightFFTChart.MaxY = maxY;
            AudioDeviceEntry.LeftFFTChart.MinY = minY;
            AudioDeviceEntry.LeftFFTChart.MaxY = maxY;

            offsetTime = AlignmentTimestamp - comparingSubject?.AlignmentTimestamp ?? 0;
            AudioDeviceEntry.SetOffset(offsetTime);

            if (comparingSubject == null) return;

            comparingCancellationTokenSource = new CancellationTokenSource();
            var token = comparingCancellationTokenSource.Token;
            comparingTask = Task.Run(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    if (spectrumRing.TryDequeue(out TimedSpectrum current))
                    {
                        bool parsed;
                        do
                        {
                            parsed = SpectrumComparison(current);

                        } while (parsed);
                    }
                    Thread.Yield();
                }
            }, token);
        }

        private void CompareDeviceSpectrumUpdated(TimedSpectrum spectrum)
        {
            compareRing.Enqueue(spectrum);
        }

        private bool SpectrumComparison(TimedSpectrum currentSpectrum)
        {
            bool hasPeek = compareRing.TryPeekTime(out long peekTime);
            bool hasNext = compareRing.TryPeekTime(out long nextTime, 1);

            if (!hasPeek || !hasNext) return false;
            long currentOffsetTime = currentSpectrum.timestampTick - offsetTime;

            // peek < next < current
            // current time is greater than the comparing point, find next compare point
            while (nextTime <= currentOffsetTime)
            {
                compareRing.AdvanceTail();

                hasPeek = compareRing.TryPeekTime(out peekTime);
                hasNext = compareRing.TryPeekTime(out nextTime, 1);

                if (!hasNext) return false;
            }

            // peek < current < next
            if (peekTime <= currentOffsetTime && currentOffsetTime < nextTime)
            {
                if (!compareRing.TryDequeue(out TimedSpectrum peek))
                {
                    return false;
                }

                differenceRing.EnqueueValue((ref TimedSpectrum value) =>
                {
                    float leftVolumeDifference = AudioChannelHandler.DbToSqrRms(volumeOffsetLeft - comparingSubject.volumeOffsetLeft);
                    float rightVolumeDifference = AudioChannelHandler.DbToSqrRms(volumeOffsetRight - comparingSubject.volumeOffsetRight);

                    for (int i = 0; i < handler.HalfBins; i++)
                    {
                        value.leftSpectrum[i] = LogorithmicCompare(
                            currentSpectrum.leftSpectrum[i],
                            peek.leftSpectrum[i], leftVolumeDifference * manualVolumeOffset
                        );
                        value.rightSpectrum[i] = LogorithmicCompare(
                            currentSpectrum.rightSpectrum[i],
                            peek.rightSpectrum[i], rightVolumeDifference * manualVolumeOffset
                        );
                    }
                    value.timestampTick = currentSpectrum.timestampTick;
                });

                return true;
            }

            return false;
        }

        private static float Compare(float a, float b)
        {
            return MathF.Abs(a - b);
        }

        /// <summary>
        /// Logorithmic Compare
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        private static float LogorithmicCompare(float a, float b, float offsetB)
        {
            if (a == 0 && b == 0)
            {
                return 0;
            }
            else if (a == 0 || b == 0)
            {
                return 0;
            }

            float logA = MathF.Max(MathF.Log10(a), -80);
            float logB = MathF.Max(MathF.Log10(b), -80);
            float logMax = Math.Max(logA, logB);
            //float noiseFix = logMax - 4;
            //noiseFix /= noiseFix + 1;

            return 10 * (logA - logB - offsetB);// * noiseFix;
        }

        public class TimedSpectrum : IDisposable
        {
            public long timestampTick;
            public float[] leftSpectrum;
            public float[] rightSpectrum;

            public void Dispose()
            {
                leftSpectrum = null;
                rightSpectrum = null;
            }
        }

        private void OnSpectrumFrame(long timestampTick)
        {
            TimedSpectrum enqueued = spectrumRing.EnqueueValue((ref TimedSpectrum spectrum) =>
            {
                spectrum.timestampTick = timestampTick;
                spectrum.leftSpectrum = new float[handler.HalfBins];
                spectrum.rightSpectrum = new float[handler.HalfBins];
                Array.Copy(handler.spectrumL, spectrum.leftSpectrum, handler.spectrumL.Length);
                Array.Copy(handler.spectrumR, spectrum.rightSpectrum, handler.spectrumR.Length);
            });

            OnSpectrumUpdated?.Invoke(enqueued);
        }

        private void OnVolumeFrame(List<TimedFloat> volumes)
        {
            currentState?.VolumeUpdate(volumes);

            float maxLeftVolume = float.MinValue;
            float maxRightVolume = float.MinValue;

            foreach (var volume in volumes)
            {
                if (volume.Left > maxLeftVolume)
                {
                    maxLeftVolume = volume.Left;
                }
                if (volume.Right > maxRightVolume)
                {
                    maxRightVolume = volume.Right;
                }
            }
            AudioDeviceEntry.LeftVolumeMeter.SetLevelDb(maxLeftVolume);
            AudioDeviceEntry.RightVolumeMeter.SetLevelDb(maxRightVolume);
        }

        public void UpdateSpectrogram()
        {
            TimedSpectrum spectrum;
            if (comparingSubject != null && showComparison)
            {
                if (differenceRing.TryDequeue(out spectrum))
                {
                    BlitSpectrum(spectrum);
                    AudioDeviceEntry.RightFFTChart.SetData(spectrum.rightSpectrum);
                    AudioDeviceEntry.LeftFFTChart.SetData(spectrum.leftSpectrum);
                }
                while (differenceRing.Count > differenceRing.Capacity / 2)
                {
                    differenceRing.AdvanceTail();
                }
            }
            else if (handler != null)
            {
                if (comparingSubject == null)
                {
                    if (spectrumRing.TryDequeue(out spectrum))
                    {
                        BlitSpectrum(spectrum);
                        AudioDeviceEntry.RightFFTChart.SetData(spectrum.rightSpectrum);
                        AudioDeviceEntry.LeftFFTChart.SetData(spectrum.leftSpectrum);
                    }
                }
                else
                {
                    if (spectrumRing.TryPeek(out spectrum))
                    {
                        BlitSpectrum(spectrum);
                        AudioDeviceEntry.RightFFTChart.SetData(spectrum.rightSpectrum);
                        AudioDeviceEntry.LeftFFTChart.SetData(spectrum.leftSpectrum);
                    }
                }
                while (spectrumRing.Count > differenceRing.Capacity / 2)
                {
                    spectrumRing.AdvanceTail();
                }
            }
        }

        private void BlitSpectrum(in TimedSpectrum spectrum)
        {
            AudioDeviceEntry.RightSpectrogram.AddSpectrum(spectrum.rightSpectrum, spectrum.timestampTick);
            AudioDeviceEntry.LeftSpectrogram.AddSpectrum(spectrum.leftSpectrum, spectrum.timestampTick);
        }

        private int BlitSpectrum(TimedSpectrum[] targetRing, in int targetPointer, int targetProcessOffset)
        {
            bool isFFTSet = false;
            while (targetProcessOffset < 0)
            {
                int index = GetOffset(targetPointer, targetProcessOffset, MaxSpectrumLength);
                var spectrum = targetRing[index];
                AudioDeviceEntry.RightSpectrogram.AddSpectrum(spectrum.rightSpectrum, spectrum.timestampTick);
                AudioDeviceEntry.LeftSpectrogram.AddSpectrum(spectrum.leftSpectrum, spectrum.timestampTick);

                targetProcessOffset++;

                if (isFFTSet) continue;
                AudioDeviceEntry.RightFFTChart.SetData(spectrum.rightSpectrum);
                AudioDeviceEntry.LeftFFTChart.SetData(spectrum.leftSpectrum);

                isFFTSet = true;
            }
            return targetProcessOffset;
        }

        public void SetTriggerThreshold(float value)
        {
            spectrumTrigger.AdaptationStdFactor = value;
        }

        public void ShowTriggerThresholds()
        {
            spectrumTrigger.CalculateThresholds();
            AudioDeviceEntry.LeftFFTLowerChart.SetData(spectrumTrigger.leftLowerThresholds);
            AudioDeviceEntry.LeftFFTUpperChart.SetData(spectrumTrigger.leftUpperThresholds);
            AudioDeviceEntry.RightFFTLowerChart.SetData(spectrumTrigger.rightLowerThresholds);
            AudioDeviceEntry.RightFFTUpperChart.SetData(spectrumTrigger.rightUpperThresholds);
        }

        public void StartAudioStream()
        {
            if (isStreaming) return;
            isStreaming = true;
            handler?.StartAudioStream();
        }

        public void StopAudioStream()
        {
            if (!isStreaming) return;
            isStreaming = false;

            if (handler == null) return;
            handler?.StopAudioStream();
        }

        public void Dispose()
        {
            comparingCancellationTokenSource?.Cancel();

            handler?.Dispose();
        }

        public T SetState<T>() where T : State, new()
        {
            if (typeof(T) == currentState?.GetType())
            {
                return (T)currentState;
            }
            currentState?.Exit();
            currentState?.Dispose();
            T newState = new();
            newState.SetSubject(this);
            newState.Enter();
            currentState = newState;
            return newState;
        }

        public bool TryGetState<T>(out T t) where T : State
        {
            t = currentState as T;
            return t != null;
        }

        public void ExitState()
        {
            currentState.Exit();
            currentState = null;
        }

        public static int GetOffset(in int pointer, in int offset, in int maxLength)
        {
            int value = pointer + offset;
            while (value < 0)
            {
                value += maxLength;
            }
            return value & (maxLength - 1);
        }

        public static void Increment(ref int pointer, in int offset, in int maxLength)
        {
            pointer = (pointer + offset) & (maxLength - 1);
        }

        public static void Increment(ref int pointer, in int maxLength)
        {
            pointer = (pointer + 1) & (maxLength - 1);
        }

        public abstract class State : IDisposable
        {
            protected AudioSubject subject;
            public void SetSubject(AudioSubject subject)
            {
                this.subject = subject;
            }
            public abstract void Enter();
            public abstract void Exit();
            public virtual void SpectrumUpdate(TimedSpectrum spectrum) { }
            public virtual void VolumeUpdate(List<TimedFloat> volumes) { }

            public virtual void Dispose()
            {
                subject = null;
            }
        }
    }
}
