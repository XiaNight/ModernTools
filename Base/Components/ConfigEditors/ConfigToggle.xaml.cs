using System.Windows.Controls;

namespace Base.Components;

/// <summary>Boolean editor rendered as a left-aligned toggle switch.</summary>
public partial class ConfigToggle : UserControl, IConfigEditor
{
    private ConfigItem item;
    private bool updating;

    public ConfigToggle()
    {
        InitializeComponent();
    }

    public void Bind(ConfigItem item)
    {
        this.item = item;
        Toggle.IsOn = item.Get() is bool b && b;
        Toggle.OnValueChanged += OnValueChanged;
    }

    private void OnValueChanged(bool value)
    {
        if (updating) return;
        item.Set(value);
        // Read back so a custom setter that overrides the value is reflected.
        updating = true;
        Toggle.IsOn = item.Get() is bool b && b;
        updating = false;
    }
}
