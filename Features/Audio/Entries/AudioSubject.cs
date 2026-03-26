using Audio.Generator;
using Audio.Receiver;
using Audio.Trigger;
using Audio.Util;
using Base.Components.Chart;
using NAudio.CoreAudioApi;
using NAudio.Wave;
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

        //- Noise Data
        private float noiseLeft = 0;
        private float noiseRight = 0;
        private float noiseCancelingEffect = 1; // 0 ~ 1
        private float[] noiseLeftSpectrum = null;
        private float[] noiseRightSpectrum = null;

        //- Compare Device
        private AudioSubject comparingSubject;
        public delegate IEnumerable<AudioSubject> CompareableDeviceRequestDelegate();
        public CompareableDeviceRequestDelegate RequestComparableDevice;
        public Action<MMDevice> onSourceSelected;
        private IEnumerable<AudioSubject> audioSubjects;
        private long offsetTime = 0;
        private float volumeOffsetLeft = 1;
        private float volumeOffsetRight = 1;
        private float[] volumeSpectrumOffsetLeft;
        private float[] volumeSpectrumOffsetRight;
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
        public DataFlow Flow { get; private set; }
        public string SourceName { get; private set; }

        public AudioSubject(int frameSize, int fftLength, long debounceDuration)
        {
            this.frameSize = frameSize;
            this.fftLength = fftLength;
            spectrumTrigger = new SpectrumTrigger(fftLength / 2, 5);
            spectrumTrigger.OnTriggered += () =>
            {
                isTriggered = true;
                Base.Services.Debug.Log($"Volume Triggered at {DateTime.Now:HH:mm:ss.fff} for device: {SourceName}");
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
            Flow = device.DataFlow;
            SourceName = device.FriendlyName;
            handler = new AudioChannelHandler(capture, fftLength, frameSize, device.FriendlyName);
            if(handler.IsInitialized == false)
            {
                handler = null;
                return;
            }
            
            handler.SpectrumFrameAvailable += OnSpectrumFrame;
            handler.VolumeAvailable += OnVolumeFrame;

            AudioDeviceEntry.SampleRateText.Text = $"Sample Rate: {handler.SampleRate} Hz";
            AudioDeviceEntry.SampleRateText.Visibility = System.Windows.Visibility.Visible;

            spectrumRing = new(MaxSpectrumLength, 13, handler.HalfBins, sigma: 4, fill: true);
            compareRing = new(MaxSpectrumLength, 13, handler.HalfBins, sigma: 4, fill: true);
            differenceRing.Fill((ref TimedSpectrum value) =>
            {
                value.leftSpectrum = new float[handler.HalfBins];
                value.rightSpectrum = new float[handler.HalfBins];
                value.timestampTick = 0;
            });

            volumeSpectrumOffsetLeft = new float[handler.HalfBins];
            volumeSpectrumOffsetRight = new float[handler.HalfBins];
            Array.Fill(volumeSpectrumOffsetLeft, 1);
            Array.Fill(volumeSpectrumOffsetRight, 1);

            noiseLeftSpectrum = new float[handler.HalfBins];
            noiseRightSpectrum = new float[handler.HalfBins];
            Array.Fill(noiseLeftSpectrum, 0.00000001f);
            Array.Fill(noiseRightSpectrum, 0.00000001f);

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
                if (subjects.SelectedSource.ID == device?.ID)
                {
                    comparingSubject = subjects;
                    comparingSubject.OnSpectrumUpdated += CompareDeviceSpectrumUpdated;
                    break;
                }
            }

            YScaleMode scaleMode = comparingSubject == null ? YScaleMode.Log10 : YScaleMode.Linear;
            AudioDeviceEntry.LeftFFTChart.ScaleMode = scaleMode;
            AudioDeviceEntry.RightFFTChart.ScaleMode = scaleMode;
            SetThresholdMode(scaleMode);

            // Set spectrum line charts
            double minY = comparingSubject == null ? -60 : -30;
            double maxY = comparingSubject == null ? 0 : 30;
            AudioDeviceEntry.RightFFTChart.MinY = minY;
            AudioDeviceEntry.RightFFTChart.MaxY = maxY;
            AudioDeviceEntry.LeftFFTChart.MinY = minY;
            AudioDeviceEntry.LeftFFTChart.MaxY = maxY;
            SetThresholdMinMax(minY, maxY);

            // Set spectrograms
            minY = comparingSubject == null ? -60 : 00;
            maxY = comparingSubject == null ? 0 : 30;
            AudioDeviceEntry.LeftSpectrogram.MinDb = minY;
            AudioDeviceEntry.LeftSpectrogram.MaxDb = maxY;
            AudioDeviceEntry.RightSpectrogram.MinDb = minY;
            AudioDeviceEntry.RightSpectrogram.MaxDb = maxY;

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

                TimedSpectrum enqueued = differenceRing.EnqueueValue((ref TimedSpectrum value) =>
                {
                    float leftVolumeDifference = AudioChannelHandler.DbToSqrRms(volumeOffsetLeft - comparingSubject.volumeOffsetLeft);
                    float rightVolumeDifference = AudioChannelHandler.DbToSqrRms(volumeOffsetRight - comparingSubject.volumeOffsetRight);

                    for (int i = 0; i < handler.HalfBins; i++)
                    {
                        value.leftSpectrum[i] = LogorithmicCompare(
                            currentSpectrum.leftSpectrum[i],
                            peek.leftSpectrum[i],
                            comparingSubject.volumeSpectrumOffsetLeft[i] / volumeSpectrumOffsetLeft[i],
                            noiseLeftSpectrum[i],
                            noiseCancelingEffect
                        );

                        value.rightSpectrum[i] = LogorithmicCompare(
                            currentSpectrum.rightSpectrum[i],
                            peek.rightSpectrum[i],
                            comparingSubject.volumeSpectrumOffsetRight[i] / volumeSpectrumOffsetRight[i],
                            noiseRightSpectrum[i],
                            noiseCancelingEffect
                        );
                    }
                    value.timestampTick = currentSpectrum.timestampTick;
                });
                currentState?.SpectrumUpdate(enqueued);

                return true;
            }

            return false;
        }

        /// <summary>
        /// Logorithmic Compare
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        private static float LogorithmicCompare(float a, float b, float offset, float noiseBase, float noiseSigma)
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
            float logOffset = MathF.Log10(offset);
            float logMax = Math.Max(logA, logB);

            float noiseFix = 1;
            if(noiseSigma > 0)
            { 
                float noiseDb = MathF.Log10(noiseBase);
                noiseFix = MathF.Max(0, logMax - noiseDb - 2);
                noiseFix = 1 - MathF.Exp(-noiseFix * (1 / noiseSigma));
            }
            return 10 * (logA - logB + logOffset) * noiseFix;
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
                Array.Copy(handler.spectrumL, spectrum.leftSpectrum, handler.spectrumL.Length);
                Array.Copy(handler.spectrumR, spectrum.rightSpectrum, handler.spectrumR.Length);
            });

            if(comparingSubject == null)
                currentState?.SpectrumUpdate(enqueued);
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
                while (spectrumRing.Count > spectrumRing.Capacity / 2)
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

        public void SetTriggerTolerance(float value)
        {
            spectrumTrigger.Tolerance = value;
        }

        public void SetNoiseCancelingEffect(float value)
        {
            noiseCancelingEffect = value;
        }

        public void SetTriggerCutOff(int lower, int upper)
        {
            if (handler == null) return;

            int index = FFTUtil.FrequencyToFftIndex(lower, handler.SampleRate, handler.FftLength);
            spectrumTrigger.LowCutOff = index;

            index = FFTUtil.FrequencyToFftIndex(upper, handler.SampleRate, handler.FftLength);
            spectrumTrigger.HighCutOff = index;
        }

        public void SetTriggerSnesitivity(int value)
        {
            spectrumTrigger.Sensitivity = value;
        }

        public void SetThresholdMinMax(double min, double max)
        {
            AudioDeviceEntry.LeftFFTLowerChart.MinY = min;
            AudioDeviceEntry.LeftFFTLowerChart.MaxY = max;
            AudioDeviceEntry.RightFFTLowerChart.MinY = min;
            AudioDeviceEntry.RightFFTLowerChart.MaxY = max;

            AudioDeviceEntry.LeftFFTUpperChart.MinY = min;
            AudioDeviceEntry.LeftFFTUpperChart.MaxY = max;
            AudioDeviceEntry.RightFFTUpperChart.MinY = min;
            AudioDeviceEntry.RightFFTUpperChart.MaxY = max;
        }

        public void SetThresholdMode(YScaleMode scaleMode)
        {
            AudioDeviceEntry.LeftFFTLowerChart.ScaleMode = scaleMode;
            AudioDeviceEntry.LeftFFTUpperChart.ScaleMode = scaleMode;
            AudioDeviceEntry.RightFFTLowerChart.ScaleMode = scaleMode;
            AudioDeviceEntry.RightFFTUpperChart.ScaleMode = scaleMode;
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
            currentState?.Exit();
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
