using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Audio.Components
{
    public sealed class VolumeMeter : FrameworkElement
    {
        // ===== Dependency Properties =====

        public static readonly DependencyProperty LevelDbProperty =
            DependencyProperty.Register(
                nameof(LevelDb),
                typeof(double),
                typeof(VolumeMeter),
                new FrameworkPropertyMetadata(-60.0, FrameworkPropertyMetadataOptions.AffectsRender, OnLevelChanged));

        public double LevelDb
        {
            get => (double)GetValue(LevelDbProperty);
            set => SetValue(LevelDbProperty, value);
        }

        public static readonly DependencyProperty MinDbProperty =
            DependencyProperty.Register(
                nameof(MinDb),
                typeof(double),
                typeof(VolumeMeter),
                new FrameworkPropertyMetadata(-60.0, FrameworkPropertyMetadataOptions.AffectsRender, OnRangeChanged));

        public double MinDb
        {
            get => (double)GetValue(MinDbProperty);
            set => SetValue(MinDbProperty, value);
        }

        public static readonly DependencyProperty MaxDbProperty =
            DependencyProperty.Register(
                nameof(MaxDb),
                typeof(double),
                typeof(VolumeMeter),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender, OnRangeChanged));

        public double MaxDb
        {
            get => (double)GetValue(MaxDbProperty);
            set => SetValue(MaxDbProperty, value);
        }

        public static readonly DependencyProperty SegmentCountProperty =
            DependencyProperty.Register(
                nameof(SegmentCount),
                typeof(int),
                typeof(VolumeMeter),
                new FrameworkPropertyMetadata(30, FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutAffectingChanged),
                v => (int)v >= 4 && (int)v <= 200);

        public int SegmentCount
        {
            get => (int)GetValue(SegmentCountProperty);
            set => SetValue(SegmentCountProperty, value);
        }

        public static readonly DependencyProperty SegmentSpacingProperty =
            DependencyProperty.Register(
                nameof(SegmentSpacing),
                typeof(double),
                typeof(VolumeMeter),
                new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutAffectingChanged),
                v => (double)v >= 0.0 && (double)v <= 10.0);

        public double SegmentSpacing
        {
            get => (double)GetValue(SegmentSpacingProperty);
            set => SetValue(SegmentSpacingProperty, value);
        }

        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register(
                nameof(CornerRadius),
                typeof(double),
                typeof(VolumeMeter),
                new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender),
                v => (double)v >= 0.0 && (double)v <= 20.0);

        public double CornerRadius
        {
            get => (double)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        public static readonly DependencyProperty GreenThresholdDbProperty =
            DependencyProperty.Register(
                nameof(GreenThresholdDb),
                typeof(double),
                typeof(VolumeMeter),
                new FrameworkPropertyMetadata(-12.0, FrameworkPropertyMetadataOptions.AffectsRender, OnThresholdChanged));

        public double GreenThresholdDb
        {
            get => (double)GetValue(GreenThresholdDbProperty);
            set => SetValue(GreenThresholdDbProperty, value);
        }

        public static readonly DependencyProperty YellowThresholdDbProperty =
            DependencyProperty.Register(
                nameof(YellowThresholdDb),
                typeof(double),
                typeof(VolumeMeter),
                new FrameworkPropertyMetadata(-3.0, FrameworkPropertyMetadataOptions.AffectsRender, OnThresholdChanged));

        public double YellowThresholdDb
        {
            get => (double)GetValue(YellowThresholdDbProperty);
            set => SetValue(YellowThresholdDbProperty, value);
        }

        public static readonly DependencyProperty TickStepDbProperty =
            DependencyProperty.Register(
                nameof(TickStepDb),
                typeof(double),
                typeof(VolumeMeter),
                new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsRender),
                v => (double)v > 0.0 && (double)v <= 24.0);

        public double TickStepDb
        {
            get => (double)GetValue(TickStepDbProperty);
            set => SetValue(TickStepDbProperty, value);
        }

        public static readonly DependencyProperty ShowScaleProperty =
            DependencyProperty.Register(
                nameof(ShowScale),
                typeof(bool),
                typeof(VolumeMeter),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutAffectingChanged));

        public bool ShowScale
        {
            get => (bool)GetValue(ShowScaleProperty);
            set => SetValue(ShowScaleProperty, value);
        }

        public static readonly DependencyProperty ShowReadoutProperty =
            DependencyProperty.Register(
                nameof(ShowReadout),
                typeof(bool),
                typeof(VolumeMeter),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender, OnLayoutAffectingChanged));

        public bool ShowReadout
        {
            get => (bool)GetValue(ShowReadoutProperty);
            set => SetValue(ShowReadoutProperty, value);
        }

        public static readonly DependencyProperty UsePeakHoldProperty =
            DependencyProperty.Register(
                nameof(UsePeakHold),
                typeof(bool),
                typeof(VolumeMeter),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender, OnPeakSettingsChanged));

        public bool UsePeakHold
        {
            get => (bool)GetValue(UsePeakHoldProperty);
            set => SetValue(UsePeakHoldProperty, value);
        }

        public static readonly DependencyProperty PeakHoldMillisecondsProperty =
            DependencyProperty.Register(
                nameof(PeakHoldMilliseconds),
                typeof(int),
                typeof(VolumeMeter),
                new FrameworkPropertyMetadata(600, FrameworkPropertyMetadataOptions.AffectsRender),
                v => (int)v >= 0 && (int)v <= 5000);

        public int PeakHoldMilliseconds
        {
            get => (int)GetValue(PeakHoldMillisecondsProperty);
            set => SetValue(PeakHoldMillisecondsProperty, value);
        }

        public static readonly DependencyProperty PeakFalloffDbPerSecondProperty =
            DependencyProperty.Register(
                nameof(PeakFalloffDbPerSecond),
                typeof(double),
                typeof(VolumeMeter),
                new FrameworkPropertyMetadata(18.0, FrameworkPropertyMetadataOptions.AffectsRender),
                v => (double)v >= 0.0 && (double)v <= 200.0);

        public double PeakFalloffDbPerSecond
        {
            get => (double)GetValue(PeakFalloffDbPerSecondProperty);
            set => SetValue(PeakFalloffDbPerSecondProperty, value);
        }

        public static readonly DependencyProperty AttackMillisecondsProperty =
            DependencyProperty.Register(
                nameof(AttackMilliseconds),
                typeof(double),
                typeof(VolumeMeter),
                new FrameworkPropertyMetadata(20.0, FrameworkPropertyMetadataOptions.AffectsRender),
                v => (double)v >= 0.0 && (double)v <= 500.0);

        public double AttackMilliseconds
        {
            get => (double)GetValue(AttackMillisecondsProperty);
            set => SetValue(AttackMillisecondsProperty, value);
        }

        public static readonly DependencyProperty ReleaseMillisecondsProperty =
            DependencyProperty.Register(
                nameof(ReleaseMilliseconds),
                typeof(double),
                typeof(VolumeMeter),
                new FrameworkPropertyMetadata(150.0, FrameworkPropertyMetadataOptions.AffectsRender),
                v => (double)v >= 0.0 && (double)v <= 2000.0);

        public double ReleaseMilliseconds
        {
            get => (double)GetValue(ReleaseMillisecondsProperty);
            set => SetValue(ReleaseMillisecondsProperty, value);
        }

        public static readonly DependencyProperty ActiveGreenBrushProperty =
            DependencyProperty.Register(
                nameof(ActiveGreenBrush),
                typeof(Brush),
                typeof(VolumeMeter),
                new FrameworkPropertyMetadata(Brushes.Lime, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush ActiveGreenBrush
        {
            get => (Brush)GetValue(ActiveGreenBrushProperty);
            set => SetValue(ActiveGreenBrushProperty, value);
        }

        public static readonly DependencyProperty ActiveYellowBrushProperty =
            DependencyProperty.Register(
                nameof(ActiveYellowBrush),
                typeof(Brush),
                typeof(VolumeMeter),
                new FrameworkPropertyMetadata(Brushes.Yellow, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush ActiveYellowBrush
        {
            get => (Brush)GetValue(ActiveYellowBrushProperty);
            set => SetValue(ActiveYellowBrushProperty, value);
        }

        public static readonly DependencyProperty ActiveRedBrushProperty =
            DependencyProperty.Register(
                nameof(ActiveRedBrush),
                typeof(Brush),
                typeof(VolumeMeter),
                new FrameworkPropertyMetadata(Brushes.Red, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush ActiveRedBrush
        {
            get => (Brush)GetValue(ActiveRedBrushProperty);
            set => SetValue(ActiveRedBrushProperty, value);
        }

        public static readonly DependencyProperty InactiveBrushProperty =
            DependencyProperty.Register(
                nameof(InactiveBrush),
                typeof(Brush),
                typeof(VolumeMeter),
                new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(20, 20, 20)), FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush InactiveBrush
        {
            get => (Brush)GetValue(InactiveBrushProperty);
            set => SetValue(InactiveBrushProperty, value);
        }

        public static readonly DependencyProperty FrameBrushProperty =
            DependencyProperty.Register(
                nameof(FrameBrush),
                typeof(Brush),
                typeof(VolumeMeter),
                new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(70, 70, 70)), FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush FrameBrush
        {
            get => (Brush)GetValue(FrameBrushProperty);
            set => SetValue(FrameBrushProperty, value);
        }

        public static readonly DependencyProperty BackgroundBrushProperty =
            DependencyProperty.Register(
                nameof(BackgroundBrush),
                typeof(Brush),
                typeof(VolumeMeter),
                new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush BackgroundBrush
        {
            get => (Brush)GetValue(BackgroundBrushProperty);
            set => SetValue(BackgroundBrushProperty, value);
        }

        public static readonly DependencyProperty PeakLineBrushProperty =
            DependencyProperty.Register(
                nameof(PeakLineBrush),
                typeof(Brush),
                typeof(VolumeMeter),
                new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush PeakLineBrush
        {
            get => (Brush)GetValue(PeakLineBrushProperty);
            set => SetValue(PeakLineBrushProperty, value);
        }

        public static readonly DependencyProperty ScaleTextBrushProperty =
            DependencyProperty.Register(
                nameof(ScaleTextBrush),
                typeof(Brush),
                typeof(VolumeMeter),
                new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(200, 200, 200)), FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush ScaleTextBrush
        {
            get => (Brush)GetValue(ScaleTextBrushProperty);
            set => SetValue(ScaleTextBrushProperty, value);
        }

        public static readonly DependencyProperty ReadoutTextBrushProperty =
            DependencyProperty.Register(
                nameof(ReadoutTextBrush),
                typeof(Brush),
                typeof(VolumeMeter),
                new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(230, 230, 230)), FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush ReadoutTextBrush
        {
            get => (Brush)GetValue(ReadoutTextBrushProperty);
            set => SetValue(ReadoutTextBrushProperty, value);
        }

        // ===== Public API: set volume without bindings =====

        public void SetLevelDb(double db)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetLevelDb(db));
                return;
            }
            LevelDb = db;
        }

        // linear amplitude in [0..1], where 1.0 = 0 dBFS
        public void SetLevelLinear(double linear)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetLevelLinear(linear));
                return;
            }

            if (double.IsNaN(linear) || double.IsInfinity(linear)) linear = 0;
            if (linear <= 0) { LevelDb = MinDb; return; }

            var db = 20.0 * Math.Log10(linear);
            LevelDb = db;
        }

        // RMS power in [0..1], where 1.0 = 0 dBFS (power)
        public void SetLevelPower(double power)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetLevelPower(power));
                return;
            }

            if (double.IsNaN(power) || double.IsInfinity(power)) power = 0;
            if (power <= 0) { LevelDb = MinDb; return; }

            var db = 10.0 * Math.Log10(power);
            LevelDb = db;
        }

        // ===== Internal State =====

        private bool _isRendering;
        private double _targetDb;
        private double _displayDb;
        private double _peakDb;
        private long _peakHoldUntilTicks;
        private long _lastTicks;

        private const double DefaultPadding = 4.0;

        public VolumeMeter()
        {
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;

            _targetDb = -60;
            _displayDb = -60;
            _peakDb = -60;
        }

        // ===== Rendering Loop =====

        private static void OnLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var vm = (VolumeMeter)d;
            vm._targetDb = vm.ClampDb((double)e.NewValue);
            if (vm.UsePeakHold && vm._targetDb > vm._peakDb)
            {
                vm._peakDb = vm._targetDb;
                vm._peakHoldUntilTicks = DateTime.UtcNow.AddMilliseconds(vm.PeakHoldMilliseconds).Ticks;
            }
            vm.EnsureRendering();
            vm.InvalidateVisual();
        }

        private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var vm = (VolumeMeter)d;
            vm._targetDb = vm.ClampDb(vm._targetDb);
            vm._displayDb = vm.ClampDb(vm._displayDb);
            vm._peakDb = vm.ClampDb(vm._peakDb);
            vm.InvalidateVisual();
        }

        private static void OnThresholdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var vm = (VolumeMeter)d;
            vm.InvalidateVisual();
        }

        private static void OnLayoutAffectingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var vm = (VolumeMeter)d;
            vm.InvalidateMeasure();
            vm.InvalidateVisual();
        }

        private static void OnPeakSettingsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var vm = (VolumeMeter)d;
            if (!(bool)e.NewValue)
            {
                vm._peakDb = vm._displayDb;
                vm._peakHoldUntilTicks = 0;
            }
            vm.InvalidateVisual();
        }

        private void EnsureRendering()
        {
            if (_isRendering) return;
            _isRendering = true;
            _lastTicks = DateTime.UtcNow.Ticks;
            CompositionTarget.Rendering += OnRendering;
        }

        private void StopRendering()
        {
            if (!_isRendering) return;
            _isRendering = false;
            CompositionTarget.Rendering -= OnRendering;
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            var now = DateTime.UtcNow.Ticks;
            var dt = TimeSpan.FromTicks(now - _lastTicks).TotalSeconds;
            _lastTicks = now;

            if (dt <= 0) dt = 1.0 / 60.0;

            var prevDisplay = _displayDb;
            var prevPeak = _peakDb;

            // Smooth display
            var diff = _targetDb - _displayDb;
            if (Math.Abs(diff) > 0.0001)
            {
                var isRising = diff > 0;
                var tauMs = isRising ? Math.Max(0.0, AttackMilliseconds) : Math.Max(0.0, ReleaseMilliseconds);

                if (tauMs <= 0.001)
                {
                    _displayDb = _targetDb;
                }
                else
                {
                    var alpha = 1.0 - Math.Exp(-dt / (tauMs / 1000.0));
                    _displayDb = _displayDb + diff * alpha;
                }

                _displayDb = ClampDb(_displayDb);
            }

            // Peak hold + falloff
            if (UsePeakHold)
            {
                if (_targetDb > _peakDb)
                {
                    _peakDb = _targetDb;
                    _peakHoldUntilTicks = DateTime.UtcNow.AddMilliseconds(PeakHoldMilliseconds).Ticks;
                }
                else
                {
                    if (DateTime.UtcNow.Ticks > _peakHoldUntilTicks)
                    {
                        var fall = PeakFalloffDbPerSecond * dt;
                        _peakDb = ClampDb(_peakDb - fall);
                        if (_peakDb < _displayDb) _peakDb = _displayDb;
                    }
                }
            }
            else
            {
                _peakDb = _displayDb;
            }

            InvalidateVisual();

            var displaySettled = Math.Abs(_displayDb - _targetDb) < 0.01;
            var peakSettled = !UsePeakHold || DateTime.UtcNow.Ticks <= _peakHoldUntilTicks || Math.Abs(_peakDb - _displayDb) < 0.01;

            if (displaySettled && peakSettled && Math.Abs(prevDisplay - _displayDb) < 0.0005 && Math.Abs(prevPeak - _peakDb) < 0.0005)
            {
                StopRendering();
            }
        }

        // ===== Layout =====

        protected override Size MeasureOverride(Size availableSize)
        {
            var minW = ShowScale ? 44 : 18;
            var minH = ShowReadout ? 120 : 90;
            return new Size(minW, minH);
        }

        // ===== Drawing =====

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            var w = ActualWidth;
            var h = ActualHeight;
            if (w <= 2 || h <= 2) return;

            dc.DrawRectangle(BackgroundBrush, null, new Rect(0, 0, w, h));

            var framePen = new Pen(FrameBrush, 1.0);
            framePen.Freeze();

            var pad = DefaultPadding;
            var readoutH = ShowReadout ? 22.0 : 0.0;
            var scaleW = ShowScale ? 24.0 : 0.0;

            var inner = new Rect(pad, pad, Math.Max(0, w - pad * 2), Math.Max(0, h - pad * 2));
            if (inner.Width <= 2 || inner.Height <= 2) return;

            var meterRect = new Rect(inner.X + scaleW, inner.Y, Math.Max(0, inner.Width - scaleW), Math.Max(0, inner.Height - readoutH));
            var readoutRect = ShowReadout
                ? new Rect(inner.X, inner.Bottom - readoutH, inner.Width, readoutH)
                : Rect.Empty;

            //dc.DrawRoundedRectangle(null, framePen, inner, CornerRadius, CornerRadius);

            if (meterRect.Width <= 4 || meterRect.Height <= 4) return;

            dc.DrawRoundedRectangle(null, framePen, meterRect, CornerRadius, CornerRadius);

            var meterPad = 2.0;
            var fillRect = new Rect(meterRect.X + meterPad, meterRect.Y + meterPad, Math.Max(0, meterRect.Width - meterPad * 2), Math.Max(0, meterRect.Height - meterPad * 2));

            if (ShowScale && scaleW > 0)
            {
                DrawScale(dc, new Rect(inner.X, meterRect.Y, scaleW, meterRect.Height), framePen);
            }

            DrawSegments(dc, fillRect);
            DrawPeak(dc, fillRect);

            if (ShowReadout)
            {
                DrawReadout(dc, readoutRect, framePen);
            }
        }

        private void DrawScale(DrawingContext dc, Rect scaleRect, Pen framePen)
        {
            //dc.DrawRectangle(null, framePen, scaleRect);

            var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var fontSize = 10.0;

            var step = Math.Max(0.1, TickStepDb);
            var min = MinDb;
            var max = MaxDb;

            for (double db = max; db >= min - 0.0001; db -= step)
            {
                var y = DbToY(db, scaleRect.Top + 2.0, scaleRect.Bottom - 2.0);
                var label = db.ToString("0", CultureInfo.InvariantCulture);

                var ft = MakeText(label, typeface, fontSize, ScaleTextBrush);
                var x = scaleRect.Right - ft.Width - 4.0;
                dc.DrawText(ft, new Point(x, y - ft.Height / 2.0));
            }

            //var ftDb = MakeText("dB", typeface, 10.0, ScaleTextBrush);
            ////dc.DrawText(ftDb, new Point(scaleRect.Left + 4.0, scaleRect.Bottom - ftDb.Height - 2.0));
        }

        private void DrawSegments(DrawingContext dc, Rect fillRect)
        {
            var n = Math.Max(4, SegmentCount);
            var spacing = SegmentSpacing;

            var usableH = fillRect.Height - spacing * (n - 1);
            if (usableH <= 1) return;

            var segH = usableH / n;
            if (segH <= 0.5) segH = 0.5;

            var level = ClampDb(_displayDb);
            var min = MinDb;
            var max = MaxDb;

            var t = DbToNorm(level, min, max);
            var activeCount = (int)Math.Ceiling(t * n);
            if (activeCount < 0) activeCount = 0;
            if (activeCount > n) activeCount = n;

            for (int i = 0; i < n; i++)
            {
                var y = fillRect.Bottom - (i + 1) * segH - i * spacing;
                var rect = new Rect(fillRect.Left, y, fillRect.Width, segH);

                var segTopDb = min + (i + 1) / (double)n * (max - min);
                var active = i < activeCount;

                Brush b = active ? BrushForDb(segTopDb) : InactiveBrush;
                dc.DrawRoundedRectangle(b, null, rect, CornerRadius, CornerRadius);
            }
        }

        private void DrawPeak(DrawingContext dc, Rect fillRect)
        {
            var peak = ClampDb(_peakDb);
            var y = DbToY(peak, fillRect.Top, fillRect.Bottom);

            var pen = new Pen(PeakLineBrush, 1.5);
            pen.Freeze();

            dc.DrawLine(pen, new Point(fillRect.Left, y), new Point(fillRect.Right, y));
        }

        private void DrawReadout(DrawingContext dc, Rect readoutRect, Pen framePen)
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(10, 10, 10)), framePen, readoutRect, CornerRadius, CornerRadius);

            var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            var fontSize = 11.0;

            var a = ClampDb(_displayDb).ToString("0.0", CultureInfo.InvariantCulture);
            var p = ClampDb(_peakDb).ToString("0.0", CultureInfo.InvariantCulture);

            var left = MakeText(a, typeface, fontSize, ReadoutTextBrush);
            var right = MakeText(p, typeface, fontSize, ReadoutTextBrush);

            var midY = readoutRect.Top + (readoutRect.Height - left.Height) / 2.0;

            dc.DrawText(left, new Point(readoutRect.Left + 6.0, midY));
            dc.DrawText(right, new Point(readoutRect.Right - right.Width - 6.0, midY));
        }

        // ===== Helpers =====

        private Brush BrushForDb(double db)
        {
            if (db >= YellowThresholdDb) return ActiveRedBrush;
            if (db >= GreenThresholdDb) return ActiveYellowBrush;
            return ActiveGreenBrush;
        }

        private double ClampDb(double db)
        {
            var min = MinDb;
            var max = MaxDb;
            if (min > max) (min, max) = (max, min);
            if (db < min) return min;
            if (db > max) return max;
            return db;
        }

        private static double DbToNorm(double db, double min, double max)
        {
            if (max - min <= 0.000001) return 0;
            var t = (db - min) / (max - min);
            if (t < 0) return 0;
            if (t > 1) return 1;
            return t;
        }

        private double DbToY(double db, double top, double bottom)
        {
            var min = MinDb;
            var max = MaxDb;
            if (min > max) (min, max) = (max, min);

            var t = DbToNorm(db, min, max);
            return bottom - t * (bottom - top);
        }

        private static FormattedText MakeText(string text, Typeface typeface, double fontSize, Brush brush)
        {
#pragma warning disable CS0618
            var ft = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                brush,
                1.0);
#pragma warning restore CS0618
            ft.TextAlignment = TextAlignment.Left;
            ft.SetFontWeight(typeface.Weight);
            return ft;
        }
    }
}
