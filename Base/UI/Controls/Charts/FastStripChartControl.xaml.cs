// :contentReference[oaicite:0]{index=0}
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Base.Components.Chart
{
    /// <summary>
    /// A fast strip chart control that uses GPU-accelerated rendering via ComputeSharp.
    /// Throws <see cref="GpuNotAvailableException"/> if no compatible GPU is available.
    /// </summary>
    public sealed partial class FastStripChartControl : UserControl, IDisposable
    {
        private readonly GpuChartRenderer _gpuRenderer;

        /// <summary>
        /// Initializes a new instance of the <see cref="FastStripChartControl"/> class.
        /// </summary>
        /// <exception cref="GpuNotAvailableException">Thrown when no compatible GPU is available.</exception>
        public FastStripChartControl()
        {
            InitializeComponent();

            // Ensure GPU is available - throws GpuNotAvailableException if not
            GpuChartRenderer.EnsureGpuAvailable();
            _gpuRenderer = GpuChartRenderer.Instance;

            UseLayoutRounding = false;
            SnapsToDevicePixels = false;

            _linePen = new Pen(LineBrush, 2.0)
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                MiterLimit = 1.0
            };
            _linePen.Freeze();

            _capacity = 1024;
            _buffer = new Sample[_capacity];
            _working = new Sample[_capacity];

            SetCurrentValue(MinYProperty, 0.0);
            SetCurrentValue(MaxYProperty, 1.0);
            SetCurrentValue(UpdateFpsProperty, 60);
            SetCurrentValue(TimeWindowProperty, 3000);

            _renderTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(1000.0 / UpdateFps)
            };
            _renderTimer.Tick += (_, __) =>
            {
                if (_dirty)
                {
                    _dirty = false;
                    InvalidateVisual();
                }
            };
        }

        // ---- Sample ----
        private struct Sample
        {
            public long Tick;
            public float Value;
        }

        // ---- Dependency Properties ----
        public static readonly DependencyProperty CapacityProperty =
            DependencyProperty.Register(nameof(Capacity), typeof(int), typeof(FastStripChartControl),
                new FrameworkPropertyMetadata(1024, FrameworkPropertyMetadataOptions.AffectsRender, OnCapacityChanged));

        public static readonly DependencyProperty AxisYLabelCountProperty =
            DependencyProperty.Register(nameof(AxisYLabelCount), typeof(int), typeof(FastStripChartControl),
                new FrameworkPropertyMetadata(8, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MinYProperty =
            DependencyProperty.Register(nameof(MinY), typeof(double), typeof(FastStripChartControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MaxYProperty =
            DependencyProperty.Register(nameof(MaxY), typeof(double), typeof(FastStripChartControl),
                new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty UpdateFpsProperty =
            DependencyProperty.Register(nameof(UpdateFps), typeof(int), typeof(FastStripChartControl),
                new FrameworkPropertyMetadata(60, OnUpdateFpsChanged));

        public static readonly DependencyProperty LineBrushProperty =
            DependencyProperty.Register(nameof(LineBrush), typeof(Brush), typeof(FastStripChartControl),
                new FrameworkPropertyMetadata(Brushes.CornflowerBlue, FrameworkPropertyMetadataOptions.AffectsRender, OnLineStyleChanged));

        public static readonly DependencyProperty LineThicknessProperty =
            DependencyProperty.Register(nameof(LineThickness), typeof(double), typeof(FastStripChartControl),
                new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender, OnLineStyleChanged));

        public static readonly DependencyProperty DotBrushProperty =
            DependencyProperty.Register(nameof(DotBrush), typeof(Brush), typeof(FastStripChartControl),
                new FrameworkPropertyMetadata(Brushes.DarkOrange, FrameworkPropertyMetadataOptions.AffectsRender, OnDotStyleChanged));

        public static readonly DependencyProperty DotThicknessProperty =
            DependencyProperty.Register(nameof(DotThickness), typeof(double), typeof(FastStripChartControl),
                new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsRender, OnDotStyleChanged));

        public static readonly DependencyProperty UseAntialiasProperty =
            DependencyProperty.Register(nameof(UseAntialias), typeof(bool), typeof(FastStripChartControl),
                new FrameworkPropertyMetadata(false, OnUseAAChanged));

        public static readonly DependencyProperty TimeWindowProperty =
            DependencyProperty.Register(nameof(TimeWindow), typeof(int), typeof(FastStripChartControl),
                new FrameworkPropertyMetadata(3000, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty AutoFitProperty =
            DependencyProperty.Register(nameof(AutoFit), typeof(bool), typeof(FastStripChartControl),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ShowAxisProperty =
        DependencyProperty.Register(nameof(ShowAxis), typeof(bool), typeof(FastStripChartControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty AxisPaddingProperty =
            DependencyProperty.Register(nameof(AxisPadding), typeof(Thickness), typeof(FastStripChartControl),
                new FrameworkPropertyMetadata(new Thickness(48, 8, 8, 24), FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LabelFontSizeProperty =
            DependencyProperty.Register(nameof(LabelFontSize), typeof(double), typeof(FastStripChartControl),
                new FrameworkPropertyMetadata(12d, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LabelBrushProperty =
            DependencyProperty.Register(nameof(LabelBrush), typeof(Brush), typeof(FastStripChartControl),
                new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty AxisPenProperty =
            DependencyProperty.Register(nameof(AxisPen), typeof(Pen), typeof(FastStripChartControl),
                new FrameworkPropertyMetadata(new Pen(Brushes.Gray, 1), FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MarkerPenProperty =
            DependencyProperty.Register(nameof(MarkerPen), typeof(Pen), typeof(FastStripChartControl),
                new FrameworkPropertyMetadata(new Pen(Brushes.Gray, 2), FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty RenderModeProperty =
            DependencyProperty.Register(nameof(RenderMode), typeof(ChartRenderMode), typeof(FastStripChartControl),
                new FrameworkPropertyMetadata(ChartRenderMode.Line, FrameworkPropertyMetadataOptions.AffectsRender));

        public bool AutoFit
        {
            get => (bool)GetValue(AutoFitProperty);
            set => SetValue(AutoFitProperty, value);
        }

        public int Capacity
        {
            get => (int)GetValue(CapacityProperty);
            set => SetValue(CapacityProperty, value);
        }
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
        public int AxisYLabelCount
        {
            get => (int)GetValue(AxisYLabelCountProperty);
            set => SetValue(AxisYLabelCountProperty, value);
        }
        public int UpdateFps
        {
            get => (int)GetValue(UpdateFpsProperty);
            set => SetValue(UpdateFpsProperty, value);
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
        public Brush DotBrush
        {
            get => (Brush)GetValue(DotBrushProperty);
            set => SetValue(DotBrushProperty, value);
        }
        public double DotThickness
        {
            get => (double)GetValue(DotThicknessProperty);
            set => SetValue(DotThicknessProperty, value);
        }
        public bool UseAntialias
        {
            get => (bool)GetValue(UseAntialiasProperty);
            set => SetValue(UseAntialiasProperty, value);
        }
        public int TimeWindow
        {
            get => (int)GetValue(TimeWindowProperty);
            set => SetValue(TimeWindowProperty, value);
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
        public Pen MarkerPen
        {
            get => (Pen)GetValue(MarkerPenProperty);
            set => SetValue(MarkerPenProperty, value);
        }
        public ChartRenderMode RenderMode
        {
            get => (ChartRenderMode)GetValue(RenderModeProperty);
            set => SetValue(RenderModeProperty, value);
        }

        private static void OnCapacityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (FastStripChartControl)d;
            int val = Math.Max(2, (int)e.NewValue);

            // set cap to nearest power of two for efficiency
            val = NextPowerOfTwo(val);

            lock (c._lock)
            {
                c._capacity = val;
                c._buffer = new Sample[val];
                c._working = new Sample[val];
                c._count = 0;
                c._head = -1;
                c._lastTick = long.MinValue;
            }
            c._dirty = true;
            c.InvalidateVisual();
        }

        private static int NextPowerOfTwo(int value)
        {
            if (value < 1)
                return 1;

            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            value++;

            return value;
        }

        private static void OnUpdateFpsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (FastStripChartControl)d;
            var v = Math.Max(1, Math.Min(240, (int)e.NewValue));
            c._updateFps = v;
            if (c._isRunning) c.RestartTimer();
        }

        private static void OnLineStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (FastStripChartControl)d;
            var brush = c.LineBrush ?? Brushes.LawnGreen;
            var pen = new Pen(brush, Math.Max(0.5, c.LineThickness))
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                MiterLimit = 1.0
            };
            pen.Freeze();
            c._linePen = pen;
            c.InvalidateVisual();
        }

        private static void OnDotStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (FastStripChartControl)d;
            c.InvalidateVisual();
        }

        private static void OnUseAAChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (FastStripChartControl)d;
            c.ApplyAA();
        }

        // ---- Public API ----
        public void Start()
        {
            if (_isRunning) return;
            _isRunning = true;
            RestartTimer();
            CompositionTarget.Rendering += OnVSync;
        }

        public void Stop()
        {
            if (!_isRunning) return;
            _isRunning = false;
            _renderTimer.Stop();
            CompositionTarget.Rendering -= OnVSync;
        }

        public void Tick(long tick)
        {
            lock (_lock)
            {
                lastTick = tick;
                _dirty = true;
            }
        }

        public void AddMagnitudeMarker(Marker marker)
        {
            lock (_lock)
            {
                magnitudeMarkers.Add(marker);
                _dirty = true;
            }
        }

        public void AddMagnitudeMarker(float y, bool showLabel, Brush brush = null)
        {
            brush ??= Brushes.Black;
            lock (_lock)
            {
                magnitudeMarkers.Add(new Marker(y, brush, showLabel));
                _dirty = true;
            }
        }

        public void ClearMagnitudeMarkers()
        {
            lock (_lock)
            {
                magnitudeMarkers.Clear();
                _dirty = true;
            }
        }

        // Append with explicit DateTime ticks (100ns units). Out-of-order ticks are ignored.
        public void AddSample(float y, long tick)
        {
            if (float.IsNaN(y) || float.IsInfinity(y)) return;

            // enforce monotonic (non-decreasing) wall clock on UI stream
            long prev = Interlocked.Read(ref _lastTick);
            if (tick <= prev) return;
            Interlocked.Exchange(ref _lastTick, tick);

            lock (_lock)
            {
                int cap = _buffer.Length;
                _head = (_head + 1) & (cap - 1);
                lastTick = tick;
                _buffer[_head].Tick = tick;
                _buffer[_head].Value = y;
                if (_count < cap) _count++;
                _dirty = true;
            }
        }

        // Overload using UtcNow if you need a quick call-site
        public void AddSample(float y) => AddSample(y, DateTime.UtcNow.Ticks);

        public void Clear()
        {
            lock (_lock)
            {
                Array.Clear(_buffer, 0, _buffer.Length);
                _count = 0; _head = -1; _lastTick = long.MinValue;
                _dirty = true;
            }
        }

        // ---- Rendering ----
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            var full = new Rect(0, 0, ActualWidth, ActualHeight);
            if (full.Width <= 1 || full.Height <= 1) return;

            // Plot area (inside axis padding)
            var plot = full;

            dc.DrawRectangle(Background ?? Brushes.Transparent, null, full);

            int count, head, cap;
            Sample[] snapshot;
            lock (_lock)
            {
                cap = _buffer.Length;
                if (_working == null || _working.Length != cap) _working = new Sample[cap];
                Array.Copy(_buffer, 0, _working, 0, cap);
                count = Math.Min(_count, cap);
                head = (_head < 0) ? -1 : Math.Min(_head, cap - 1);
                snapshot = _working;
            }

            double usedMinY = MinY, usedMaxY = MaxY;
            long startTick;
            long windowTicks = TimeSpan.FromMilliseconds(TimeWindow).Ticks;
            if (windowTicks <= 0) windowTicks = TimeSpan.FromSeconds(1).Ticks;
            startTick = lastTick - windowTicks;

            if (count >= 2 && head >= 0)
            {
                if (AutoFit)
                {
                    ComputeAutoRange(snapshot, head, count, startTick, lastTick, out usedMinY, out usedMaxY);
                }
                else
                {
                    usedMinY = MinY;
                    usedMaxY = MaxY;
                    if (usedMaxY - usedMinY <= 0)
                    {
                        usedMaxY = usedMinY + 0.01;
                        usedMinY = usedMinY - 0.01;
                    }
                }
            }

            if (ShowAxis)
            {
                DrawAxis(dc, plot, usedMinY, usedMaxY);
            }

            // GPU-accelerated chart rendering
            if (count >= 2 && head >= 0)
            {
                int wPx = (int)Math.Max(1, Math.Round(plot.Width));
                int hPx = (int)Math.Max(1, Math.Round(plot.Height));

                // Prepare data for GPU rendering
                PrepareGpuRenderData(snapshot, head, count, startTick, lastTick, out long[] ticks, out float[] values);

                if (ticks != null && ticks.Length > 0)
                {
                    // Extract line color from brush
                    Color lineColor = GetColorFromBrush(LineBrush);
                    Color dotColor = GetColorFromBrush(DotBrush ?? LineBrush);

                    // Ensure pixel buffer is allocated
                    EnsurePixelBuffer(wPx, hPx);

                    bool renderLines = RenderMode == ChartRenderMode.Line || RenderMode == ChartRenderMode.Combined;
                    bool renderDots = RenderMode == ChartRenderMode.Dot || RenderMode == ChartRenderMode.Combined;

                    // GPU-accelerated rendering with separate line and dot colors
                    _gpuRenderer.RenderStripChart(
                        ticks.AsSpan(),
                        values.AsSpan(),
                        _pixelBuffer.AsSpan(),
                        wPx,
                        hPx,
                        startTick,
                        lastTick,
                        (float)usedMinY,
                        (float)usedMaxY,
                        lineColor.R,
                        lineColor.G,
                        lineColor.B,
                        (float)LineThickness,
                        renderLines,
                        renderDots,
                        dotColor.R,
                        dotColor.G,
                        dotColor.B,
                        (float)(DotThickness * 0.5));

                    // Create bitmap from pixel buffer and draw
                    var bitmap = CreateBitmapFromPixels(wPx, hPx);
                    if (bitmap != null)
                    {
                        dc.PushClip(new RectangleGeometry(plot));
                        dc.DrawImage(bitmap, plot);
                        dc.Pop();
                    }
                }
            }
        }

        private void DrawAxis(DrawingContext dc, Rect plot, double usedMinY, double usedMaxY)
        {
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            // Snap coordinates to half-pixels for crisp 1px lines
            double sLeft = Snap(plot.Left);
            double sRight = Snap(plot.Right);
            double sTop = Snap(plot.Top);
            double sBottom = Snap(plot.Bottom);

            // Axes
            dc.DrawLine(AxisPen, new Point(sLeft, sTop), new Point(sLeft, sBottom));     // Y axis (left)
            dc.DrawLine(AxisPen, new Point(sLeft, sBottom), new Point(sRight, sBottom));  // X axis (bottom)

            // Labels should reflect the Y range actually used (AutoFit or fixed)
            var yLabelMin = usedMinY;
            var yLabelMax = usedMaxY;

            FormattedText FT(string text) =>
                new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), LabelFontSize, LabelBrush, dpi);

            // Y labels: Max at top-left, Min at bottom-left
            var yMaxTxt = FT(yLabelMax.ToString("0.#"));
            var yMinTxt = FT(yLabelMin.ToString("0.#"));

            // Y ticks intermediate
            int yLabelCount = AxisYLabelCount; // Number of labels between min and max
            if (yLabelCount > 0)
            {
                double step = (usedMaxY - usedMinY) / yLabelCount;
                for (int i = 1; i < yLabelCount; i++)
                {
                    double yTickValue = usedMinY + i * step;
                    var yTickTxt = FT(yTickValue.ToString("0.#"));
                    dc.DrawText(yTickTxt, new Point(sLeft - yTickTxt.Width - 4, sBottom - ((yTickValue - usedMinY) / (usedMaxY - usedMinY)) * (sBottom - sTop) - yTickTxt.Height * 0.5));
                }
            }

            //- Draw Markers
            foreach (Marker marker in magnitudeMarkers)
            {
                if (marker.Value < usedMinY || marker.Value > usedMaxY) continue;

                double yTickPos = sBottom - ((marker.Value - usedMinY) / (usedMaxY - usedMinY)) * (sBottom - sTop);
                if (marker.ShowLabel)
                {
                    var yTickTxt = FT(marker.Value.ToString("0.#"));
                    dc.DrawText(yTickTxt, new Point(sLeft - yTickTxt.Width - 4, yTickPos - yTickTxt.Height * 0.5));
                }
                dc.DrawLine(AxisPen, new Point(sLeft, yTickPos), new Point(sRight, yTickPos));
            }

            // A small offset from the axis line
            const double xOffset = 4.0;

            dc.DrawText(yMaxTxt, new Point(sLeft - xOffset - yMaxTxt.Width, sTop - (yMaxTxt.Height * 0.4)));
            dc.DrawText(yMinTxt, new Point(sLeft - xOffset - yMinTxt.Width, sBottom - yMinTxt.Height * 0.6));

            TimeSpan span = TimeSpan.FromMilliseconds(TimeWindow);

            // X labels: 0 at left-bottom, TimeWindow at right-bottom
            string xMaxLabel = "0";
            string xMinLabel = span.TotalSeconds >= 1
                ? $"-{span.TotalSeconds:0.#}s"
                : $"-{span.TotalMilliseconds:0}ms";

            var xMinTxt = FT(xMinLabel);
            var xMaxTxt = FT(xMaxLabel);

            const double yOffset = 2.0;

            dc.DrawText(xMinTxt, new Point(sLeft, sBottom + yOffset));
            dc.DrawText(xMaxTxt, new Point(sRight - xMaxTxt.Width, sBottom + yOffset));

            // Optional small tick marks at min/max points
            const double tickLen = 6.0;

            // Y ticks
            dc.DrawLine(AxisPen, new Point(sLeft - tickLen, sTop), new Point(sRight, sTop));         // MaxY tick
            dc.DrawLine(AxisPen, new Point(sLeft - tickLen, sBottom), new Point(sLeft, sBottom));   // MinY tick

            // Y ticks intermediate
            int tickCount = AxisYLabelCount; // Number of ticks between min and max
            if (tickCount > 0)
            {
                double step = (usedMaxY - usedMinY) / tickCount;
                for (int i = 1; i < tickCount; i++)
                {
                    double yTickValue = usedMinY + i * step;
                    double yTickPos = sBottom - ((yTickValue - usedMinY) / (usedMaxY - usedMinY)) * (sBottom - sTop);
                    dc.DrawLine(AxisPen, new Point(sLeft - tickLen, yTickPos), new Point(sRight, yTickPos));
                }
            }

            // X ticks
            dc.DrawLine(AxisPen, new Point(sLeft, sBottom), new Point(sLeft, sBottom + tickLen));   // 0 tick
            dc.DrawLine(AxisPen, new Point(sRight, sBottom), new Point(sRight, sBottom + tickLen)); // MaxX tick
        }

        private void PrepareGpuRenderData(Sample[] buf, int head, int count, long startTick, long endTick, out long[] ticks, out float[] values)
        {
            int cap = buf.Length;
            var tickList = new List<long>(count);
            var valueList = new List<float>(count);

            int startIndex = head - count + 1;
            if (startIndex < 0) startIndex += cap * ((-startIndex / cap) + 1);
            startIndex %= cap;

            for (int i = 0; i < count; i++)
            {
                int bi = (startIndex + i) % cap;
                var s = buf[bi];
                if (s.Tick >= startTick && s.Tick <= endTick)
                {
                    tickList.Add(s.Tick);
                    valueList.Add(s.Value);
                }
            }

            ticks = tickList.ToArray();
            values = valueList.ToArray();
        }

        private void EnsurePixelBuffer(int width, int height)
        {
            int requiredSize = width * height * 4;
            if (_pixelBuffer == null || _pixelBuffer.Length != requiredSize)
            {
                _pixelBuffer = new byte[requiredSize];
            }
            // Clear pixel buffer for each render
            Array.Clear(_pixelBuffer, 0, _pixelBuffer.Length);
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

        private void ComputeAutoRange(Sample[] buf, int head, int count, long startTick, long lastTick, out double minY, out double maxY)
        {
            int cap = buf.Length;
            double localMin = double.PositiveInfinity;
            double localMax = double.NegativeInfinity;

            int scanStart = head - count + 1;
            if (scanStart < 0) scanStart += cap * ((-scanStart / cap) + 1);
            scanStart %= cap;

            for (int i = 0; i < count; i++)
            {
                int bi = (scanStart + i) % cap;
                var s = buf[bi];
                if (s.Tick < startTick || s.Tick > lastTick) continue;
                if (s.Value < localMin) localMin = s.Value;
                if (s.Value > localMax) localMax = s.Value;
            }

            if (double.IsInfinity(localMin) || double.IsInfinity(localMax))
            {
                localMin = 0; localMax = 1;
            }
            if (Math.Abs(localMax - localMin) < 1e-9)
            {
                localMax = localMin + 0.01;
                localMin = localMin - 0.01;
            }

            _autoMinY = localMin;
            _autoMaxY = localMax;

            minY = _autoMinY;
            maxY = _autoMaxY;
        }

        private StreamGeometry BuildGeometry(Sample[] buf, int head, int count, Rect plot, long startTick, long lastTick, double minY, double maxY)
        {
            double w = plot.Width;
            double h = plot.Height;
            if (w <= 0 || h <= 0) return null;

            int cap = buf.Length;

            double yRange = maxY - minY;
            if (yRange <= 0) yRange = 1; // final guard

            var sg = new StreamGeometry { FillRule = FillRule.EvenOdd };
            using (var ctx = sg.Open())
            {
                int startIndex = head - count + 1;
                if (startIndex < 0) startIndex += cap * ((-startIndex / cap) + 1);
                startIndex %= cap;

                int i = 0;
                for (; i < count; i++)
                {
                    int bi = (startIndex + i) % cap;
                    if (buf[bi].Tick >= startTick) break;
                }
                if (i >= count) { sg.Freeze(); return sg; }

                {
                    int bi = (startIndex + i) % cap;
                    double x = TickToX(buf[bi].Tick, startTick, lastTick, plot.Left, plot.Right);
                    double y = plot.Top + h - ((buf[bi].Value - minY) / yRange) * h; // use minY
                    if (double.IsNaN(y)) y = plot.Bottom;
                    ctx.BeginFigure(new Point(x, y), isFilled: false, isClosed: false);
                }

                for (i = i + 1; i < count; i++)
                {
                    int bi = (startIndex + i) % cap;
                    var t = buf[bi].Tick;
                    if (t < startTick) continue;
                    if (t > lastTick) break;

                    double x = TickToX(t, startTick, lastTick, plot.Left, plot.Right);
                    double y = plot.Top + h - ((buf[bi].Value - minY) / yRange) * h; // use minY
                    if (double.IsNaN(y)) continue;
                    ctx.LineTo(new Point(x, y), true, false);
                }
            }
            sg.Freeze();
            return sg;
        }

        private static double TickToX(long tick, long startTick, long endTick, double left, double right)
        {
            double span = Math.Max(1.0, (double)(endTick - startTick));
            double norm = (tick - startTick) / span; // 0..1
            if (norm < 0) norm = 0; else if (norm > 1) norm = 1;
            return left + norm * (right - left);
        }

        private static double Snap(double v) => Math.Round(v) + 0.5;
        private void OnVSync(object sender, EventArgs e) => ApplyAA();

        private void ApplyAA()
        {
            if (_useAAApplied == UseAntialias) return;
            RenderOptions.SetEdgeMode(this, UseAntialias ? EdgeMode.Unspecified : EdgeMode.Aliased);
            RenderOptions.SetBitmapScalingMode(this, UseAntialias ? BitmapScalingMode.Linear : BitmapScalingMode.NearestNeighbor);
            _useAAApplied = UseAntialias;
        }

        private void RestartTimer()
        {
            _renderTimer.Stop();
            _renderTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, _updateFps));
            if (_isRunning) _renderTimer.Start();
        }

        // ---- IDisposable ----
        public void Dispose() => Stop();

        // ---- Fields ----
        private int _capacity;
        private Sample[] _buffer = Array.Empty<Sample>();
        private Sample[] _working = Array.Empty<Sample>();
        private long lastTick = 0;
        private int _count;
        private int _head = -1;
        private long _lastTick = long.MinValue;
        private readonly object _lock = new();
        private readonly List<Marker> magnitudeMarkers = new();
        private byte[] _pixelBuffer;

        private DispatcherTimer _renderTimer = null!;
        private bool _dirty;
        private bool _isRunning;
        private int _updateFps = 60;

        private Pen _linePen = null!;
        private bool _useAAApplied = false;

        private double _autoMinY;
        private double _autoMaxY;

        public class Marker(float value, Brush brush, bool showLabel)
        {
            public float Value { get; set; } = value;
            public Brush Brush { get; set; } = brush;
            public bool ShowLabel { get; set; } = showLabel;
        }
    }
}
