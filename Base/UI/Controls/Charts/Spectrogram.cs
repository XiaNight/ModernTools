using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Base.Components.Chart
{
    /// <summary>
    /// A spectrogram control that uses GPU-accelerated rendering via ComputeSharp.
    /// Throws <see cref="GpuNotAvailableException"/> if no compatible GPU is available.
    /// </summary>
    public class Spectrogram : FrameworkElement
    {
        private readonly GpuChartRenderer _gpuRenderer;
        private WriteableBitmap _bitmap;
        private byte[] _pixels;
        private int _stride;
        private Color[] _colorMap;

        private long _lastTimestampTicks;
        private bool _hasTimestamp;

        public static readonly DependencyProperty TimeWindowSecondsProperty =
            DependencyProperty.Register(
                nameof(TimeWindowSeconds),
                typeof(double),
                typeof(Spectrogram),
                new FrameworkPropertyMetadata(3.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double TimeWindowSeconds
        {
            get => (double)GetValue(TimeWindowSecondsProperty);
            set => SetValue(TimeWindowSecondsProperty, value);
        }
        
        public double MinDb { get; set; } = -80.0;
        public double MaxDb { get; set; } = 0.0;
        public double MinHz { get; set; } = 20.0;
        public double MaxHz { get; set; } = 2000.0;
        public int SampleRate { get; set; } = 48000;
        public int FftLength { get; set; } = 4096;

        /// <summary>
        /// Initializes a new instance of the <see cref="Spectrogram"/> class.
        /// </summary>
        /// <exception cref="GpuNotAvailableException">Thrown when no compatible GPU is available.</exception>
        public Spectrogram()
        {
            // Ensure GPU is available - throws GpuNotAvailableException if not
            GpuChartRenderer.EnsureGpuAvailable();
            _gpuRenderer = GpuChartRenderer.Instance;

            SnapsToDevicePixels = true;
            BuildColorMap();

            Loaded += (_, __) => InitializeBitmap();
            SizeChanged += (_, __) => InitializeBitmap();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(
                double.IsInfinity(availableSize.Width) ? 100 : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? 50 : availableSize.Height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            InitializeBitmap();
            return finalSize;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            if (_bitmap != null)
            {
                drawingContext.DrawImage(_bitmap, new Rect(0, 0, ActualWidth, ActualHeight));
            }
        }

        public void AddSpectrum(float[] sqrMagnitudes)
        {
            AddSpectrum(sqrMagnitudes, DateTime.Now.Ticks);
        }

        public void AddSpectrum(float[] sqrMagnitudes, long timestampTicks)
        {
            if (sqrMagnitudes == null || sqrMagnitudes.Length == 0)
                return;

            if (!CheckBitmap())
                return;

            int width = _bitmap.PixelWidth;
            int height = _bitmap.PixelHeight;
            if (width <= 0 || height <= 0)
                return;

            double timeWindow = TimeWindowSeconds;
            if (timeWindow <= 0) timeWindow = 0.001;

            int pixelShift;

            if (!_hasTimestamp)
            {
                pixelShift = 1;
                _hasTimestamp = true;
            }
            else
            {
                long dtTicks = timestampTicks - _lastTimestampTicks;
                if (dtTicks < 0)
                {
                    dtTicks = 0;
                }

                double dtSeconds = dtTicks / (double)TimeSpan.TicksPerSecond;
                double secondsPerPixel = timeWindow / width;
                if (secondsPerPixel <= 0) secondsPerPixel = timeWindow;

                pixelShift = (int)Math.Round(dtSeconds / secondsPerPixel);
                if (pixelShift < 0) pixelShift = 0;
                if (pixelShift > width) pixelShift = width;
            }

            _lastTimestampTicks = timestampTicks;

            if (pixelShift == 0)
                pixelShift = 1;

            // GPU-accelerated spectrogram rendering
            _gpuRenderer.RenderSpectrogram(
                sqrMagnitudes.AsSpan(),
                _pixels.AsSpan(),
                width,
                height,
                pixelShift,
                (float)MinDb,
                (float)MaxDb,
                (float)MinHz,
                (float)MaxHz,
                SampleRate,
                FftLength);

            _bitmap.WritePixels(
                new Int32Rect(0, 0, width, height),
                _pixels, _stride, 0);

            InvalidateVisual();
        }

        private bool CheckBitmap()
        {
            if (_bitmap == null)
                InitializeBitmap();

            return _bitmap != null && _pixels != null;
        }

        private void InitializeBitmap()
        {
            int width = Math.Max(1, (int)Math.Ceiling(ActualWidth));
            int height = Math.Max(1, (int)Math.Ceiling(ActualHeight));

            if (width <= 0 || height <= 0)
                return;

            if (_bitmap != null &&
                _bitmap.PixelWidth == width &&
                _bitmap.PixelHeight == height)
                return;

            _bitmap = new WriteableBitmap(
                width, height,
                96, 96,
                PixelFormats.Bgra32,
                null);

            _stride = width * 4;
            _pixels = new byte[height * _stride];

            InvalidateVisual();
        }

        private void BuildColorMap()
        {
            _colorMap = new Color[256];

            var stops = new[]
            {
                (pos: 0.0, color: Color.FromRgb(0, 0, 0)),
                (pos: 0.2, color: Color.FromRgb(30, 0, 60)),
                (pos: 0.4, color: Color.FromRgb(120, 0, 120)),
                (pos: 0.6, color: Color.FromRgb(220, 30, 80)),
                (pos: 0.8, color: Color.FromRgb(255, 140, 0)),
                (pos: 1.0, color: Color.FromRgb(255, 255, 160)),
            };

            for (int i = 0; i < 256; i++)
            {
                double t = i / 255.0;

                var a = stops[0];
                var b = stops[stops.Length - 1];

                for (int s = 0; s < stops.Length - 1; s++)
                {
                    if (t >= stops[s].pos && t <= stops[s + 1].pos)
                    {
                        a = stops[s];
                        b = stops[s + 1];
                        break;
                    }
                }

                double localT = (t - a.pos) / (b.pos - a.pos);
                byte r = (byte)(a.color.R + (b.color.R - a.color.R) * localT);
                byte g = (byte)(a.color.G + (b.color.G - a.color.G) * localT);
                byte bl = (byte)(a.color.B + (b.color.B - a.color.B) * localT);

                _colorMap[i] = Color.FromRgb(r, g, bl);
            }
        }
    }
}
