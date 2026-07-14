using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Base.UI.Controls;

/// <summary>
/// A self-contained colour picker: a hue/saturation disc (angle = hue,
/// radius = saturation) with a vertical value slider, RGB/HSV tabs of
/// slider+box rows, and an editable HEX field. Original implementation - no
/// third-party code. Bind or read <see cref="SelectedColor"/> (two-way), or
/// listen to <see cref="SelectedColorChanged"/> (fires while dragging).
/// </summary>
public partial class ColorPicker : UserControl
{
    // Source of truth. Hue 0..360, saturation/value 0..1.
    private double hue;
    private double saturation = 1.0;
    private double value = 1.0;

    // Guards against re-entrant updates while we push values into the widgets.
    private bool syncing;
    // Set while we write SelectedColor ourselves, so the callback ignores it.
    private bool suppressColorCallback;

    // Disc geometry (control space), recomputed on resize.
    private double discCenterX, discCenterY, discRadius;
    private double lastDiscValue = double.NaN;
    private bool draggingDisc;

    // Value slider geometry.
    private double valueTop, valueHeight;
    private bool draggingValue;

    private const double ThumbSize = 14.0;
    private const int MaxBitmapPixels = 1024;

    // Reused so the knob fills don't allocate a brush on every move.
    private readonly SolidColorBrush discThumbFill = new(Colors.Transparent);
    private readonly SolidColorBrush valueThumbFill = new(Colors.Transparent);

    public ColorPicker()
    {
        InitializeComponent();
        DiscThumb.Fill = discThumbFill;
        ValueThumb.Background = valueThumbFill;
        ColorToHsv(SelectedColor, out hue, out saturation, out value);
        SetMode(rgb: true);
        Loaded += (_, _) => Sync(sourceIsWidget: false);
    }

    #region SelectedColor

    public static readonly DependencyProperty SelectedColorProperty =
        DependencyProperty.Register(
            nameof(SelectedColor),
            typeof(Color),
            typeof(ColorPicker),
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
        => ((ColorPicker)d).OnSelectedColorChanged((Color)e.NewValue);

    private void OnSelectedColorChanged(Color newColor)
    {
        if (suppressColorCallback)
            return;
        ColorToHsv(newColor, out hue, out saturation, out value);
        Sync(sourceIsWidget: false);
    }

    #endregion

    #region Central sync

    // Pushes the current HSV state into every widget and the SelectedColor
    // property, then repaints. The 'syncing' guard stops the slider ValueChanged
    // handlers from recursing while we assign their values.
    private void Sync(bool sourceIsWidget)
    {
        if (syncing)
            return;
        syncing = true;
        try
        {
            (byte r, byte g, byte b) = HsvToRgb(hue, saturation, value);

            RSlider.Value = r;
            GSlider.Value = g;
            BSlider.Value = b;
            HSlider.Value = hue;
            SSlider.Value = saturation * 100.0;
            VSlider.Value = value * 100.0;

            RBox.Text = r.ToString(CultureInfo.InvariantCulture);
            GBox.Text = g.ToString(CultureInfo.InvariantCulture);
            BBox.Text = b.ToString(CultureInfo.InvariantCulture);
            HBox.Text = hue.ToString("0.##", CultureInfo.InvariantCulture);
            SBox.Text = Math.Round(saturation * 100.0).ToString(CultureInfo.InvariantCulture);
            VBox.Text = Math.Round(value * 100.0).ToString(CultureInfo.InvariantCulture);
            HexBox.Text = $"#{r:X2}{g:X2}{b:X2}";

            UpdateSliderGradients();

            Color color = Color.FromRgb(r, g, b);
            suppressColorCallback = true;
            SelectedColor = color;
            suppressColorCallback = false;

            SelectedColorChanged?.Invoke(this, color);
        }
        finally
        {
            syncing = false;
        }

        RepaintVisuals();
    }

    private void RepaintVisuals()
    {
        // Disc colour only depends on value, so only re-render when it changes.
        // (lastDiscValue starts as NaN, so the first paint always renders.)
        if (discRadius > 0 && value != lastDiscValue)
        {
            RenderDisc();
            lastDiscValue = value;
        }
        UpdateValueBar();
        UpdateThumbs();
    }

    #endregion

    #region Disc

    private void Disc_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double size = Math.Min(DiscRoot.ActualWidth, DiscRoot.ActualHeight);
        if (size <= 0)
            return;

        discCenterX = DiscRoot.ActualWidth / 2.0;
        discCenterY = DiscRoot.ActualHeight / 2.0;
        discRadius = (size / 2.0) - (ThumbSize / 2.0);

        Canvas.SetLeft(DiscImage, discCenterX - discRadius);
        Canvas.SetTop(DiscImage, discCenterY - discRadius);
        DiscImage.Width = discRadius * 2.0;
        DiscImage.Height = discRadius * 2.0;

        lastDiscValue = double.NaN; // force a re-render at the new size
        RepaintVisuals();
    }

    private void RenderDisc()
    {
        if (discRadius <= 0)
            return;

        int px = Math.Min(MaxBitmapPixels, Math.Max(1, (int)Math.Round(discRadius * 2.0)));
        double c = px / 2.0;
        double rPix = px / 2.0;

        byte[] buffer = new byte[px * px * 4];
        for (int y = 0; y < px; y++)
        {
            for (int x = 0; x < px; x++)
            {
                double dx = x + 0.5 - c;
                double dy = y + 0.5 - c;
                double dist = Math.Sqrt((dx * dx) + (dy * dy));

                double coverage = rPix - dist; // >1 inside, feather over last px
                if (coverage <= 0)
                    continue;
                double alpha = coverage >= 1 ? 1.0 : coverage;

                double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                if (angle < 0) angle += 360.0;
                double sat = Math.Min(1.0, dist / rPix);

                (byte r, byte g, byte b) = HsvToRgb(angle, sat, value);
                int i = ((y * px) + x) * 4;
                buffer[i + 0] = b;
                buffer[i + 1] = g;
                buffer[i + 2] = r;
                buffer[i + 3] = (byte)Math.Clamp((int)Math.Round(alpha * 255.0), 0, 255);
            }
        }

        DiscImage.Source = ToBitmap(buffer, px, px);
    }

    private void Disc_Down(object sender, MouseButtonEventArgs e)
    {
        draggingDisc = true;
        DiscRoot.CaptureMouse();
        ApplyDisc(e.GetPosition(DiscRoot));
        e.Handled = true;
    }

    private void Disc_Move(object sender, MouseEventArgs e)
    {
        if (draggingDisc)
            ApplyDisc(e.GetPosition(DiscRoot));
    }

    private void Disc_Up(object sender, MouseButtonEventArgs e)
    {
        if (!draggingDisc)
            return;
        draggingDisc = false;
        DiscRoot.ReleaseMouseCapture();
    }

    private void ApplyDisc(Point p)
    {
        double dx = p.X - discCenterX;
        double dy = p.Y - discCenterY;
        double dist = Math.Sqrt((dx * dx) + (dy * dy));

        double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        if (angle < 0) angle += 360.0;

        hue = angle;
        saturation = discRadius > 0 ? Math.Min(1.0, dist / discRadius) : 0;
        Sync(sourceIsWidget: true);
    }

    #endregion

    #region Value slider

    private void Value_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        valueTop = 0;
        valueHeight = ValueRoot.ActualHeight;
        ValueBar.Height = valueHeight;
        UpdateValueBar();
        UpdateThumbs();
    }

    private void UpdateValueBar()
    {
        // Top = full value for the current hue/sat, bottom = black.
        (byte r, byte g, byte b) = HsvToRgb(hue, saturation, 1.0);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(r, g, b), 0));
        brush.GradientStops.Add(new GradientStop(Colors.Black, 1));
        ValueBar.Background = brush;
    }

    private void Value_Down(object sender, MouseButtonEventArgs e)
    {
        draggingValue = true;
        ValueRoot.CaptureMouse();
        ApplyValue(e.GetPosition(ValueRoot));
        e.Handled = true;
    }

    private void Value_Move(object sender, MouseEventArgs e)
    {
        if (draggingValue)
            ApplyValue(e.GetPosition(ValueRoot));
    }

    private void Value_Up(object sender, MouseButtonEventArgs e)
    {
        if (!draggingValue)
            return;
        draggingValue = false;
        ValueRoot.ReleaseMouseCapture();
    }

    private void ApplyValue(Point p)
    {
        if (valueHeight <= 0)
            return;
        value = Clamp01(1.0 - ((p.Y - valueTop) / valueHeight));
        Sync(sourceIsWidget: true);
    }

    #endregion

    #region Thumbs

    private void UpdateThumbs()
    {
        // Fill both knobs with the current colour (no allocation).
        (byte cr, byte cg, byte cb) = HsvToRgb(hue, saturation, value);
        Color current = Color.FromRgb(cr, cg, cb);
        discThumbFill.Color = current;
        valueThumbFill.Color = current;

        if (discRadius > 0)
        {
            double r = saturation * discRadius;
            double a = hue * Math.PI / 180.0;
            double x = discCenterX + (r * Math.Cos(a));
            double y = discCenterY + (r * Math.Sin(a));
            Canvas.SetLeft(DiscThumb, x - (ThumbSize / 2.0));
            Canvas.SetTop(DiscThumb, y - (ThumbSize / 2.0));
        }

        if (valueHeight > 0)
        {
            double y = valueTop + ((1.0 - value) * valueHeight);
            Canvas.SetTop(ValueThumb, y - (ValueThumb.Height / 2.0));
        }
    }

    #endregion

    #region Widget handlers

    private void RgbMode_Click(object sender, RoutedEventArgs e) => SetMode(rgb: true);

    private void HsvMode_Click(object sender, RoutedEventArgs e) => SetMode(rgb: false);

    private void SetMode(bool rgb)
    {
        RgbPanel.Visibility = rgb ? Visibility.Visible : Visibility.Collapsed;
        HsvPanel.Visibility = rgb ? Visibility.Collapsed : Visibility.Visible;
        RgbModeButton.IsChecked = rgb;
        HsvModeButton.IsChecked = !rgb;
    }

    private void RgbSliders_Changed(object sender, EventArgs e)
    {
        if (syncing)
            return;
        var color = Color.FromRgb((byte)RSlider.Value, (byte)GSlider.Value, (byte)BSlider.Value);
        ColorToHsv(color, out hue, out saturation, out value);
        Sync(sourceIsWidget: true);
    }

    private void HsvSliders_Changed(object sender, EventArgs e)
    {
        if (syncing)
            return;
        hue = HSlider.Value;
        saturation = SSlider.Value / 100.0;
        value = VSlider.Value / 100.0;
        Sync(sourceIsWidget: true);
    }

    // Recolours each channel slider's track from the current colour: two marks per
    // RGB/S/V channel, and a full rainbow (7 marks) for hue.
    private void UpdateSliderGradients()
    {
        (byte r, byte g, byte b) = HsvToRgb(hue, saturation, value);

        RSlider.SetMarks(new GradientMark(0, Color.FromRgb(0, g, b)), new GradientMark(1, Color.FromRgb(255, g, b)));
        GSlider.SetMarks(new GradientMark(0, Color.FromRgb(r, 0, b)), new GradientMark(1, Color.FromRgb(r, 255, b)));
        BSlider.SetMarks(new GradientMark(0, Color.FromRgb(r, g, 0)), new GradientMark(1, Color.FromRgb(r, g, 255)));

        var hueMarks = new GradientMark[7];
        for (int i = 0; i < hueMarks.Length; i++)
        {
            (byte hr, byte hg, byte hb) = HsvToRgb(i * 60.0, 1.0, 1.0);
            hueMarks[i] = new GradientMark(i / 6.0, Color.FromRgb(hr, hg, hb));
        }
        HSlider.SetMarks(hueMarks);

        (byte s0r, byte s0g, byte s0b) = HsvToRgb(hue, 0.0, value);
        (byte s1r, byte s1g, byte s1b) = HsvToRgb(hue, 1.0, value);
        SSlider.SetMarks(new GradientMark(0, Color.FromRgb(s0r, s0g, s0b)), new GradientMark(1, Color.FromRgb(s1r, s1g, s1b)));

        (byte v1r, byte v1g, byte v1b) = HsvToRgb(hue, saturation, 1.0);
        VSlider.SetMarks(new GradientMark(0, Colors.Black), new GradientMark(1, Color.FromRgb(v1r, v1g, v1b)));
    }

    private void RgbBoxes_Commit(object sender, RoutedEventArgs e)
    {
        int r = ParseByte(RBox.Text, (int)Math.Round(RSlider.Value));
        int g = ParseByte(GBox.Text, (int)Math.Round(GSlider.Value));
        int b = ParseByte(BBox.Text, (int)Math.Round(BSlider.Value));
        ColorToHsv(Color.FromRgb((byte)r, (byte)g, (byte)b), out hue, out saturation, out value);
        Sync(sourceIsWidget: true);
    }

    private void HsvBoxes_Commit(object sender, RoutedEventArgs e)
    {
        hue = ((ParseDouble(HBox.Text, hue) % 360.0) + 360.0) % 360.0;
        saturation = Clamp01(ParseDouble(SBox.Text, saturation * 100.0) / 100.0);
        value = Clamp01(ParseDouble(VBox.Text, value * 100.0) / 100.0);
        Sync(sourceIsWidget: true);
    }

    private void Hex_Commit(object sender, RoutedEventArgs e)
    {
        if (TryParseHex(HexBox.Text, out Color color))
            ColorToHsv(color, out hue, out saturation, out value);
        Sync(sourceIsWidget: true);
    }

    private void Box_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is UIElement el)
        {
            // Move focus away so the LostFocus commit runs.
            el.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
        }
    }

    #endregion

    #region Parsing / colour maths

    private static double Clamp01(double x) => x < 0 ? 0 : (x > 1 ? 1 : x);

    private static int ParseByte(string s, int fallback)
        => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
            ? Math.Clamp(v, 0, 255)
            : fallback;

    private static double ParseDouble(string s, double fallback)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
            ? v
            : fallback;

    private static bool TryParseHex(string s, out Color color)
    {
        color = Colors.Black;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        s = s.Trim().TrimStart('#');
        if (s.Length != 6)
            return false;

        if (!byte.TryParse(s.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r) ||
            !byte.TryParse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g) ||
            !byte.TryParse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
            return false;

        color = Color.FromRgb(r, g, b);
        return true;
    }

    private static WriteableBitmap ToBitmap(byte[] bgra, int w, int h)
    {
        var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        bmp.WritePixels(new Int32Rect(0, 0, w, h), bgra, w * 4, 0);
        return bmp;
    }

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
            h = 60.0 * (((((g - b) / delta) % 6.0) + 6.0) % 6.0);
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
