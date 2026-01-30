// =======================================================
// SimpleLineGraph.cs  (= drop-in WPF control)  ( ﾟ∀ﾟ)ﾉ♡
// =======================================================

#nullable enable
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Base.Components
{
    public sealed class SimpleLineGraph : FrameworkElement
    {
        public ObservableCollection<Point> Points
        {
            get => (ObservableCollection<Point>)GetValue(PointsProperty);
            set => SetValue(PointsProperty, value);
        }

        public static readonly DependencyProperty PointsProperty =
            DependencyProperty.Register(
                nameof(Points),
                typeof(ObservableCollection<Point>),
                typeof(SimpleLineGraph),
                new FrameworkPropertyMetadata(new ObservableCollection<Point>(), FrameworkPropertyMetadataOptions.AffectsRender, OnPointsChanged));

        private static void OnPointsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var g = (SimpleLineGraph)d;
            if (e.OldValue is ObservableCollection<Point> oldC) oldC.CollectionChanged -= g.OnPointsCollectionChanged;
            if (e.NewValue is ObservableCollection<Point> newC) newC.CollectionChanged += g.OnPointsCollectionChanged;
            g.InvalidateVisual();
        }

        private void OnPointsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
            => InvalidateVisual();

        // ----------------------------
        // Styling  (ﾉ◕ヮ◕)ﾉ*:･ﾟ✧
        // ----------------------------
        public Brush Background
        {
            get => (Brush)GetValue(BackgroundProperty);
            set => SetValue(BackgroundProperty, value);
        }
        public static readonly DependencyProperty BackgroundProperty =
            DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(SimpleLineGraph),
                new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush Stroke
        {
            get => (Brush)GetValue(StrokeProperty);
            set => SetValue(StrokeProperty, value);
        }
        public static readonly DependencyProperty StrokeProperty =
            DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(SimpleLineGraph),
                new FrameworkPropertyMetadata(SystemColors.HighlightBrush, FrameworkPropertyMetadataOptions.AffectsRender));

        public double StrokeThickness
        {
            get => (double)GetValue(StrokeThicknessProperty);
            set => SetValue(StrokeThicknessProperty, value);
        }
        public static readonly DependencyProperty StrokeThicknessProperty =
            DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(SimpleLineGraph),
                new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush AxisBrush
        {
            get => (Brush)GetValue(AxisBrushProperty);
            set => SetValue(AxisBrushProperty, value);
        }
        public static readonly DependencyProperty AxisBrushProperty =
            DependencyProperty.Register(nameof(AxisBrush), typeof(Brush), typeof(SimpleLineGraph),
                new FrameworkPropertyMetadata(SystemColors.ControlTextBrush, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush GridBrush
        {
            get => (Brush)GetValue(GridBrushProperty);
            set => SetValue(GridBrushProperty, value);
        }
        public static readonly DependencyProperty GridBrushProperty =
            DependencyProperty.Register(nameof(GridBrush), typeof(Brush), typeof(SimpleLineGraph),
                new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(35, 128, 128, 128)), FrameworkPropertyMetadataOptions.AffectsRender));

        public bool ShowGrid
        {
            get => (bool)GetValue(ShowGridProperty);
            set => SetValue(ShowGridProperty, value);
        }
        public static readonly DependencyProperty ShowGridProperty =
            DependencyProperty.Register(nameof(ShowGrid), typeof(bool), typeof(SimpleLineGraph),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush IndicatorBrush
        {
            get => (Brush)GetValue(IndicatorBrushProperty);
            set => SetValue(IndicatorBrushProperty, value);
        }
        public static readonly DependencyProperty IndicatorBrushProperty =
            DependencyProperty.Register(nameof(IndicatorBrush), typeof(Brush), typeof(SimpleLineGraph),
                new FrameworkPropertyMetadata(SystemColors.HighlightBrush, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush CursorLineBrush
        {
            get => (Brush)GetValue(CursorLineBrushProperty);
            set => SetValue(CursorLineBrushProperty, value);
        }
        public static readonly DependencyProperty CursorLineBrushProperty =
            DependencyProperty.Register(nameof(CursorLineBrush), typeof(Brush), typeof(SimpleLineGraph),
                new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(140, 200, 200, 200)), FrameworkPropertyMetadataOptions.AffectsRender));

        public Thickness PlotPadding
        {
            get => (Thickness)GetValue(PlotPaddingProperty);
            set => SetValue(PlotPaddingProperty, value);
        }
        public static readonly DependencyProperty PlotPaddingProperty =
            DependencyProperty.Register(nameof(PlotPadding), typeof(Thickness), typeof(SimpleLineGraph),
                new FrameworkPropertyMetadata(new Thickness(40, 16, 16, 28), FrameworkPropertyMetadataOptions.AffectsRender));

        public int TickCount
        {
            get => (int)GetValue(TickCountProperty);
            set => SetValue(TickCountProperty, value);
        }
        public static readonly DependencyProperty TickCountProperty =
            DependencyProperty.Register(nameof(TickCount), typeof(int), typeof(SimpleLineGraph),
                new FrameworkPropertyMetadata(5, FrameworkPropertyMetadataOptions.AffectsRender));

        public int FontSize
        {
            get => (int)GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }
        public static readonly DependencyProperty FontSizeProperty =
            DependencyProperty.Register(nameof(FontSize), typeof(int), typeof(SimpleLineGraph),
                new FrameworkPropertyMetadata(12, FrameworkPropertyMetadataOptions.AffectsRender));

        public string TooltipFormat
        {
            get => (string)GetValue(TooltipFormatProperty);
            set => SetValue(TooltipFormatProperty, value);
        }
        public static readonly DependencyProperty TooltipFormatProperty =
            DependencyProperty.Register(nameof(TooltipFormat), typeof(string), typeof(SimpleLineGraph),
                new FrameworkPropertyMetadata("x={0:0.###}\ny={1:0.###}", FrameworkPropertyMetadataOptions.AffectsRender));

        // ----------------------------
        // Optional fixed ranges  (ง •̀_•́)ง
        // NaN = auto
        // ----------------------------
        public double XMin
        {
            get => (double)GetValue(XMinProperty);
            set => SetValue(XMinProperty, value);
        }
        public static readonly DependencyProperty XMinProperty =
            DependencyProperty.Register(nameof(XMin), typeof(double), typeof(SimpleLineGraph),
                new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

        public double XMax
        {
            get => (double)GetValue(XMaxProperty);
            set => SetValue(XMaxProperty, value);
        }
        public static readonly DependencyProperty XMaxProperty =
            DependencyProperty.Register(nameof(XMax), typeof(double), typeof(SimpleLineGraph),
                new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

        public double YMin
        {
            get => (double)GetValue(YMinProperty);
            set => SetValue(YMinProperty, value);
        }
        public static readonly DependencyProperty YMinProperty =
            DependencyProperty.Register(nameof(YMin), typeof(double), typeof(SimpleLineGraph),
                new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

        public double YMax
        {
            get => (double)GetValue(YMaxProperty);
            set => SetValue(YMaxProperty, value);
        }
        public static readonly DependencyProperty YMaxProperty =
            DependencyProperty.Register(nameof(YMax), typeof(double), typeof(SimpleLineGraph),
                new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

        // ----------------------------
        // Hover state  (づ｡◕‿‿◕｡)づ
        // ----------------------------
        private bool _hover;
        private Point _mousePx;
        private Point _hoverData;
        private double _hoverX;
        private Rect _plot;
        private (double xmin, double xmax, double ymin, double ymax) _range;
        private readonly ToolTip _tt = new ToolTip { Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse, StaysOpen = true };

        public SimpleLineGraph()
        {
            SnapsToDevicePixels = true;
            Focusable = false;

            ToolTipService.SetInitialShowDelay(this, 0);
            ToolTipService.SetBetweenShowDelay(this, 0);
            ToolTipService.SetShowDuration(this, int.MaxValue);
            ToolTip = _tt;

            MouseMove += OnMouseMove;
            MouseLeave += OnMouseLeave;
            SizeChanged += (_, __) => InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var w = double.IsInfinity(availableSize.Width) ? 240 : availableSize.Width;
            var h = double.IsInfinity(availableSize.Height) ? 120 : availableSize.Height;
            return new Size(w, h);
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            _hover = false;
            _tt.IsOpen = false;
            InvalidateVisual();
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            _mousePx = e.GetPosition(this);
            if (!_plot.Contains(_mousePx) || Points.Count == 0)
            {
                _hover = false;
                _tt.IsOpen = false;
                InvalidateVisual();
                return;
            }

            _hover = true;

            _hoverX = ScreenToDataX(_mousePx.X, _plot, _range.xmin, _range.xmax);
            _hoverData = InterpolateYAtX(_hoverX);
            _tt.Content = string.Format(CultureInfo.CurrentCulture, TooltipFormat, _hoverData.X, _hoverData.Y);
            _tt.IsOpen = true;

            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            var w = ActualWidth;
            var h = ActualHeight;
            if (w <= 1 || h <= 1) return;

            dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));

            _plot = new Rect(
                PlotPadding.Left,
                PlotPadding.Top,
                Math.Max(1, w - PlotPadding.Left - PlotPadding.Right),
                Math.Max(1, h - PlotPadding.Top - PlotPadding.Bottom));

            var ptsSorted = GetSortedPoints();
            _range = ComputeRange(ptsSorted);

            DrawAxesAndGrid(dc, _plot, _range);

            if (ptsSorted.Count >= 2)
            {
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(DataToScreen(ptsSorted[0], _plot, _range), false, false);
                    for (int i = 1; i < ptsSorted.Count; i++)
                        ctx.LineTo(DataToScreen(ptsSorted[i], _plot, _range), true, false);
                }
                geo.Freeze();

                var pen = new Pen(Stroke, StrokeThickness)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };

                dc.DrawGeometry(null, pen, geo);
            }
            else if (ptsSorted.Count == 1)
            {
                var p = DataToScreen(ptsSorted[0], _plot, _range);
                dc.DrawEllipse(Stroke, null, p, 2.5, 2.5);
            }

            if (_hover && _plot.Contains(_mousePx) && ptsSorted.Count > 0)
            {
                var hoverPx = DataToScreen(_hoverData, _plot, _range);

                var vPen = new Pen(CursorLineBrush, 1.0) { DashStyle = DashStyles.Dash };
                dc.DrawLine(vPen, new Point(hoverPx.X, _plot.Top), new Point(hoverPx.X, _plot.Bottom));

                dc.DrawEllipse(IndicatorBrush, new Pen(Brushes.White, 1.5), hoverPx, 4.0, 4.0);
            }
        }

        // ----------------------------
        // Helpers  (ง'̀-'́)ง
        // ----------------------------
        private List<Point> GetSortedPoints()
        {
            var src = Points ?? new ObservableCollection<Point>();
            if (src.Count == 0) return new List<Point>(0);

            // copy + sort by X, stable for same X
            return src.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        }

        private (double xmin, double xmax, double ymin, double ymax) ComputeRange(List<Point> pts)
        {
            if (pts.Count == 0)
                return (0, 1, 0, 1);

            double xmin = double.IsNaN(XMin) ? pts.Min(p => p.X) : XMin;
            double xmax = double.IsNaN(XMax) ? pts.Max(p => p.X) : XMax;
            double ymin = double.IsNaN(YMin) ? pts.Min(p => p.Y) : YMin;
            double ymax = double.IsNaN(YMax) ? pts.Max(p => p.Y) : YMax;

            if (xmax - xmin == 0) { xmax = xmin + 1; }
            if (ymax - ymin == 0) { ymax = ymin + 1; }

            // small margins for nicer visuals (unless user fixed both bounds)
            if (double.IsNaN(XMin) || double.IsNaN(XMax))
            {
                var dx = (xmax - xmin) * 0.02;
                xmin -= dx; xmax += dx;
            }
            if (double.IsNaN(YMin) || double.IsNaN(YMax))
            {
                var dy = (ymax - ymin) * 0.06;
                ymin -= dy; ymax += dy;
            }

            return (xmin, xmax, ymin, ymax);
        }

        private void DrawAxesAndGrid(DrawingContext dc, Rect plot, (double xmin, double xmax, double ymin, double ymax) r)
        {
            var axisPen = new Pen(AxisBrush, 1.0);
            dc.DrawRectangle(null, axisPen, plot);

            int ticks = Math.Max(2, TickCount);
            var gridPen = new Pen(GridBrush, 1.0);

            var typeface = new Typeface("Segoe UI");
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            if (ShowGrid)
            {
                for (int i = 1; i < ticks; i++)
                {
                    double t = (double)i / ticks;

                    // vertical grid
                    double x = plot.Left + t * plot.Width;
                    dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));

                    // horizontal grid
                    double y = plot.Top + t * plot.Height;
                    dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
                }
            }

            // labels
            for (int i = 0; i <= ticks; i++)
            {
                double t = (double)i / ticks;

                // x label
                double xVal = Lerp(r.xmin, r.xmax, t);
                string xText = FormatTick(xVal);
                var xFt = new FormattedText(xText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, FontSize * 0.85, AxisBrush, dpi);
                double x = plot.Left + t * plot.Width - xFt.Width / 2;
                double y = plot.Bottom + 4;
                dc.DrawText(xFt, new Point(x, y));

                // y label
                double yVal = Lerp(r.ymax, r.ymin, t);
                string yText = FormatTick(yVal);
                var yFt = new FormattedText(yText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, FontSize * 0.85, AxisBrush, dpi);
                double yy = plot.Top + t * plot.Height - yFt.Height / 2;
                double xx = Math.Max(0, plot.Left - 6 - yFt.Width);
                dc.DrawText(yFt, new Point(xx, yy));
            }
        }

        private string FormatTick(double v)
        {
            double av = Math.Abs(v);
            if (av >= 10000) return v.ToString("0.###E+0", CultureInfo.CurrentCulture);
            if (av >= 100) return v.ToString("0.##", CultureInfo.CurrentCulture);
            if (av >= 1) return v.ToString("0.###", CultureInfo.CurrentCulture);
            if (av >= 0.01) return v.ToString("0.####", CultureInfo.CurrentCulture);
            return v.ToString("0.#####", CultureInfo.CurrentCulture);
        }

        private Point InterpolateYAtX(double x)
        {
            var pts = GetSortedPoints();
            if (pts.Count == 0) return new Point(x, 0);
            if (pts.Count == 1) return new Point(x, pts[0].Y);

            if (x <= pts[0].X) return new Point(pts[0].X, pts[0].Y);
            if (x >= pts[^1].X) return new Point(pts[^1].X, pts[^1].Y);

            // binary search segment
            int lo = 0, hi = pts.Count - 1;
            while (hi - lo > 1)
            {
                int mid = lo + hi >> 1;
                if (pts[mid].X <= x) lo = mid;
                else hi = mid;
            }

            var a = pts[lo];
            var b = pts[hi];

            double dx = b.X - a.X;
            if (dx == 0) return new Point(a.X, (a.Y + b.Y) * 0.5);

            double t = (x - a.X) / dx;
            double y = Lerp(a.Y, b.Y, t);

            return new Point(x, y);
        }

        private static Point DataToScreen(Point p, Rect plot, (double xmin, double xmax, double ymin, double ymax) r)
        {
            double tx = (p.X - r.xmin) / (r.xmax - r.xmin);
            double ty = (p.Y - r.ymin) / (r.ymax - r.ymin);

            double x = plot.Left + tx * plot.Width;
            double y = plot.Bottom - ty * plot.Height; // invert

            return new Point(x, y);
        }

        private static double ScreenToDataX(double xPx, Rect plot, double xmin, double xmax)
        {
            double t = (xPx - plot.Left) / Math.Max(1e-12, plot.Width);
            t = Math.Max(0, Math.Min(1, t));
            return Lerp(xmin, xmax, t);
        }

        private static double Lerp(double a, double b, double t)
            => a + (b - a) * t;
    }
}

// =======================================================
// Example usage (Window XAML)  ( ﾟ∀ﾟ)ﾉ☆
// =======================================================
/*
<Window x:Class="WpfApp1.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:local="clr-namespace:DropInControls"
        Title="Demo" Width="800" Height="450">
    <Grid Margin="16">
        <local:SimpleLineGraph x:Name="Graph"
                               Background="{DynamicResource CardBackgroundFillColorDefaultBrush}"
                               AxisBrush="{DynamicResource TextFillColorPrimaryBrush}"
                               GridBrush="{DynamicResource DividerStrokeColorDefaultBrush}"
                               Stroke="{DynamicResource AccentFillColorDefaultBrush}"
                               IndicatorBrush="{DynamicResource AccentFillColorDefaultBrush}"
                               CursorLineBrush="{DynamicResource TextFillColorSecondaryBrush}"
                               StrokeThickness="2"
                               ShowGrid="True"
                               TickCount="5"
                               TooltipFormat="x={0:0.###}  y={1:0.###}" />
    </Grid>
</Window>
*/

// =======================================================
// Example usage (Window code-behind)  (ง'̀-'́)ง
// =======================================================
/*
using System.Windows;
using System.Windows;

namespace WpfApp1
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            Graph.Points.Clear();
            Graph.Points.Add(new Point(0, 0.2));
            Graph.Points.Add(new Point(1, 0.6));
            Graph.Points.Add(new Point(2, 0.1));
            Graph.Points.Add(new Point(3, 0.85));
            Graph.Points.Add(new Point(4, 0.4));
            Graph.Points.Add(new Point(5, 0.9));
        }
    }
}
*/
