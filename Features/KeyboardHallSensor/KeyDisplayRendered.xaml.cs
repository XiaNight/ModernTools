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

    public KeyDisplayRendered(byte keycode, float w, float h, string label = "")
    {
        InitializeComponent();

        Width = w;
        Height = h;
        Keycode = keycode;
        Label = label;

        label = label.Replace("L-", "").Replace("R-", "");
        if(label == "") label = "-----";

        string[] splits = label.Split(new[] { "\\n" }, StringSplitOptions.None);
        if (splits.Length == 1)
        {
            LabelText.Text = label;
            if(label.Length == 1) LabelText.FontSize = 16;
        }
        else
        {
            LabelText.Text = $"{splits[1]}  {splits[0]}";
        }
    }

    public void SetText(string text)
    {
        LabelText.Text = text;
    }

    public void SetColor(byte r, byte g, byte b)
    {
        R = r;
        G = g;
        B = b;
        Color color = Color.FromRgb(r, g, b);
        LabelText.Foreground = new SolidColorBrush(color);
    }
    public byte[] GetColorBytes()
    {
        return [R, G, B];
    }
}
