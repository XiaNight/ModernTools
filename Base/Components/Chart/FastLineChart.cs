using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Base.Components.Chart
{
    public enum YScaleMode
    {
        Linear = 0,
        Log10 = 1
    }

    /// <summary>
    /// A fast line chart control that uses GPU-accelerated rendering via ComputeSharp.
    /// Throws <see cref="GpuNotAvailableException"/> if no compatible GPU is available.
    /// </summary>
    public sealed class FastLineChartControl : FrameworkElement
    {
        private readonly GpuChartRenderer _gpuRenderer;

        /// <summary>
        /// Initializes a new instance of the <see cref="FastLineChartControl"/> class.
        /// </summary>
        /// <exception cref="GpuNotAvailableException">Thrown when no compatible GPU is available.</exception>
        public FastLineChartControl()
        {
            // Ensure GPU is available - throws GpuNotAvailableException if not
            GpuChartRenderer.EnsureGpuAvailable();
            _gpuRenderer = GpuChartRenderer.Instance;

            UseLayoutRounding = false;
            SnapsToDevicePixels = false;

            SetCurrentValue(MinYProperty, 0.0);
            SetCurrentValue(MaxYProperty, 1.0);

            SetCurrentValue(ScaleModeProperty, YScaleMode.Linear);
            SetCurrentValue(LogEpsilonProperty, 1e-12);

            SetCurrentValue(SpectralModeProperty, false);
            SetCurrentValue(MinHzProperty, 0.0);
            SetCurrentValue(MaxHzProperty, 22050.0);
            SetCurrentValue(SampleRateProperty, 44100.0);
            SetCurrentValue(FftLengthProperty, 1024);

            SetCurrentValue(LineBrushProperty, Brushes.CornflowerBlue);
            SetCurrentValue(LineThicknessProperty, 2.0);
            SetCurrentValue(UseAntialiasProperty, false);

            SetCurrentValue(ShowAxisProperty, true);
            SetCurrentValue(AxisPaddingProperty, new Thickness(56, 8, 8, 28));
            SetCurrentValue(LabelFontSizeProperty, 12d);
            SetCurrentValue(LabelBrushProperty, Brushes.Black);
            SetCurrentValue(AxisPenProperty, new Pen(Brushes.Gray, 1));

            UpdateLinePen();
            ApplyAA();
        }

        // ---- Dependency Properties ----
        public static readonly DependencyProperty MinYProperty =
            DependencyProperty.Register(nameof(MinY), typeof(double), typeof(FastLineChartControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MaxYProperty =
            DependencyProperty.Register(nameof(MaxY), typeof(double), typeof(FastLineChartControl),
                new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ScaleModeProperty =
            DependencyProperty.Register(nameof(ScaleMode), typeof(YScaleMode), typeof(FastLineChartControl),
                new FrameworkPropertyMetadata(YScaleMode.Linear, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LogEpsilonProperty =
            DependencyProperty.Register(nameof(LogEpsilon), typeof(double), typeof(FastLineChartControl),
                new FrameworkPropertyMetadata(1e-12, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SpectralModeProperty =
            DependencyProperty.Register(nameof(SpectralMode), typeof(bool), typeof(FastLineChartControl),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MinHzProperty =
            DependencyProperty.Register(nameof(MinHz), typeof(double), typeof(FastLineChartControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MaxHzProperty =
            DependencyProperty.Register(nameof(MaxHz), typeof(double), typeof(FastLineChartControl),
                new FrameworkPropertyMetadata(22050.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SampleRateProperty =
            DependencyProperty.Register(nameof(SampleRate), typeof(double), typeof(FastLineChartControl),
                new FrameworkPropertyMetadata(44100.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FftLengthProperty =
            DependencyProperty.Register(nameof(FftLength), typeof(int), typeof(FastLineChartControl),
                new FrameworkPropertyMetadata(1024, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LineBrushProperty =
            DependencyProperty.Register(nameof(LineBrush), typeof(Brush), typeof(FastLineChartControl),
                new FrameworkPropertyMetadata(Brushes.CornflowerBlue, FrameworkPropertyMetadataOptions.AffectsRender, OnLineStyleChanged));

        public static readonly DependencyProperty LineThicknessProperty =
            DependencyProperty.Register(nameof(LineThickness), typeof(double), typeof(FastLineChartControl),
                new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender, OnLineStyleChanged));

        public static readonly DependencyProperty UseAntialiasProperty =
            DependencyProperty.Register(nameof(UseAntialias), typeof(bool), typeof(FastLineChartControl),
                new FrameworkPropertyMetadata(false, OnUseAAChanged));

        public static readonly DependencyProperty ShowAxisProperty =
            DependencyProperty.Register(nameof(ShowAxis), typeof(bool), typeof(FastLineChartControl),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty AxisPaddingProperty =
            DependencyProperty.Register(nameof(AxisPadding), typeof(Thickness), typeof(FastLineChartControl),
                new FrameworkPropertyMetadata(new Thickness(56, 8, 8, 28), FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LabelFontSizeProperty =
            DependencyProperty.Register(nameof(LabelFontSize), typeof(double), typeof(FastLineChartControl),
                new FrameworkPropertyMetadata(12d, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LabelBrushProperty =
            DependencyProperty.Register(nameof(LabelBrush), typeof(Brush), typeof(FastLineChartControl),
                new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty AxisPenProperty =
            DependencyProperty.Register(nameof(AxisPen), typeof(Pen), typeof(FastLineChartControl),
                new FrameworkPropertyMetadata(new Pen(Brushes.Gray, 1), FrameworkPropertyMetadataOptions.AffectsRender));

        public double MinY
        {
            get => (double)GetValue(MinYProperty);
            set => SetValue(MinYProperty, value);
        }

        public double MaxY
        {
            get => (double)GetValue(MaxYProperty);
            set => SetValue(MaxYProperty, value);
        }

        public YScaleMode ScaleMode
        {
            get => (YScaleMode)GetValue(ScaleModeProperty);
            set => SetValue(ScaleModeProperty, value);
        }

        public double LogEpsilon
        {
            get => (double)GetValue(LogEpsilonProperty);
            set => SetValue(LogEpsilonProperty, value);
        }

        public bool SpectralMode
        {
            get => (bool)GetValue(SpectralModeProperty);
            set => SetValue(SpectralModeProperty, value);
        }

        public double MinHz
        {
            get => (double)GetValue(MinHzProperty);
            set => SetValue(MinHzProperty, value);
        }

        public double MaxHz
        {
            get => (double)GetValue(MaxHzProperty);
            set => SetValue(MaxHzProperty, value);
        }

        public double SampleRate
        {
            get => (double)GetValue(SampleRateProperty);
            set => SetValue(SampleRateProperty, value);
        }

        public int FftLength
        {
            get => (int)GetValue(FftLengthProperty);
            set => SetValue(FftLengthProperty, value);
        }

        public Brush LineBrush
        {
            get => (Brush)GetValue(LineBrushProperty);
            set => SetValue(LineBrushProperty, value);
        }

        public double LineThickness
        {
            get => (double)GetValue(LineThicknessProperty);
            set => SetValue(LineThicknessProperty, value);
        }

        public bool UseAntialias
        {
            get => (bool)GetValue(UseAntialiasProperty);
            set => SetValue(UseAntialiasProperty, value);
        }

        public bool ShowAxis
        {
            get => (bool)GetValue(ShowAxisProperty);
            set => SetValue(ShowAxisProperty, value);
        }

        public Thickness AxisPadding
        {
            get => (Thickness)GetValue(AxisPaddingProperty);
            set => SetValue(AxisPaddingProperty, value);
        }

        public double LabelFontSize
        {
            get => (double)GetValue(LabelFontSizeProperty);
            set => SetValue(LabelFontSizeProperty, value);
        }

        public Brush LabelBrush
        {
            get => (Brush)GetValue(LabelBrushProperty);
            set => SetValue(LabelBrushProperty, value);
        }

        public Pen AxisPen
        {
            get => (Pen)GetValue(AxisPenProperty);
            set => SetValue(AxisPenProperty, value);
        }

        // ---- Public API ----
        public void SetData(float[] data)
        {
            _data = data;
            InvalidateVisual();
        }

        // ---- Rendering ----
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            var full = new Rect(0, 0, ActualWidth, ActualHeight);
            if (full.Width <= 1 || full.Height <= 1)
            {
                return;
            }

            var plot = full;
            if (ShowAxis)
            {
                var pad = AxisPadding;
                plot = new Rect(
                    full.Left + pad.Left,
                    full.Top + pad.Top,
                    Math.Max(1, full.Width - pad.Left - pad.Right),
                    Math.Max(1, full.Height - pad.Top - pad.Bottom));
            }

            if (ShowAxis)
                DrawAxis(dc, full, plot);

            var data = _data;
            if (data == null || data.Length < 2)
                return;

            double minY = MinY;
            double maxY = MaxY;
            if (maxY <= minY) maxY = minY + 1e-6;

            int wPx = (int)Math.Max(1, Math.Round(plot.Width));
            int hPx = (int)Math.Max(1, Math.Round(plot.Height));

            // Prepare data for GPU rendering
            float[] renderData = PrepareRenderData(data, out float effectiveMinY, out float effectiveMaxY);
            if (renderData == null || renderData.Length < 2)
                return;

            // Extract line color from brush
            Color lineColor = GetColorFromBrush(LineBrush);

            // Ensure pixel buffer is allocated
            EnsurePixelBuffer(wPx, hPx);

            // Clear pixel buffer
            Array.Clear(_pixelBuffer, 0, _pixelBuffer.Length);

            // GPU-accelerated rendering
            _gpuRenderer.RenderLineChart(
                renderData.AsSpan(),
                _pixelBuffer.AsSpan(),
                wPx,
                hPx,
                effectiveMinY,
                effectiveMaxY,
                lineColor.R,
                lineColor.G,
                lineColor.B,
                (float)LineThickness);

            // Create bitmap from pixel buffer and draw
            var bitmap = CreateBitmapFromPixels(wPx, hPx);
            if (bitmap != null)
            {
                dc.PushClip(new RectangleGeometry(plot));
                dc.DrawImage(bitmap, plot);
                dc.Pop();
            }
        }

        private float[] PrepareRenderData(float[] data, out float minY, out float maxY)
        {
            minY = (float)MinY;
            maxY = (float)MaxY;
            if (maxY <= minY) maxY = minY + 1e-6f;

            int n = data.Length;
            var mode = ScaleMode;

            int i0 = 0;
            int i1 = n - 1;

            if (SpectralMode)
            {
                if (TryGetBinRange(n, out int b0, out int b1))
                {
                    i0 = b0;
                    i1 = b1;
                }
            }

            int length = i1 - i0 + 1;
            if (length < 2)
                return null;

            float[] result = new float[length];

            if (mode == YScaleMode.Linear)
            {
                for (int i = 0; i < length; i++)
                {
                    float v = data[i0 + i];
                    if (float.IsNaN(v) || float.IsInfinity(v))
                        v = minY;
                    result[i] = v;
                }
            }
            else
            {
                double eps = LogEpsilon;
                if (!(eps > 0)) eps = 1e-12;

                float logEps = (float)Math.Log10(eps);
                if (minY < logEps) minY = logEps;
                if (maxY < minY + 1e-6f) maxY = minY + 1e-6f;

                for (int i = 0; i < length; i++)
                {
                    float v0 = data[i0 + i];
                    if (float.IsNaN(v0) || float.IsInfinity(v0))
                        v0 = (float)eps;

                    double vv = v0;
                    if (vv < eps) vv = eps;
                    result[i] = (float)Math.Log10(vv);
                }
            }

            return result;
        }

        private void EnsurePixelBuffer(int width, int height)
        {
            int requiredSize = width * height * 4;
            if (_pixelBuffer == null || _pixelBuffer.Length != requiredSize)
            {
                _pixelBuffer = new byte[requiredSize];
            }
        }

        private WriteableBitmap CreateBitmapFromPixels(int width, int height)
        {
            if (_pixelBuffer == null || width <= 0 || height <= 0)
                return null;

            var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            bitmap.WritePixels(new Int32Rect(0, 0, width, height), _pixelBuffer, width * 4, 0);
            return bitmap;
        }

        private static Color GetColorFromBrush(Brush brush)
        {
            if (brush is SolidColorBrush scb)
                return scb.Color;
            return Colors.CornflowerBlue;
        }

        private bool TryGetBinRange(int dataLength, out int bin0, out int bin1)
        {
            bin0 = 0;
            bin1 = dataLength - 1;

            double sr = SampleRate;
            int fft = FftLength;

            if (!(sr > 0) || fft <= 0) return false;

            int nyquistBins = (fft / 2) + 1; // includes DC..Nyquist
            if (dataLength <= 1) return false;

            // If caller provides only magnitude bins, it might be fft/2+1 or dataLength.
            int bins = dataLength;
            double hzPerBin = (sr * 0.5) / Math.Max(1, (bins - 1));

            double minHz = MinHz;
            double maxHz = MaxHz;
            if (maxHz < minHz) (minHz, maxHz) = (maxHz, minHz);

            minHz = Math.Max(0.0, minHz);
            maxHz = Math.Min(sr * 0.5, maxHz);

            int b0 = (int)Math.Floor(minHz / hzPerBin);
            int b1 = (int)Math.Ceiling(maxHz / hzPerBin);

            if (b0 < 0) b0 = 0;
            if (b1 >= bins) b1 = bins - 1;

            if (b1 <= b0) return false;

            bin0 = b0;
            bin1 = b1;
            return true;
        }

        private void DrawAxis(DrawingContext dc, Rect full, Rect plot)
        {
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            double sLeft = Snap(plot.Left);
            double sRight = Snap(plot.Right);
            double sTop = Snap(plot.Top);
            double sBottom = Snap(plot.Bottom);

            var axisPen = AxisPen;
            if (axisPen != null && !axisPen.IsFrozen) axisPen.Freeze();

            dc.DrawLine(axisPen, new Point(sLeft, sTop), new Point(sLeft, sBottom));
            dc.DrawLine(axisPen, new Point(sLeft, sBottom), new Point(sRight, sBottom));

            FormattedText FT(string text) =>
                new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), LabelFontSize, LabelBrush, dpi);

            string FormatLinear(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

            string FormatLog10Label(double log10v)
            {
                double v = Math.Pow(10.0, log10v);
                if (v >= 1e-3 && v <= 1e6)
                    return v.ToString("0.###", CultureInfo.InvariantCulture);

                int exp = (int)Math.Round(log10v);
                return "1e" + exp.ToString(CultureInfo.InvariantCulture);
            }

            string topText, botText;
            if (ScaleMode == YScaleMode.Linear)
            {
                topText = FormatLinear(MaxY);
                botText = FormatLinear(MinY);
            }
            else
            {
                double eps = LogEpsilon;
                if (!(eps > 0)) eps = 1e-12;

                double logEps = Math.Log10(eps);
                double minY = MinY < logEps ? logEps : MinY;
                double maxY = MaxY < minY + 1e-6 ? minY + 1e-6 : MaxY;

                topText = FormatLog10Label(maxY);
                botText = FormatLog10Label(minY);
            }

            var yMaxTxt = FT(topText);
            var yMinTxt = FT(botText);

            const double xOffset = 4.0;
            dc.DrawText(yMaxTxt, new Point(sLeft - xOffset - yMaxTxt.Width, sTop - (yMaxTxt.Height * 0.4)));
            dc.DrawText(yMinTxt, new Point(sLeft - xOffset - yMinTxt.Width, sBottom - yMinTxt.Height * 0.6));

            if (SpectralMode)
            {
                string hzL = $"{MinHz:0.#} Hz";
                string hzR = $"{MaxHz:0.#} Hz";
                var tL = FT(hzL);
                var tR = FT(hzR);

                dc.DrawText(tL, new Point(plot.Left, sBottom + 2));
                dc.DrawText(tR, new Point(plot.Right - tR.Width, sBottom + 2));
            }
        }

        // ---- Style / Perf helpers ----
        private static void OnLineStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (FastLineChartControl)d;
            c.UpdateLinePen();
            c.InvalidateVisual();
        }

        private void UpdateLinePen()
        {
            var brush = LineBrush ?? Brushes.CornflowerBlue;
            var pen = new Pen(brush, Math.Max(0.5, LineThickness))
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                MiterLimit = 1.0
            };
            pen.Freeze();
            _linePen = pen;
        }

        private static void OnUseAAChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (FastLineChartControl)d;
            c.ApplyAA();
            c.InvalidateVisual();
        }

        private void ApplyAA()
        {
            if (_useAAApplied == UseAntialias) return;
            RenderOptions.SetEdgeMode(this, UseAntialias ? EdgeMode.Unspecified : EdgeMode.Aliased);
            RenderOptions.SetBitmapScalingMode(this, UseAntialias ? BitmapScalingMode.Linear : BitmapScalingMode.NearestNeighbor);
            _useAAApplied = UseAntialias;
        }

        private void EnsureScratch(int widthPx)
        {
            if (_colMin != null && _colMin.Length == widthPx) return;

            _colMin = new float[widthPx];
            _colMax = new float[widthPx];
            _colHas = new byte[widthPx];
        }

        private static double Snap(double v) => Math.Round(v) + 0.5;

        // ---- Fields ----
        private float[] _data;
        private byte[] _pixelBuffer;

        private Pen _linePen = new Pen(Brushes.CornflowerBlue, 2.0);
        private bool _useAAApplied;

        private float[] _colMin;
        private float[] _colMax;
        private byte[] _colHas;
    }
}
