using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Base.UI.Controls;

/// <summary>
/// A self-contained HSV colour picker: an outer hue ring around an inner
/// saturation/value square. Original implementation - no third-party code.
///
/// Bind or read <see cref="SelectedColor"/> (two-way by default), or listen to
/// <see cref="SelectedColorChanged"/> which fires continuously while dragging.
/// The control is square; it centres itself in whatever space it is given.
/// </summary>
public partial class HsvColorWheel : UserControl
{
    // Current colour in HSV: hue 0..360, saturation/value 0..1.
    private double hue;
    private double saturation = 1.0;
    private double value = 1.0;

    // Set while we push a colour to SelectedColor ourselves, so the property
    // callback doesn't fight the interactive edit.
    private bool suppressColorCallback;

    private enum DragTarget { None, Hue, Square }
    private DragTarget drag = DragTarget.None;

    // Geometry (in this control's coordinate space), recomputed on resize.
    private double originX, originY;   // top-left of the centred square area
    private double centerX, centerY;   // ring/square centre
    private double outerRadius, innerRadius;
    private double squareLeft, squareTop, squareSide;

    private const double ThumbSize = 14.0;
    private const double thickness = 12.0;
    private const int MaxBitmapPixels = 1024; // guard against huge allocations

    public HsvColorWheel()
    {
        InitializeComponent();
        ColorToHsv(SelectedColor, out hue, out saturation, out value);
    }

    #region SelectedColor

    public static readonly DependencyProperty SelectedColorProperty =
        DependencyProperty.Register(
            nameof(SelectedColor),
            typeof(Color),
            typeof(HsvColorWheel),
            new FrameworkPropertyMetadata(
                Colors.Red,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedColorChanged));

    /// <summary>The currently selected colour. Two-way bindable.</summary>
    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    /// <summary>Raised whenever the selected colour changes, including mid-drag.</summary>
    public event EventHandler<Color> SelectedColorChanged;

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((HsvColorWheel)d).OnSelectedColorChanged((Color)e.NewValue);

    private void OnSelectedColorChanged(Color newColor)
    {
        if (suppressColorCallback)
            return;

        // Externally assigned: sync HSV state and repaint.
        ColorToHsv(newColor, out hue, out saturation, out value);
        RenderSquare();
        UpdateThumbs();
    }

    #endregion

    #region Layout & rendering

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e) => Relayout();

    private void Relayout()
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0 || double.IsNaN(size))
            return;

        const double pad = 8.0;
        originX = (ActualWidth - size) / 2.0;
        originY = (ActualHeight - size) / 2.0;
        centerX = size / 2.0;
        centerY = size / 2.0;
        outerRadius = (size / 2.0) - pad;
        innerRadius = Math.Max(1.0, outerRadius - thickness);
        squareSide = (innerRadius - 6) * Math.Sqrt(2.0) * 0.98;
        squareLeft = centerX - (squareSide / 2.0);
        squareTop = centerY - (squareSide / 2.0);

        // Position the two images on the canvas (bitmaps are stretched to fit).
        Canvas.SetLeft(HueRingImage, originX);
        Canvas.SetTop(HueRingImage, originY);
        HueRingImage.Width = size;
        HueRingImage.Height = size;

        Canvas.SetLeft(SvSquareImage, originX + squareLeft);
        Canvas.SetTop(SvSquareImage, originY + squareTop);
        SvSquareImage.Width = squareSide;
        SvSquareImage.Height = squareSide;

        RenderRing(size);
        RenderSquare();
        UpdateThumbs();
    }

    private void RenderRing(double size)
    {
        int px = Math.Min(MaxBitmapPixels, Math.Max(1, (int)Math.Round(size)));
        double scale = px / size;                 // bitmap pixels per DIP
        double cx = centerX * scale;
        double cy = centerY * scale;
        double rOut = outerRadius * scale;
        double rIn = innerRadius * scale;

        byte[] buffer = new byte[px * px * 4];
        for (int y = 0; y < px; y++)
        {
            for (int x = 0; x < px; x++)
            {
                double dx = x + 0.5 - cx;
                double dy = y + 0.5 - cy;
                double dist = Math.Sqrt((dx * dx) + (dy * dy));

                double alpha = RingCoverage(dist, rIn, rOut);
                int i = ((y * px) + x) * 4;
                if (alpha <= 0)
                {
                    // leave transparent (buffer already zeroed)
                    continue;
                }

                double angle = (Math.Atan2(dy, dx) * 180.0 / Math.PI);
                if (angle < 0) angle += 360.0;

                (byte r, byte g, byte b) = HsvToRgb(angle, 1.0, 1.0);
                byte a = (byte)Math.Clamp((int)Math.Round(alpha * 255.0), 0, 255);
                buffer[i + 0] = b;
                buffer[i + 1] = g;
                buffer[i + 2] = r;
                buffer[i + 3] = a;
            }
        }

        HueRingImage.Source = ToBitmap(buffer, px, px);
    }

    // 1px linear feather on both edges of the ring band.
    private static double RingCoverage(double dist, double rIn, double rOut)
    {
        if (dist > rOut) return Math.Max(0.0, 1.0 - (dist - rOut));
        if (dist < rIn) return Math.Max(0.0, 1.0 - (rIn - dist));
        return 1.0;
    }

    private void RenderSquare()
    {
        if (squareSide <= 0)
            return;

        int px = Math.Min(MaxBitmapPixels, Math.Max(1, (int)Math.Round(squareSide)));
        byte[] buffer = new byte[px * px * 4];
        double denom = Math.Max(1, px - 1);

        for (int y = 0; y < px; y++)
        {
            double v = 1.0 - (y / denom);         // top = bright
            for (int x = 0; x < px; x++)
            {
                double s = x / denom;             // left = unsaturated
                (byte r, byte g, byte b) = HsvToRgb(hue, s, v);
                int i = ((y * px) + x) * 4;
                buffer[i + 0] = b;
                buffer[i + 1] = g;
                buffer[i + 2] = r;
                buffer[i + 3] = 255;
            }
        }

        SvSquareImage.Source = ToBitmap(buffer, px, px);
    }

    private static WriteableBitmap ToBitmap(byte[] bgra, int w, int h)
    {
        var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        bmp.WritePixels(new Int32Rect(0, 0, w, h), bgra, w * 4, 0);
        return bmp;
    }

    private void UpdateThumbs()
    {
        if (outerRadius <= 0)
            return;

        double midR = (innerRadius + outerRadius) / 2.0;
        double hueRad = hue * Math.PI / 180.0;
        double hx = originX + centerX + (midR * Math.Cos(hueRad));
        double hy = originY + centerY + (midR * Math.Sin(hueRad));
        Canvas.SetLeft(HueThumb, hx - (ThumbSize / 2.0));
        Canvas.SetTop(HueThumb, hy - (ThumbSize / 2.0));

        double sx = originX + squareLeft + (saturation * squareSide);
        double sy = originY + squareTop + ((1.0 - value) * squareSide);
        Canvas.SetLeft(SvThumb, sx - (ThumbSize / 2.0));
        Canvas.SetTop(SvThumb, sy - (ThumbSize / 2.0));
    }

    #endregion

    #region Input

    private void Root_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Point p = e.GetPosition(Root);
        double lx = p.X - originX;
        double ly = p.Y - originY;
        double dist = Math.Sqrt(Math.Pow(lx - centerX, 2) + Math.Pow(ly - centerY, 2));

        if (dist >= innerRadius - 1 && dist <= outerRadius + 1)
            drag = DragTarget.Hue;
        else if (lx >= squareLeft && lx <= squareLeft + squareSide &&
                 ly >= squareTop && ly <= squareTop + squareSide)
            drag = DragTarget.Square;
        else
            return;

        Root.CaptureMouse();
        ApplyDrag(lx, ly);
        e.Handled = true;
    }

    private void Root_MouseMove(object sender, MouseEventArgs e)
    {
        if (drag == DragTarget.None)
            return;

        Point p = e.GetPosition(Root);
        ApplyDrag(p.X - originX, p.Y - originY);
    }

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (drag == DragTarget.None)
            return;
        drag = DragTarget.None;
        Root.ReleaseMouseCapture();
    }

    private void ApplyDrag(double lx, double ly)
    {
        if (drag == DragTarget.Hue)
        {
            double angle = Math.Atan2(ly - centerY, lx - centerX) * 180.0 / Math.PI;
            if (angle < 0) angle += 360.0;
            hue = angle;
            RenderSquare();       // hue changed -> the SV square recolours
        }
        else if (drag == DragTarget.Square)
        {
            saturation = Clamp01((lx - squareLeft) / squareSide);
            value = 1.0 - Clamp01((ly - squareTop) / squareSide);
        }
        else
        {
            return;
        }

        UpdateThumbs();
        CommitColor();
    }

    private void CommitColor()
    {
        (byte r, byte g, byte b) = HsvToRgb(hue, saturation, value);
        Color color = Color.FromRgb(r, g, b);

        suppressColorCallback = true;
        SelectedColor = color;
        suppressColorCallback = false;

        SelectedColorChanged?.Invoke(this, color);
    }

    #endregion

    #region Colour maths

    private static double Clamp01(double x) => x < 0 ? 0 : (x > 1 ? 1 : x);

    private static (byte r, byte g, byte b) HsvToRgb(double h, double s, double v)
    {
        h = ((h % 360.0) + 360.0) % 360.0;
        s = Clamp01(s);
        v = Clamp01(v);

        double c = v * s;
        double x = c * (1.0 - Math.Abs(((h / 60.0) % 2.0) - 1.0));
        double m = v - c;

        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return (ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    private static void ColorToHsv(Color color, out double h, out double s, out double v)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        if (delta <= 0)
            h = 0;
        else if (max == r)
            h = 60.0 * ((((g - b) / delta) % 6.0 + 6.0) % 6.0);
        else if (max == g)
            h = 60.0 * (((b - r) / delta) + 2.0);
        else
            h = 60.0 * (((r - g) / delta) + 4.0);

        s = max <= 0 ? 0 : delta / max;
        v = max;
    }

    private static byte ToByte(double unit)
        => (byte)Math.Clamp((int)Math.Round(unit * 255.0), 0, 255);

    #endregion
}
