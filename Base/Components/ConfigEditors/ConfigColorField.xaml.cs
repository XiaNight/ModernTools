using Base.UI.Controls;
using System.Security.Policy;
using System.Windows.Controls;
using ModernWpf;
using ModernWpf.Controls;
using Windows.UI.Popups;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Base.Components;
/// <summary>
/// Interaction logic for ConfigColorField.xaml
/// </summary>
public partial class ConfigColorField : UserControl, IConfigEditor
{
    private ConfigItem _item;
    private SolidColorBrush previewBrush;

    public ConfigColorField()
    {
        InitializeComponent();
    }

    public void Bind(ConfigItem item)
    {
        _item = item;
        Type type = item.UnderlyingType;

        Color color = (Color)item.Get();
        previewBrush = new SolidColorBrush(color);

        SetInputText(color);
    }

    private void SetInputText(Color color)
    {
        if (_item.Attr.Type == Core.ConfigType.Hex_RGBA)
        {
            Input.Text = $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        else if (_item.Attr.Type == Core.ConfigType.Hex_RGB)
        {
            Input.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }
        previewBrush.Color = color;
        ColorPreview.Background = previewBrush;
    }

    private void ColorWheel_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_item == null) return;

        // Open a color picker dialog and get the selected color
        ColorPicker colorPicker = new();
        colorPicker.SelectedColor = (Color)_item.Get();
        colorPicker.SelectedColorChanged += ColorPicker_SelectedColorChanged;

        Popup popup = new Popup
        {
            PlacementTarget = ColorWheel,
            Child = colorPicker,
            IsOpen = true,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade
        };

        popup.Focus();
    }

    private void ColorPicker_SelectedColorChanged(object sender, Color e)
    {
        _item.Set(e);
        SetInputText(e);
    }
}
