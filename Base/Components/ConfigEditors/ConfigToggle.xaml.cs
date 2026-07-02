using System.Windows.Controls;

namespace Base.Components;

/// <summary>Boolean editor rendered as a left-aligned toggle switch.</summary>
public partial class ConfigToggle : UserControl, IConfigEditor
{
    private ConfigItem _item;
    private bool _updating;

    public ConfigToggle()
    {
        InitializeComponent();
    }

    public void Bind(ConfigItem item)
    {
        _item = item;
        Toggle.IsOn = item.Get() is bool b && b;
        Toggle.OnValueChanged += OnValueChanged;
    }

    private void OnValueChanged(bool value)
    {
        if (_updating) return;
        _item.Set(value);
        // Read back so a custom setter that overrides the value is reflected.
        _updating = true;
        Toggle.IsOn = _item.Get() is bool b && b;
        _updating = false;
    }
}
