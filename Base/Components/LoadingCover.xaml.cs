// LoadingCover.xaml.cs
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Base.Components
{
    public partial class LoadingCover : UserControl
    {
        public readonly struct LoadingJob
        {
            public readonly float Weight;
            private readonly Action _finish;

            internal LoadingJob(float weight, Action finish)
            {
                Weight = weight;
                _finish = finish;
            }

            public void Finish() => _finish?.Invoke();
        }

        public LoadingCover()
        {
            InitializeComponent();
            UpdateOpenState(false, immediate: true);
        }

        // =========================
        // Public API (Jobs)
        // =========================

        public LoadingJob RentJob(float weight = 1f)
        {
            if (float.IsNaN(weight) || float.IsInfinity(weight) || weight <= 0f)
                weight = 1f;

            EnsureOnUI(() =>
            {
                if (!IsOpen)
                    IsOpen = true;

                IsIndeterminate = false;
                _totalWeight += weight;
                UpdateProgress();
            });

            int finishedFlag = 0;

            void FinishImpl()
            {
                if (Interlocked.Exchange(ref finishedFlag, 1) != 0)
                    return;

                EnsureOnUI(() =>
                {
                    _finishedWeight += weight;
                    if (_finishedWeight > _totalWeight)
                        _finishedWeight = _totalWeight;

                    UpdateProgress();
                    TryAutoFinishIfDone();
                });
            }

            return new LoadingJob(weight, FinishImpl);
        }

        // =========================
        // Public API (AutoFinish)
        // =========================

        public void AutoFinish(Action<float> progressCallback, double durationMs = 220)
        {
            _autoFinishEnabled = true;
            _autoFinishCallback = progressCallback;
            _autoFinishDurationMs = Math.Max(0, durationMs);

            EnsureOnUI(TryAutoFinishIfDone);
        }

        // =========================
        // Public API (Manual close)
        // =========================

        public Task FadeOutAsync(double durationMs = 220, IEasingFunction? easing = null)
        {
            return EnsureOnUIAsync(() => FadeOutCoreAsync(durationMs, easing, progressCallback: null));
        }

        // =========================
        // Dependency Properties
        // =========================

        public static readonly DependencyProperty IsOpenProperty =
            DependencyProperty.Register(nameof(IsOpen), typeof(bool), typeof(LoadingCover),
                new PropertyMetadata(false, OnIsOpenChanged));

        public bool IsOpen
        {
            get => (bool)GetValue(IsOpenProperty);
            set => SetValue(IsOpenProperty, value);
        }

        public static readonly DependencyProperty IsIndeterminateProperty =
            DependencyProperty.Register(nameof(IsIndeterminate), typeof(bool), typeof(LoadingCover),
                new PropertyMetadata(true));

        public bool IsIndeterminate
        {
            get => (bool)GetValue(IsIndeterminateProperty);
            set => SetValue(IsIndeterminateProperty, value);
        }

        public static readonly DependencyProperty ProgressProperty =
            DependencyProperty.Register(nameof(Progress), typeof(double), typeof(LoadingCover),
                new PropertyMetadata(0.0, OnProgressChanged, CoerceProgress));

        public double Progress
        {
            get => (double)GetValue(ProgressProperty);
            set => SetValue(ProgressProperty, value);
        }

        public static readonly DependencyProperty MessageProperty =
            DependencyProperty.Register(nameof(Message), typeof(string), typeof(LoadingCover),
                new PropertyMetadata("Loading..."));

        public string Message
        {
            get => (string)GetValue(MessageProperty);
            set => SetValue(MessageProperty, value);
        }

        public static readonly DependencyProperty SubMessageProperty =
            DependencyProperty.Register(nameof(SubMessage), typeof(string), typeof(LoadingCover),
                new PropertyMetadata(""));

        public string SubMessage
        {
            get => (string)GetValue(SubMessageProperty);
            set => SetValue(SubMessageProperty, value);
        }

        public static readonly DependencyProperty OverlayBrushProperty =
            DependencyProperty.Register(nameof(OverlayBrush), typeof(Brush), typeof(LoadingCover),
                new PropertyMetadata(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0))));

        public Brush OverlayBrush
        {
            get => (Brush)GetValue(OverlayBrushProperty);
            set => SetValue(OverlayBrushProperty, value);
        }

        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(LoadingCover),
                new PropertyMetadata(new CornerRadius(12)));

        public CornerRadius CornerRadius
        {
            get => (CornerRadius)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        public static readonly DependencyProperty ContentPaddingProperty =
            DependencyProperty.Register(nameof(ContentPadding), typeof(Thickness), typeof(LoadingCover),
                new PropertyMetadata(new Thickness(28)));

        public Thickness ContentPadding
        {
            get => (Thickness)GetValue(ContentPaddingProperty);
            set => SetValue(ContentPaddingProperty, value);
        }

        private static readonly DependencyPropertyKey PercentTextPropertyKey =
            DependencyProperty.RegisterReadOnly(nameof(PercentText), typeof(string), typeof(LoadingCover),
                new PropertyMetadata("0%"));

        public static readonly DependencyProperty PercentTextProperty =
            PercentTextPropertyKey.DependencyProperty;

        public string PercentText
        {
            get => (string)GetValue(PercentTextProperty);
            private set => SetValue(PercentTextPropertyKey, value);
        }

        // =========================
        // Internals
        // =========================

        private float _totalWeight;
        private float _finishedWeight;

        private bool _autoFinishEnabled;
        private Action<float>? _autoFinishCallback;
        private double _autoFinishDurationMs = 220;
        private int _autoFinishTriggered;
        private float lastProgress = 0;

        private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var self = (LoadingCover)d;
            self.UpdateOpenState((bool)e.NewValue, immediate: false);
        }

        private void UpdateOpenState(bool open, bool immediate)
        {
            if (open)
            {
                Visibility = Visibility.Visible;

                if (Visibility != Visibility.Visible)
                    Visibility = Visibility.Visible;

                if (!_wasOpenLastTime)
                    ResetSessionState();

                _wasOpenLastTime = true;

                if (immediate)
                {
                    Opacity = 1.0;
                    BeginAnimation(OpacityProperty, null);
                    return;
                }

                BeginAnimation(OpacityProperty, null);
                BeginAnimation(OpacityProperty, new DoubleAnimation
                {
                    From = Math.Min(Opacity, 1.0),
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(160),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });
            }
            else
            {
                BeginAnimation(OpacityProperty, null);
                Opacity = 0.0;
                Visibility = Visibility.Collapsed;

                _wasOpenLastTime = false;
            }
        }

        private bool _wasOpenLastTime;

        private void ResetSessionState()
        {
            _totalWeight = 0f;
            _finishedWeight = 0f;
            _autoFinishTriggered = 0;

            Progress = 0.0;
            IsIndeterminate = true;
            PercentText = "0%";
        }

        private void UpdateProgress()
        {
            if (_totalWeight <= 0f)
            {
                Progress = 0.0;
                PercentText = "0%";
                return;
            }

            var t = _finishedWeight / _totalWeight;
            if (t < 0f) t = 0f;
            if (t > 1f) t = 1f;

            float progress = t * 100.0f;
            if (progress < lastProgress) return;

            lastProgress = progress;
            Progress = lastProgress;
        }

        private void Reset()
        {
            _totalWeight = 0f;
            _finishedWeight = 0f;
            lastProgress = 0;
        }

        private void TryAutoFinishIfDone()
        {
            if (!_autoFinishEnabled)
                return;

            if (_totalWeight <= 0f)
                return;

            if (_finishedWeight < _totalWeight)
                return;

            if (Interlocked.Exchange(ref _autoFinishTriggered, 1) != 0)
                return;

            _ = FadeOutCoreAsync(_autoFinishDurationMs,
                easing: new CubicEase { EasingMode = EasingMode.EaseInOut },
                progressCallback: _autoFinishCallback);
        }

        private async Task FadeOutCoreAsync(double durationMs, IEasingFunction? easing, Action<float>? progressCallback)
        {
            if (!IsOpen)
                return;

            durationMs = Math.Max(0, durationMs);
            easing ??= new CubicEase { EasingMode = EasingMode.EaseInOut };

            if (durationMs <= 0)
            {
                progressCallback?.Invoke(0f);
                Opacity = 0.0;
                IsOpen = false;
                progressCallback?.Invoke(1f);
                return;
            }

            var tcs = new TaskCompletionSource<object?>();
            var sw = Stopwatch.StartNew();

            var tick = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(1000.0 / 60.0)
            };

            tick.Tick += (_, __) =>
            {
                var t = (float)(sw.Elapsed.TotalMilliseconds / durationMs);
                if (t < 0f) t = 0f;
                if (t > 1f) t = 1f;
                progressCallback?.Invoke(t);
            };

            tick.Start();

            var anim = new DoubleAnimation
            {
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = easing,
                FillBehavior = FillBehavior.Stop
            };

            anim.Completed += (_, __) =>
            {
                tick.Stop();
                progressCallback?.Invoke(1f);

                Opacity = 0.0;
                IsOpen = false;

                tcs.TrySetResult(null);
            };

            BeginAnimation(OpacityProperty, anim);

            await tcs.Task;
        }

        private static object CoerceProgress(DependencyObject d, object baseValue)
        {
            var v = (double)baseValue;
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0.0;
            if (v < 0.0) return 0.0;
            if (v > 100.0) return 100.0;
            return v;
        }

        private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var self = (LoadingCover)d;
            var p = (double)e.NewValue;
            self.PercentText = $"{(int)Math.Round(p)}%";
        }

        private void EnsureOnUI(Action action)
        {
            if (Dispatcher.CheckAccess())
            {
                action();
                return;
            }
            try
            {
                Dispatcher.Invoke(action);
            }
            catch { }
        }

        private Task EnsureOnUIAsync(Func<Task> action)
        {
            if (Dispatcher.CheckAccess())
                return action();

            return Dispatcher.InvokeAsync(action).Task.Unwrap();
        }
    }
}
