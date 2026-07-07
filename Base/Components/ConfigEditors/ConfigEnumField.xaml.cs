using System.Windows.Controls;

namespace Base.Components;

/// <summary>Enum editor rendered as a combo box of the enum's values.</summary>
public partial class ConfigEnumField : UserControl, IConfigEditor
{
    private ConfigItem _item;
    private bool _updating;

    public ConfigEnumField()
    {
        InitializeComponent();
    }

    public void Bind(ConfigItem item)
    {
        _item = item;
        Combo.ItemsSource = Enum.GetValues(item.UnderlyingType);
        Combo.SelectedItem = item.Get();
        Combo.SelectionChanged += OnSelectionChanged;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || Combo.SelectedItem == null) return;
        _item.Set(Combo.SelectedItem);
        _updating = true;
        Combo.SelectedItem = _item.Get();
        _updating = false;
    }
}
