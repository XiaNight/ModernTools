using System.Windows;
using System.Windows.Controls;

namespace Base.Components;

/// <summary>Plain-text help box rendered beneath a field (like Unity's <c>EditorGUI.HelpBox</c>).</summary>
public partial class ConfigHelpBox : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(ConfigHelpBox),
            new PropertyMetadata(string.Empty));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public ConfigHelpBox()
    {
        InitializeComponent();
    }

    public void SetText(string text) => Text = text;
}
