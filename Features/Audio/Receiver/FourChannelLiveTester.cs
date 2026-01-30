using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Timers;
using Windows.System.UserProfile;

public class FourChannelLiveTester : IDisposable
{
    private readonly WasapiLoopbackCapture _loopback;
    private readonly WasapiCapture _micCapture;

    private readonly ConcurrentQueue<TimedValue> _outL = new();
    private readonly ConcurrentQueue<TimedValue> _outR = new();
    private readonly ConcurrentQueue<TimedValue> _micL = new();
    private readonly ConcurrentQueue<TimedValue> _micR = new();

    private long lastOutTicks = 0;
    private long lastMicTicks = 0;

    private readonly System.Timers.Timer _frameTimer;
    private readonly int _frameSize;

    // Raw samples frame event (for graphing waveforms / spectra)
    public event Action<TimedValue[], TimedValue[], TimedValue[], TimedValue[]> SamplesFrameAvailable;

    // Loudness event (one value per channel, e.g. RMS in dB)
    public event Action<TimedValue, TimedValue, TimedValue, TimedValue> LoudnessFrameAvailable;

    public record TimedValue (float Value, long Timestamp);

    public FourChannelLiveTester(
        int frameSize = 512,
        double frameIntervalMs = 20.0)
    {
        _frameSize = frameSize;

        var deviceEnum = new MMDeviceEnumerator();
        var outDevice = deviceEnum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var micDevice = deviceEnum.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);

        _loopback = new WasapiLoopbackCapture(outDevice);
        _micCapture = new WasapiCapture(micDevice);

        if (_loopback.WaveFormat.SampleRate != _micCapture.WaveFormat.SampleRate)
            throw new InvalidOperationException("Sample rates must match. Add resampling if needed.");

        if (_loopback.WaveFormat.Channels < 2 || _micCapture.WaveFormat.Channels < 2)
            throw new InvalidOperationException("Both devices must be stereo for 4-channel test.");

        _loopback.DataAvailable += Loopback_DataAvailable;
        _micCapture.DataAvailable += Mic_DataAvailable;

        _frameTimer = new System.Timers.Timer(frameIntervalMs);
        _frameTimer.AutoReset = true;
        _frameTimer.Elapsed += FrameTimer_Elapsed;
    }

    public void Start()
    {
        _loopback.StartRecording();
        _micCapture.StartRecording();
        _frameTimer.Start();
    }

    public void Stop()
    {
        _frameTimer.Stop();
        _loopback.StopRecording();
        _micCapture.StopRecording();
    }

    private void Loopback_DataAvailable(object sender, WaveInEventArgs e)
    {
        EnqueueStereoSamples(e.Buffer, e.BytesRecorded, _loopback.WaveFormat, _outL, _outR, ref lastOutTicks);
    }

    private void Mic_DataAvailable(object sender, WaveInEventArgs e)
    {
        EnqueueStereoSamples(e.Buffer, e.BytesRecorded, _micCapture.WaveFormat, _micL, _micR, ref lastMicTicks);
    }

    private static void EnqueueStereoSamples(
        byte[] buffer,
        int bytes,
        WaveFormat format,
        ConcurrentQueue<TimedValue> chL,
        ConcurrentQueue<TimedValue> chR,
        ref long chLastTicks)
    {
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

                long t = chLastTicks + (tick - chLastTicks) * f / frames;

                chL.Enqueue(new TimedValue(sampleL, t));
                chR.Enqueue(new TimedValue(sampleR, t));
            }

            chLastTicks = tick;
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

                long t = chLastTicks + (tick - chLastTicks) * f / frames;

                chL.Enqueue(new TimedValue(sL * scale, t));
                chR.Enqueue(new TimedValue(sR * scale, t));
            }

            chLastTicks = tick;
        }
        else
        {
            throw new NotSupportedException("Unsupported format. Add conversion as needed.");
        }
    }

    private void FrameTimer_Elapsed(object sender, ElapsedEventArgs e)
    {
        while (_outL.Count >= _frameSize &&
               _outR.Count >= _frameSize &&
               _micL.Count >= _frameSize &&
               _micR.Count >= _frameSize)
        {
            var outL = DequeueWindow(_outL, _frameSize);
            var outR = DequeueWindow(_outR, _frameSize);
            var micL = DequeueWindow(_micL, _frameSize);
            var micR = DequeueWindow(_micR, _frameSize);

            SamplesFrameAvailable?.Invoke(outL, outR, micL, micR);

            float outLRms = ComputeRms(outL);
            float outRRms = ComputeRms(outR);
            float micLRms = ComputeRms(micL);
            float micRRms = ComputeRms(micR);

            float outLdB = ToDb(outLRms);
            float outRdB = ToDb(outRRms);
            float micLdB = ToDb(micLRms);
            float micRdB = ToDb(micRRms);

            LoudnessFrameAvailable?.Invoke(new(outLdB, outL[0].Timestamp), new(outRdB, outR[0].Timestamp), new(micLdB, micL[0].Timestamp), new(micRdB, micR[0].Timestamp));
        }
    }

    private static T[] DequeueWindow<T>(ConcurrentQueue<T> q, int count)
    {
        var window = new T[count];
        for (int i = 0; i < count; i++)
        {
            if (!q.TryDequeue(out var s))
                break;
            window[i] = s;
        }
        return window;
    }

    /// <summary>
    /// Compute RMS (root mean square) of the samples
    /// </summary>
    /// <param name="samples"></param>
    /// <returns></returns>
    private static float ComputeRms(TimedValue[] samples)
    {
        if (samples == null || samples.Length < 2)
            return 0f;

        double sum = 0;
        int count = samples.Length - 1;

        try
        {
            for (int i = 1; i < samples.Length; i++)
            {
                float d = samples[i].Value - samples[i - 1].Value;
                sum += d * d;
            }
        }
        catch {
            Debug.WriteLine("RMS computation error");
        }

        return (float)Math.Sqrt(sum / count);
    }

    private static float ToDb(float rms)
    {
        return 20f * (float)Math.Log10(rms + 1e-12f);
    }

    public void Dispose()
    {
        Stop();
        _loopback.DataAvailable -= Loopback_DataAvailable;
        _micCapture.DataAvailable -= Mic_DataAvailable;
        _loopback.Dispose();
        _micCapture.Dispose();
        _frameTimer.Dispose();
    }
}