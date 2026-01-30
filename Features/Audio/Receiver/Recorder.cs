using System;
using System.Diagnostics;
using System.IO;
using NAudio.Wave;

namespace Audio.Receiver
{
    sealed class SegmentingRecorder : IDisposable
    {
        private readonly string name;
        private readonly string baseDir;
        private readonly TimeSpan segment;

        private IWaveIn capture;
        private Stopwatch sw;

        private int segmentIndex;
        private string currentWavPath;
        private WaveFileWriter wavWriter;

        public SegmentingRecorder(string name, IWaveIn capture, string baseDir, TimeSpan segment)
        {
            this.name = name ?? throw new ArgumentNullException(nameof(name));
            this.capture = capture;
            this.baseDir = baseDir ?? throw new ArgumentNullException(nameof(baseDir));
            this.segment = segment;

            Directory.CreateDirectory(this.baseDir);
        }

        public void Start()
        {
            if (wavWriter != null) return;

            sw = Stopwatch.StartNew();
            segmentIndex = 0;

            OpenNewSegment(capture.WaveFormat);

            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnStopped;
        }

        public void Stop()
        {
            capture.DataAvailable -= OnDataAvailable;
            capture.RecordingStopped -= OnStopped;

            CloseSegment();
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            if (capture == null || sw == null) return;

            if (sw.Elapsed >= segment)
            {
                CloseSegment();
                sw.Restart();
                OpenNewSegment(capture.WaveFormat);
            }

            wavWriter?.Write(e.Buffer, 0, e.BytesRecorded);
            wavWriter?.Flush();
        }

        private void OnStopped(object sender, StoppedEventArgs e)
        {
            Stop();

            if (e.Exception != null)
                Console.Error.WriteLine($"{name} stopped with error: {e.Exception}");
        }

        private void OpenNewSegment(WaveFormat captureFormat)
        {
            var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var segName = $"{name}_{ts}_{segmentIndex:D4}";

            currentWavPath = Path.Combine(baseDir, segName + ".wav");
            wavWriter = new WaveFileWriter(currentWavPath, captureFormat);

            segmentIndex++;
        }

        private void CloseSegment()
        {
            var writer = wavWriter;
            wavWriter = null;
            currentWavPath = null;

            writer?.Dispose();
        }

        public void Dispose()
        {
            try { Stop(); } catch { }
            CloseSegment();
        }
    }
}
