using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Base.UI.Controls;

/// <summary>A single colour stop on a <see cref="GradientSlider"/> track.</summary>
public sealed class GradientMark
{
    /// <summary>Position along the track, 0 (left) .. 1 (right).</summary>
    public double Offset { get; set; }

    public Color Color { get; set; }

    public GradientMark() { }

    public GradientMark(double offset, Color color)
    {
        Offset = offset;
        Color = color;
    }
}

/// <summary>
/// A horizontal value slider whose track is painted by a set of colour
/// <see cref="Marks"/> (a gradient with arbitrarily many stops). Original
/// implementation - no third-party code.
///
/// Set <see cref="Minimum"/>/<see cref="Maximum"/>/<see cref="Value"/> like a
/// normal slider; define the track colours via <see cref="SetMarks"/> or by
/// editing <see cref="Marks"/>. Two marks give a simple A-to-B bar; more marks
/// give a multi-stop gradient (e.g. a hue rainbow) for later gradient editing.
/// </summary>
public partial class GradientSlider : UserControl
{
    private bool dragging;
    private bool suspendRebuild;

    private double trackLeft, trackWidth, trackTop;
    // The knob centre travels between the track's two corner-arc centres.
    private double travelLeft, travelWidth;
    private const double TrackHeight = 8.0;

    // Reused so the knob fill doesn't allocate a brush on every move.
    private readonly SolidColorBrush thumbFill = new(Colors.Transparent);

    public GradientSlider()
    {
        InitializeComponent();
        Thumb.Fill = thumbFill;
        Marks = new ObservableCollection<GradientMark>();
        Marks.CollectionChanged += Marks_CollectionChanged;
        RebuildTrackBrush();
    }

    #region Dependency properties

    public static readonly DependencyProperty MinimumProperty =
        DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(GradientSlider),
            new FrameworkPropertyMetadata(0.0, OnRangeChanged));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(GradientSlider),
            new FrameworkPropertyMetadata(100.0, OnRangeChanged));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(GradientSlider),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }

    /// <summary>Raised whenever <see cref="Value"/> changes (including while dragging).</summary>
    public event EventHandler ValueChanged;

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((GradientSlider)d).UpdateThumb();

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var slider = (GradientSlider)d;
        double newValue = (double)e.NewValue;
        double clamped = Clamp(newValue, slider.Minimum, slider.Maximum);
        if (clamped != newValue)
        {
            slider.Value = clamped; // re-enters once, then falls through below
            return;
        }
        slider.UpdateThumb();
        slider.ValueChanged?.Invoke(slider, EventArgs.Empty);
    }

    #endregion

    #region Marks

    /// <summary>The colour stops painting the track. Edit or replace to recolour.</summary>
    public ObservableCollection<GradientMark> Marks { get; }

    /// <summary>Replaces all marks in one shot and repaints the track once.</summary>
    public void SetMarks(params GradientMark[] marks)
    {
        suspendRebuild = true;
        Marks.Clear();
        if (marks != null)
        {
            foreach (GradientMark m in marks)
                Marks.Add(m);
        }
        suspendRebuild = false;
        RebuildTrackBrush();
    }

    private void Marks_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (!suspendRebuild)
            RebuildTrackBrush();
    }

    private void RebuildTrackBrush()
    {
        if (Marks == null || Marks.Count == 0)
        {
            Track.Background = Brushes.Transparent;
            return;
        }

        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
        };
        foreach (GradientMark m in Marks.OrderBy(m => m.Offset))
            brush.GradientStops.Add(new GradientStop(m.Color, Clamp01(m.Offset)));

        Track.Background = brush;

        // Marks changed -> refresh the knob's fill colour.
        UpdateThumb();
    }

    // The colour of the gradient at a given fraction (0..1) of the track.
    private Color SampleColor(double frac)
    {
        if (Marks == null || Marks.Count == 0)
            return Colors.Transparent;

        var sorted = Marks.OrderBy(m => m.Offset).ToList();
        frac = Clamp01(frac);

        if (frac <= sorted[0].Offset)
            return sorted[0].Color;
        GradientMark last = sorted[^1];
        if (frac >= last.Offset)
            return last.Color;

        for (int i = 0; i < sorted.Count - 1; i++)
        {
            GradientMark a = sorted[i];
            GradientMark b = sorted[i + 1];
            if (frac >= a.Offset && frac <= b.Offset)
            {
                double span = b.Offset - a.Offset;
                double t = span > 0 ? (frac - a.Offset) / span : 0.0;
                return LerpColor(a.Color, b.Color, t);
            }
        }
        return last.Color;
    }

    private static Color LerpColor(Color a, Color b, double t)
    {
        byte L(byte x, byte y) => (byte)Math.Round(x + ((y - x) * t));
        return Color.FromRgb(L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }

    #endregion

    #region Layout & interaction

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e) => Relayout();

    private void Relayout()
    {
        double w = Root.ActualWidth;
        double h = Root.ActualHeight;
        if (w <= 0 || h <= 0)
            return;

        double knobRadius = Thumb.Width / 2.0;
        double cornerRadius = Track.CornerRadius.TopLeft;

        // Inset the track just enough that the knob (centred on a corner-arc
        // centre at each end) doesn't overhang the control edge.
        double inset = Math.Max(0.0, knobRadius - cornerRadius);
        trackLeft = inset;
        trackWidth = Math.Max(0.0, w - (2.0 * inset));
        trackTop = (h - TrackHeight) / 2.0;

        Canvas.SetLeft(Track, trackLeft);
        Canvas.SetTop(Track, trackTop);
        Track.Width = trackWidth;
        Track.Height = TrackHeight;

        // Knob centre runs from the left corner centre to the right corner centre.
        travelLeft = trackLeft + cornerRadius;
        travelWidth = Math.Max(0.0, trackWidth - (2.0 * cornerRadius));

        UpdateThumb();
    }

    private void UpdateThumb()
    {
        double range = Maximum - Minimum;
        double frac = range > 0 ? Clamp01((Value - Minimum) / range) : 0.0;

        // Fill the knob with the gradient colour at its position (no allocation).
        thumbFill.Color = SampleColor(frac);

        if (travelWidth <= 0)
            return;

        double cx = travelLeft + (frac * travelWidth);
        double cy = Root.ActualHeight / 2.0;
        Canvas.SetLeft(Thumb, cx - (Thumb.Width / 2.0));
        Canvas.SetTop(Thumb, cy - (Thumb.Height / 2.0));
    }

    private void Root_Down(object sender, MouseButtonEventArgs e)
    {
        dragging = true;
        Root.CaptureMouse();
        SetValueFromX(e.GetPosition(Root).X);
        e.Handled = true;
    }

    private void Root_Move(object sender, MouseEventArgs e)
    {
        if (dragging)
            SetValueFromX(e.GetPosition(Root).X);
    }

    private void Root_Up(object sender, MouseButtonEventArgs e)
    {
        if (!dragging)
            return;
        dragging = false;
        Root.ReleaseMouseCapture();
    }

    private void SetValueFromX(double x)
    {
        if (travelWidth <= 0)
            return;
        double frac = Clamp01((x - travelLeft) / travelWidth);
        Value = Minimum + (frac * (Maximum - Minimum));
    }

    #endregion

    private static double Clamp(double x, double lo, double hi)
    {
        if (hi < lo) (lo, hi) = (hi, lo);
        return x < lo ? lo : (x > hi ? hi : x);
    }

    private static double Clamp01(double x) => x < 0 ? 0 : (x > 1 ? 1 : x);
}
