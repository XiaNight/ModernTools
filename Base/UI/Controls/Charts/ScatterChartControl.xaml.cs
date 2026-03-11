using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Base.Components.Chart
{
    /// <summary>
    /// A scatter/XY chart control that uses GPU-accelerated rendering via ComputeSharp.
    /// Throws <see cref="GpuNotAvailableException"/> if no compatible GPU is available.
    /// </summary>
    public sealed partial class ScatterChartControl : UserControl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ScatterChartControl"/> class.
        /// </summary>
        /// <exception cref="GpuNotAvailableException">Thrown when no compatible GPU is available.</exception>
        public ScatterChartControl()
        {
            // Ensure GPU is available - throws GpuNotAvailableException if not
            GpuChartRenderer.EnsureGpuAvailable();

            ClipToBounds = true;
            Background = Brushes.Transparent;

            SetCurrentValue(LineThicknessProperty, 2.0);
            SetCurrentValue(DotThicknessProperty, 4.0);

            SetCurrentValue(AutoFitProperty, true);
            SetCurrentValue(RenderModeProperty, ChartRenderMode.Line);

            SetCurrentValue(MinXProperty, -1.0);
            SetCurrentValue(MaxXProperty, 1.0);
            SetCurrentValue(MinYProperty, -1.0);
            SetCurrentValue(MaxYProperty, 1.0);

            ResetAxes();
        }

        // -------- Dependency Properties --------
        public static readonly DependencyProperty AutoFitProperty =
            DependencyProperty.Register(nameof(AutoFit), typeof(bool), typeof(ScatterChartControl),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MinXProperty =
            DependencyProperty.Register(nameof(MinX), typeof(double), typeof(ScatterChartControl),
                new FrameworkPropertyMetadata(-1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MaxXProperty =
            DependencyProperty.Register(nameof(MaxX), typeof(double), typeof(ScatterChartControl),
                new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MinYProperty =
            DependencyProperty.Register(nameof(MinY), typeof(double), typeof(ScatterChartControl),
                new FrameworkPropertyMetadata(-1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MaxYProperty =
            DependencyProperty.Register(nameof(MaxY), typeof(double), typeof(ScatterChartControl),
                new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty LineBrushProperty =
            DependencyProperty.Register(nameof(LineBrush), typeof(Brush), typeof(ScatterChartControl),
                new FrameworkPropertyMetadata(Brushes.LawnGreen, FrameworkPropertyMetadataOptions.AffectsRender, OnStyleChanged));

        public static readonly DependencyProperty LineThicknessProperty =
            DependencyProperty.Register(nameof(LineThickness), typeof(double), typeof(ScatterChartControl),
                new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender, OnStyleChanged));

        public static readonly DependencyProperty DotBrushProperty =
            DependencyProperty.Register(nameof(DotBrush), typeof(Brush), typeof(ScatterChartControl),
                new FrameworkPropertyMetadata(Brushes.LawnGreen, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty DotThicknessProperty =
            DependencyProperty.Register(nameof(DotThickness), typeof(double), typeof(ScatterChartControl),
                new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty RenderModeProperty =
            DependencyProperty.Register(nameof(RenderMode), typeof(ChartRenderMode), typeof(ScatterChartControl),
                new FrameworkPropertyMetadata(ChartRenderMode.Line, FrameworkPropertyMetadataOptions.AffectsRender));

        public bool AutoFit
        {
            get => (bool)GetValue(AutoFitProperty);
            set => SetValue(AutoFitProperty, value);
        }

        public double MinX
        {
            get => (double)GetValue(MinXProperty);
            set => SetValue(MinXProperty, value);
        }

        public double MaxX
        {
            get => (double)GetValue(MaxXProperty);
            set => SetValue(MaxXProperty, value);
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

        public ChartRenderMode RenderMode
        {
            get => (ChartRenderMode)GetValue(RenderModeProperty);
            set => SetValue(RenderModeProperty, value);
        }

        // -------- Public API --------
        public void Clear()
        {
            _bitmap = null;
            _hasLast = false;
            ResetAxes();
            InvalidateVisual();
        }

        public void AddSample(Point p)
        {
            if (!IsLoaded || ActualWidth <= 0 || ActualHeight <= 0)
            {
                _pending.Add(p);
                Loaded -= OnDeferredLoaded;
                Loaded += OnDeferredLoaded;
                return;
            }

            _pending.Add(p);
            EnsureRenderHook();
        }

        public void AddSamples(IEnumerable<Point> points)
        {
            if (points == null) return;

            if (!IsLoaded || ActualWidth <= 0 || ActualHeight <= 0)
            {
                _pending.AddRange(points);
                Loaded -= OnDeferredLoaded;
                Loaded += OnDeferredLoaded;
                return;
            }

            _pending.AddRange(points);
            EnsureRenderHook();
        }

        private void OnDeferredLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnDeferredLoaded;
            if (_pending.Count > 0)
                EnsureRenderHook();
        }

        private void EnsureRenderHook()
        {
            if (_renderHooked) return;
            _renderHooked = true;

            // Coalesce all AddSample calls that happen this tick and render once per frame.
            CompositionTarget.Rendering -= OnRenderTick;
            CompositionTarget.Rendering += OnRenderTick;
        }

        private void OnRenderTick(object sender, EventArgs e)
        {
            if (_pending.Count == 0)
            {
                // Nothing to do; unhook until more samples arrive.
                CompositionTarget.Rendering -= OnRenderTick;
                _renderHooked = false;
                return;
            }

            int w = Math.Max(1, (int)ActualWidth);
            int h = Math.Max(1, (int)ActualHeight);
            var targetRect = new Rect(0, 0, w, h);

            var batch = _pending.ToArray();
            _pending.Clear();

            // Update axes with new data
            foreach (var p in batch)
            {
                UpdateAxesWith(p.X, p.Y);
            }

            // Get current axis bounds
            float minX = (float)(AutoFit ? _curMinX : MinX);
            float maxX = (float)(AutoFit ? _curMaxX : MaxX);
            float minY = (float)(AutoFit ? _curMinY : MinY);
            float maxY = (float)(AutoFit ? _curMaxY : MaxY);

            if (maxX - minX <= 0) maxX = minX + 1;
            if (maxY - minY <= 0) maxY = minY + 1;

            // Get colors from brushes
            Color dotColor = GetColorFromBrush(DotBrush ?? LineBrush);
            Color lineColor = GetColorFromBrush(LineBrush);

            // Use DrawingVisual to blit new data onto the existing bitmap (original approach)
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Draw existing bitmap as background
                if (_bitmap != null)
                {
                    dc.DrawImage(_bitmap, targetRect);
                }
                else
                {
                    dc.DrawRectangle(Background ?? Brushes.Transparent, null, targetRect);
                }

                // Draw lines first (if enabled) - connecting new points to previous
                if (RenderMode == ChartRenderMode.Line || RenderMode == ChartRenderMode.Combined)
                {
                    var linePen = _cachedPen ??= MakePen();
                    for (int i = 0; i < batch.Length; i++)
                    {
                        var p = batch[i];
                        var pt = WorldToScreen(p, targetRect, minX, maxX, minY, maxY);

                        if (_hasLast)
                            dc.DrawLine(linePen, _lastScreen, pt);

                        _lastScreen = pt;
                        _hasLast = true;
                    }
                }

                // Draw dots on top (if enabled)
                if (RenderMode == ChartRenderMode.Dot || RenderMode == ChartRenderMode.Combined)
                {
                    var dotBrush = DotBrush ?? LineBrush ?? Brushes.LawnGreen;
                    double radius = Math.Max(0.5, DotThickness) * 0.5;

                    foreach (var p in batch)
                    {
                        // If only Dot mode, update last point for potential future line connections
                        if (RenderMode == ChartRenderMode.Dot)
                        {
                            _lastScreen = WorldToScreen(p, targetRect, minX, maxX, minY, maxY);
                            _hasLast = true;
                        }

                        var pt = WorldToScreen(p, targetRect, minX, maxX, minY, maxY);
                        dc.DrawEllipse(dotBrush, null, pt, radius, radius);
                    }
                }
            }

            // Render the DrawingVisual to a new bitmap
            var newBmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            newBmp.Render(dv);
            newBmp.Freeze();
            _bitmap = newBmp;

            InvalidateVisual();
        }

        private Point WorldToScreen(Point p, Rect rect, float minX, float maxX, float minY, float maxY)
        {
            double dx = maxX - minX;
            double dy = maxY - minY;
            if (dx <= 0) dx = 1;
            if (dy <= 0) dy = 1;

            double nx = (p.X - minX) / dx;
            double ny = (p.Y - minY) / dy;

            double sx = rect.Left + nx * rect.Width;
            double sy = rect.Top + (1.0 - ny) * rect.Height;
            return new Point(sx, sy);
        }

        private static Color GetColorFromBrush(Brush brush)
        {
            if (brush is SolidColorBrush scb)
                return scb.Color;
            return Colors.LawnGreen;
        }

        private void OnDeferredBatchLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnDeferredBatchLoaded;
            if (_deferredBatch is { Count: > 0 })
            {
                var batch = _deferredBatch;
                _deferredBatch = null;
                AddSamples(batch!);
            }
        }

        public void SavePng(string filePath)
        {
            if (_bitmap == null) return;
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(_bitmap));
            using var fs = File.Create(filePath);
            encoder.Save(fs);
        }

        public void ResetAxes()
        {
            _curMinX = double.NaN; _curMaxX = double.NaN;
            _curMinY = double.NaN; _curMaxY = double.NaN;
        }

        // -------- Rendering --------
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            var rect = new Rect(0, 0, ActualWidth, ActualHeight);
            if (_bitmap != null)
                dc.DrawImage(_bitmap, rect);
            else
                dc.DrawRectangle(Background ?? Brushes.Transparent, null, rect);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);

            if (sizeInfo.NewSize.Width <= 0 || sizeInfo.NewSize.Height <= 0)
                return;

            // Scale existing bitmap to new size (original behavior)
            if (_bitmap != null)
            {
                int w = (int)Math.Max(1, sizeInfo.NewSize.Width);
                int h = (int)Math.Max(1, sizeInfo.NewSize.Height);

                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(Background ?? Brushes.Transparent, null, new Rect(0, 0, w, h));
                    dc.DrawImage(_bitmap, new Rect(0, 0, w, h)); // scale old into new size
                }

                var newBmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
                newBmp.Render(dv);
                newBmp.Freeze();
                _bitmap = newBmp;
                InvalidateVisual();
            }
        }

        // -------- Helpers --------
        private static void OnStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (ScatterChartControl)d;
            c._cachedPen = null; // future strokes use new style
        }

        private void OnDeferredAddLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnDeferredAddLoaded;
            AddSample(new(_deferredPoint.X, _deferredPoint.Y));
        }

        private void UpdateAxesWith(double x, double y)
        {
            if (!AutoFit)
            {
                _curMinX = MinX; _curMaxX = MaxX;
                _curMinY = MinY; _curMaxY = MaxY;
                return;
            }

            if (double.IsNaN(_curMinX)) { _curMinX = _curMaxX = x; } else { if (x < _curMinX) _curMinX = x; if (x > _curMaxX) _curMaxX = x; }
            if (double.IsNaN(_curMinY)) { _curMinY = _curMaxY = y; } else { if (y < _curMinY) _curMinY = y; if (y > _curMaxY) _curMaxY = y; }

            if (Math.Abs(_curMaxX - _curMinX) < 1e-12) _curMaxX = _curMinX + 1.0;
            if (Math.Abs(_curMaxY - _curMinY) < 1e-12) _curMaxY = _curMinY + 1.0;
        }

        private Pen MakePen()
        {
            var pen = new Pen(LineBrush, Math.Max(0.5, LineThickness))
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
                MiterLimit = 1.0
            };
            pen.Freeze();
            return pen;
        }

        // -------- Fields --------
        private RenderTargetBitmap _bitmap;
        private Pen _cachedPen;

        private double _curMinX, _curMaxX, _curMinY, _curMaxY;
        private bool _hasLast;
        private Point _lastScreen;
        private Point _deferredPoint;
        private List<Point> _deferredBatch;
        private readonly List<Point> _pending = new();
        private bool _renderHooked;
    }
}
