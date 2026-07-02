using System.Windows;
using System.Windows.Controls;

namespace Base.Components;

/// <summary>Decorative title rendered above a field (like Unity's <c>[Header]</c>).</summary>
public partial class ConfigHeader : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(ConfigHeader),
            new PropertyMetadata(string.Empty));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public ConfigHeader()
    {
        InitializeComponent();
    }

    public void SetText(string text) => Text = text;
}
