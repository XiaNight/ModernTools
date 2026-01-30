using NAudio.CoreAudioApi;
using NAudio.Wave;
using static Base.Services.DeviceSelection;

namespace Audio.Generator
{
    public sealed class AudioGenerator : WaveProvider32, IDisposable
    {
        private readonly double phaseIncrement;
        private double phase;
        public float Amplitude { get; private set; }
        private readonly WasapiOut waveOut;

        private delegate int ReadDelegate(float[] buffer, in int offset, in int sampleCount);
        private ReadDelegate readDelegate;

        public WaveType CurrentWaveType { get; private set; } = WaveType.Sine;
        private MMDevice device;

        public AudioGenerator(MMDevice device, int sampleRate, double frequencyHz, float amplitude = 0.1f)
        {
            this.device = device;
            waveOut = new WasapiOut(device, AudioClientShareMode.Shared, true, 50);

            SetWaveFormat(sampleRate, 1); // mono
            phaseIncrement = 2.0 * Math.PI * frequencyHz / sampleRate;
            this.Amplitude = amplitude;

            readDelegate = SineRead;

            waveOut.Init(this);
        }

        public override int Read(float[] buffer, int offset, int sampleCount)
        {
            return readDelegate(buffer, offset, sampleCount);
        }

        private int SineRead(float[] buffer, in int offset, in int sampleCount)
        {
            for (int i = 0; i < sampleCount; i++)
            {
                buffer[offset + i] = (float)(Math.Sin(phase) * Amplitude);

                phase += phaseIncrement;
                if (phase >= 2.0 * Math.PI)
                    phase -= 2.0 * Math.PI;
            }

            return sampleCount;
        }

        private int SquareRead(float[] buffer, in int offset, in int sampleCount)
        {
            for (int i = 0; i < sampleCount; i++)
            {
                buffer[offset + i] = (float)((Math.Sin(phase) >= 0 ? 1.0 : -1.0) * Amplitude);
                phase += phaseIncrement;
                if (phase >= 2.0 * Math.PI)
                    phase -= 2.0 * Math.PI;
            }
            return sampleCount;
        }

        private int NoiseRead(float[] buffer, in int offset, in int sampleCount)
        {
            Random rand = new Random();
            for (int i = 0; i < sampleCount; i++)
            {
                buffer[offset + i] = (float)((rand.NextDouble() * 2.0 - 1.0) * Amplitude);
            }
            return sampleCount;
        }

        public void SetWaveType(WaveType waveType)
        {
            CurrentWaveType = waveType;
            readDelegate = waveType switch
            {
                WaveType.Sine => SineRead,
                WaveType.Square => SquareRead,
                WaveType.Noise => NoiseRead,
                _ => SineRead,
            };
        }

        public void Start()
        {
            waveOut.Play();
        }

        public void Pause()
        {
            waveOut.Pause();
        }

        public void Stop()
        {
            waveOut.Stop();
        }

        public void Dispose()
        {
            waveOut?.Stop();
            waveOut?.Dispose();
        }

        public void SetAmplitude(float newAmplitude)
        {
            Amplitude = newAmplitude;
        }

        public enum WaveType
        {
            Sine,
            Square,
            Noise
        }
    }
}
