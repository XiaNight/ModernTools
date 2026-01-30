using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Audio
{
    /// <summary>
    /// Interaction logic for BarRenderer.xaml
    /// </summary>
    public partial class BarRenderer : UserControl, IDisposable
    {
        public static readonly DependencyProperty MultiplierProperty =
            DependencyProperty.Register(nameof(Multiplier), typeof(float), typeof(BarRenderer),
                new FrameworkPropertyMetadata(1.0f, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty CapacityProperty =
            DependencyProperty.Register(nameof(Capacity), typeof(int), typeof(BarRenderer),
                new FrameworkPropertyMetadata(1024, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty UpdateFpsProperty =
            DependencyProperty.Register(nameof(UpdateFps), typeof(int), typeof(BarRenderer),
                new FrameworkPropertyMetadata(60, OnUpdateFpsChanged));

        public static readonly DependencyProperty UseAntialiasProperty =
            DependencyProperty.Register(nameof(UseAntialias), typeof(bool), typeof(BarRenderer),
                new FrameworkPropertyMetadata(false, OnUseAAChanged));
        public float Multiplier
        {
            get => (float)GetValue(MultiplierProperty);
            set => SetValue(MultiplierProperty, value);
        }
        public int Capacity
        {
            get => (int)GetValue(CapacityProperty);
            set => SetValue(CapacityProperty, value);
        }

        public int UpdateFps
        {
            get => (int)GetValue(UpdateFpsProperty);
            set => SetValue(UpdateFpsProperty, value);
        }
        public bool UseAntialias
        {
            get => (bool)GetValue(UseAntialiasProperty);
            set => SetValue(UseAntialiasProperty, value);
        }

        private static void OnUpdateFpsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (BarRenderer)d;
            var v = Math.Max(1, Math.Min(240, (int)e.NewValue));
            c._updateFps = v;
            if (c._isRunning) c.RestartTimer();
        }

        private static void OnUseAAChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var c = (BarRenderer)d;
            c.ApplyAA();
        }

        public BarRenderer()
        {
            InitializeComponent();

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

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            var full = new Rect(0, 0, ActualWidth, ActualHeight);
            if (full.Width <= 1 || full.Height <= 1) return;

            dc.DrawRectangle(Background, null, full);

            double barWidth = full.Width / Capacity;
            double midHeight = full.Height / 2;
            double offset = (full.Width - barWidth * values.Length) / 2;
            double step = barWidth;

            for (int i = 0; i < values.Length; i++)
            {
                float value = values[i];
                var barHeight = value * full.Height / 2 * Multiplier;
                var barRect = new Rect(step * i + offset, midHeight - barHeight, barWidth, barHeight * 2);
                dc.DrawRectangle(Brushes.Black, null, barRect);
            }
        }
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

        private void RestartTimer()
        {
            _renderTimer.Stop();
            _renderTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, _updateFps));
            if (_isRunning) _renderTimer.Start();
        }

        public void SetValues(float[] newValues)
        {
            if (newValues == null || newValues.Length == 0) return;
            int len = Math.Min(newValues.Length, values.Length);
            Array.Copy(newValues, values, len);
            _dirty = true;
        }
        public void Dispose() => Stop();
        private void OnVSync(object sender, EventArgs e) => ApplyAA();

        private void ApplyAA()
        {
            if (_useAAApplied == UseAntialias) return;
            RenderOptions.SetEdgeMode(this, UseAntialias ? EdgeMode.Unspecified : EdgeMode.Aliased);
            RenderOptions.SetBitmapScalingMode(this, UseAntialias ? BitmapScalingMode.Linear : BitmapScalingMode.NearestNeighbor);
            _useAAApplied = UseAntialias;
        }

        // 0 ~ 1
        private float[] values = new float[1024];

        private DispatcherTimer _renderTimer = null!;
        private bool _dirty;
        private bool _isRunning;
        private int _updateFps = 60;
        private bool _useAAApplied = false;
    }
}
