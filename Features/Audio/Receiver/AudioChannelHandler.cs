using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.MediaFoundation;
using NAudio.Wave;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;

namespace Audio.Receiver
{
    public class AudioChannelHandler : IDisposable
    {
        private readonly WasapiCapture capture;
        private GaussianSqrRms gaussianSqrRms;
        private readonly SegmentingRecorder recorder;

        private readonly ConcurrentQueue<TimedValue<float>> queue = new();
        private long lastOutTicks = 0;

        private CancellationTokenSource cts;
        private int frameSize;

        private TimedValue<float>[] ring;
        private int ringIndex;

        private List<TimedValue<float>> framesBuffer;
        private List<TimedValue<float>> volumeBuffer;

        // Raw samples frame event (for graphing waveforms / spectra)
        public event Action<List<TimedValue<float>>> FramesAvailable;

        // Volume event (one value per channel, e.g. RMS in dB)
        public event Action<List<TimedValue<float>>> VolumeAvailable;

        // Spectrum event (one value per channel, e.g. FFT bins)
        public event Action<long> SpectrumFrameAvailable;

        // ---- Spectrum config
        public readonly int FftLength;
        public readonly int FftExponent;
        public readonly int HopSize;
        public readonly int HalfBins;

        // ---- Spectrum state
        private readonly TimedValue<float>[] fftRing;
        private int fftRingIndex;         // next write index
        private int hopCounter;           // samples since last FFT
        private int fftFillCount;         // how many samples collected (cap at FftLength)

        // ---- FFT working buffers
        private readonly Complex[] fftBufferL;
        private readonly Complex[] fftBufferR;

        // ---- Output spectrum buffers (power)
        public readonly float[] spectrumL;
        public readonly float[] spectrumR;

        // ---- Window
        private readonly float[] window;

        CpsDebugger debugger = new();

        public int SampleRate => capture.WaveFormat.SampleRate;

        public AudioChannelHandler(WasapiCapture capture, int fftLength = 2048, int frameSize = 512, string name = "AudioSource")
        {
            this.capture = capture;
            this.FftLength = fftLength;

            FftExponent = (int)Math.Log2(fftLength);
            HopSize = (int)(this.FftLength * 0.1f);
            HalfBins = (this.FftLength / 2) + 1;

            fftRing = new TimedValue<float>[fftLength];
            fftBufferL = new Complex[fftLength];
            fftBufferR = new Complex[fftLength];
            spectrumL = new float[HalfBins];
            spectrumR = new float[HalfBins];
            window = new float[FftLength];

            SetFrameSize(frameSize);

            if (capture.WaveFormat.Channels < 2)
                throw new InvalidOperationException("device must be stereo (2 channels Left and Right).");

            capture.DataAvailable += CaptureDataAvailable;

            for (int i = 0; i < fftLength; i++)
            {
                window[i] = (float)FastFourierTransform.HammingWindow(i, fftLength);
            }

            //- Recording
            var baseDir = Path.Combine(AppContext.BaseDirectory, "recordings");
            Directory.CreateDirectory(baseDir);

            recorder = new SegmentingRecorder(
                name: name,
                capture: capture,
                baseDir: baseDir,
                segment: TimeSpan.FromMinutes(10)
            );
        }

        public void SetFrameSize(int frameSize)
        {
            this.frameSize = frameSize;
            ring = new TimedValue<float>[frameSize];
            framesBuffer = new List<TimedValue<float>>(frameSize * 4);
            volumeBuffer = new List<TimedValue<float>>(frameSize * 4);
            gaussianSqrRms = new GaussianSqrRms(frameSize);
            ringIndex = 0;
        }

        public void StartAudioStream()
        {
            MediaFoundationApi.Startup();
            try
            {
                capture.StartRecording();
            }
            catch (Exception ex)
            {
                Base.Services.Debug.Log("Error starting audio capture:", ex.Message);
            }
            StartConsumer();
        }

        public void StopAudioStream()
        {
            StopLoop();
            capture.StopRecording();

            StopRecorder();
            MediaFoundationApi.Shutdown();
        }

        public void StartRecorder()
        {
            recorder.Start();
        }

        public void StopRecorder()
        {
            recorder.Stop();
        }

        private void StartConsumer()
        {
            cts = new CancellationTokenSource();
            Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    FrameTimer_Elapsed();
                    await Task.Yield();
                }
                cts.Dispose();
            }, cts.Token);
        }

        private void StopLoop()
        {
            cts?.Cancel();
        }

        private void CaptureDataAvailable(object sender, WaveInEventArgs e)
        {
            byte[] buffer = e.Buffer;
            int bytes = e.BytesRecorded;
            WaveFormat format = capture.WaveFormat;

            int channels = format.Channels;
            if (channels < 2)
                throw new NotSupportedException("Need at least 2 channels for stereo.");

            if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
            {
                int totalSamples = bytes / 4;
                int frames = totalSamples / channels;

                long tick = DateTime.Now.Ticks;

                for (int f = 0; f < frames; f++)
                {
                    int baseIndex = f * channels;

                    float sampleL = BitConverter.ToSingle(buffer, (baseIndex + 0) * 4);
                    float sampleR = BitConverter.ToSingle(buffer, (baseIndex + 1) * 4);

                    long t = lastOutTicks + ((tick - lastOutTicks) * f) / frames;

                    queue.Enqueue(new TimedValue<float>(sampleL, sampleR, t));
                }

                lastOutTicks = tick;
            }
            else if (format.BitsPerSample == 16)
            {
                int totalSamples = bytes / 2;
                int frames = totalSamples / channels;
                const float scale = 1.0f / 32768f;

                long tick = DateTime.Now.Ticks;

                for (int f = 0; f < frames; f++)
                {
                    int baseIndex = f * channels;

                    short sL = BitConverter.ToInt16(buffer, (baseIndex + 0) * 2);
                    short sR = BitConverter.ToInt16(buffer, (baseIndex + 1) * 2);

                    long t = lastOutTicks + ((tick - lastOutTicks) * f) / frames;

                    queue.Enqueue(new TimedValue<float>(sL * scale, sR * scale, t));
                }

                lastOutTicks = tick;
            }
            else
            {
                throw new NotSupportedException("Unsupported format. Add conversion as needed.");
            }
        }

        private void FrameTimer_Elapsed()
        {
            if (queue.IsEmpty) return;

            framesBuffer.Clear();
            volumeBuffer.Clear();

            bool needVolume = VolumeAvailable != null;

            while (queue.TryDequeue(out var sample))
            {
                ring[ringIndex] = sample;
                ringIndex = (ringIndex + 1) & (frameSize - 1);

                framesBuffer.Add(sample);

                fftRing[fftRingIndex] = sample;
                fftRingIndex = (fftRingIndex + 1) & (FftLength - 1);

                if (fftFillCount < FftLength) fftFillCount++;
                hopCounter++;

                if (fftFillCount == FftLength && hopCounter >= HopSize)
                {
                    hopCounter -= HopSize;
                    ComputeSpectrumFromFftRing(sample.Timestamp);
                    SpectrumFrameAvailable?.Invoke(sample.Timestamp);
                }

                if (needVolume)
                {
                    var (lsqrrms, rsqrrms) = gaussianSqrRms.Compute(ring, ringIndex);
                    volumeBuffer.Add(new TimedValue<float>(SqrRmsToDb(lsqrrms), SqrRmsToDb(rsqrrms), sample.Timestamp));
                }
            }

            FramesAvailable?.Invoke(framesBuffer);
            VolumeAvailable?.Invoke(volumeBuffer);
        }

        private void ComputeSpectrumFromFftRing(long timestamp)
        {
            debugger.Mark();

            // Oldest sample is at fftRingIndex (because fftRingIndex is "next write")
            int start = fftRingIndex;

            for (int i = 0; i < FftLength; i++)
            {
                int idx = (start + i) & (FftLength - 1);
                var s = fftRing[idx];
                float w = window[i];

                fftBufferL[i].X = s.Left * w;
                fftBufferL[i].Y = 0f;

                fftBufferR[i].X = s.Right * w;
                fftBufferR[i].Y = 0f;
            }

            FastFourierTransform.FFT(true, FftExponent, fftBufferL);
            FastFourierTransform.FFT(true, FftExponent, fftBufferR);

            // Power spectrum (0..Nyquist)
            for (int k = 0; k < HalfBins; k++)
            {
                float reL = fftBufferL[k].X, imL = fftBufferL[k].Y;
                spectrumL[k] = reL * reL + imL * imL;

                float reR = fftBufferR[k].X, imR = fftBufferR[k].Y;
                spectrumR[k] = reR * reR + imR * imR;
            }
        }

        public static float SqrRmsToDb(float rms)
        {
            return 10f * MathF.Log10(rms + 1e-20f);
        }

        public static float DbToSqrRms(float db)
        {
            return MathF.Pow(10f, db / 10f);
        }

        public void Dispose()
        {
            StopLoop();
            capture.Dispose();
            recorder.Dispose();
        }

        public sealed class GaussianSqrRms
        {
            private readonly int count;
            private readonly int diffCount;
            private readonly int mask;
            private readonly float[] wNorm; // length = diffCount, sums to 1

            public int Count => count;
            private static bool IsPowerOfTwo(int x) => (x & (x - 1)) == 0;

            public GaussianSqrRms(int count)
            {
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(count, 1);
                if (!IsPowerOfTwo(count)) throw new ArgumentException("count must be a power of 2.", nameof(count));

                this.count = count;
                diffCount = count - 1;
                mask = count - 1;

                wNorm = new float[diffCount];

                double center = (diffCount - 1) * 0.5;
                double sigma = diffCount / 3.0;
                if (sigma < 1e-12) sigma = 1e-12;
                double twoSigmaSq = 2.0 * sigma * sigma;

                double sum = 0.0;
                for (int k = 0; k < diffCount; k++)
                {
                    double x = k - center;
                    double w = Math.Exp(-(x * x) / twoSigmaSq);
                    sum += w;
                    wNorm[k] = (float)w;
                }

                if (sum <= 0.0) throw new InvalidOperationException("Invalid Gaussian weight sum.");

                float invSum = (float)(1.0 / sum);
                for (int k = 0; k < diffCount; k++)
                    wNorm[k] *= invSum;
            }

            public (float leftSqrRms, float rightSqrRms) Compute(TimedValue<float>[] buffer, int offset)
            {
                offset &= mask;

                float sumL = 0f;
                float sumR = 0f;

                int idxPrev = offset;
                var prev = buffer[idxPrev];

                for (int i = 1; i < count; i++)
                {
                    int idx = (offset + i) & mask;
                    var s = buffer[idx];

                    float dL = s.Left - prev.Left;
                    float dR = s.Right - prev.Right;

                    float w = wNorm[i - 1];

                    sumL += w * dL * dL;
                    sumR += w * dR * dR;

                    prev = s;
                }

                return (sumL, sumR);
            }
        }

        public readonly struct TimedValue<T>(T left, T right, long timestamp)
        {
            public T Left { get; } = left;
            public T Right { get; } = right;
            public long Timestamp { get; } = timestamp;
        }
    }

    /// <summary>
    /// Counts "calculations per second" (CPS) from calls to Mark().
    /// Call Mark() once whenever a calculation finishes.
    /// Logs CPS periodically to System.Diagnostics.Debug.
    /// </summary>
    public sealed class CpsDebugger
    {
        private readonly string _name;
        private readonly long _logIntervalTicks;
        private long _windowStartTimestamp;
        private long _nextLogTimestamp;
        private long _countInWindow;

        public CpsDebugger(string name = "CPS", TimeSpan? logInterval = null)
        {
            _name = string.IsNullOrWhiteSpace(name) ? "CPS" : name;

            var interval = logInterval ?? TimeSpan.FromSeconds(1);
            if (interval <= TimeSpan.Zero) interval = TimeSpan.FromSeconds(1);

            _logIntervalTicks = (long)(interval.TotalSeconds * Stopwatch.Frequency);

            var now = Stopwatch.GetTimestamp();
            _windowStartTimestamp = now;
            _nextLogTimestamp = now + _logIntervalTicks;
        }

        /// <summary>
        /// Call this when ONE calculation finishes.
        /// </summary>
        public void Mark()
        {
            Interlocked.Increment(ref _countInWindow);
            TryLogIfDue(Stopwatch.GetTimestamp());
        }

        /// <summary>
        /// Call this when N calculations finish at once (optional).
        /// </summary>
        public void Mark(long count)
        {
            if (count <= 0) return;
            Interlocked.Add(ref _countInWindow, count);
            TryLogIfDue(Stopwatch.GetTimestamp());
        }

        /// <summary>
        /// Force a log right now using the current window.
        /// </summary>
        public void Flush()
        {
            LogAndReset(Stopwatch.GetTimestamp());
        }

        private void TryLogIfDue(long now)
        {
            var next = Volatile.Read(ref _nextLogTimestamp);
            if (now < next) return;

            if (Interlocked.CompareExchange(ref _nextLogTimestamp, next + _logIntervalTicks, next) == next)
            {
                LogAndReset(now);
            }
        }

        private void LogAndReset(long now)
        {
            var start = Interlocked.Exchange(ref _windowStartTimestamp, now);
            var count = Interlocked.Exchange(ref _countInWindow, 0);

            var elapsedTicks = now - start;
            if (elapsedTicks <= 0) return;

            var seconds = elapsedTicks / (double)Stopwatch.Frequency;
            var cps = count / seconds;

            Debug.WriteLine($"[{_name}] CPS: {cps:F2} (count={count}, dt={seconds:F3}s)");
        }
    }
}
