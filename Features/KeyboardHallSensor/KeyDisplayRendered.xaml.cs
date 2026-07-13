using System.Windows.Controls;
using System.Windows.Media;

namespace KeyboardHallSensor;
/// <summary>
/// Interaction logic for KeyDisplayRendered.xaml
/// </summary>
public partial class KeyDisplayRendered : UserControl
{
    public byte Keycode { get; private set; }
    public string Label { get; private set; }
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }

    // Reused for every color update so the 40 FPS animation doesn't allocate a
    // fresh brush per key per frame. Per-frame allocations churned gen-0 GC and
    // caused the occasional single-frame stutter.
    private readonly SolidColorBrush brush = new(Colors.Black);
    private bool brushAttached;

    public KeyDisplayRendered(byte keycode, float w, float h, string label = "")
    {
        InitializeComponent();

        Width = w;
        Height = h;
        Keycode = keycode;
        Label = label;

        LabelText.Text = label;
        if(label.Length == 1) LabelText.FontSize = 16;

        if (label.Length > 0) LabelText.Foreground = brush;
        else KeySurface.Background = brush;
    }

    public void SetText(string text)
    {
        LabelText.Text = text;
    }

    public void SetColor(byte r, byte g, byte b)
    {
        // Skip redundant updates: most keys keep the same color frame-to-frame.
        if (brushAttached && R == r && G == g && B == b)
            return;

        R = r;
        G = g;
        B = b;

        // Mutating the existing brush's Color repaints without allocating.
        brush.Color = Color.FromRgb(r, g, b);

        if (!brushAttached)
        {
            LabelText.Foreground = brush;
            brushAttached = true;
        }
    }
    public byte[] GetColorBytes()
    {
        return [R, G, B];
    }
}
