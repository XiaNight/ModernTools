using System.Globalization;
using System.Windows.Controls;
using System.Windows.Input;

namespace Base.Components;

/// <summary>
/// Numeric editor rendered as a slider with an editable value box on the right. The range comes from
/// <see cref="Base.Core.ConfigAttribute.Min"/> / <see cref="Base.Core.ConfigAttribute.Max"/>
/// (defaulting to 0..100). Integer members snap to whole ticks. The slider and the value box stay in
/// sync — editing either updates the member.
/// </summary>
public partial class ConfigSlider : UserControl, IConfigEditor
{
    private ConfigItem _item;
    private Type _type;
    private bool _isInteger;
    private double _min;
    private double _max;
    private bool _updating;

    public ConfigSlider()
    {
        InitializeComponent();
    }

    public void Bind(ConfigItem item)
    {
        _item = item;
        _type = item.UnderlyingType;
        _isInteger = ConfigEditorUtil.IntegerTypes.Contains(_type);

        _min = item.Attr.HasMin ? item.Attr.Min : 0;
        _max = item.Attr.HasMax ? item.Attr.Max : 100;
        if (_max < _min) (_min, _max) = (_max, _min);

        Slider.Minimum = _min;
        Slider.Maximum = _max;
        if (_isInteger)
        {
            Slider.IsSnapToTickEnabled = true;
            Slider.TickFrequency = 1;
        }

        ConfigEditorUtil.AttachNumericFilter(
            ValueBox,
            allowNegative: ConfigEditorUtil.SignedTypes.Contains(_type),
            allowDecimal: ConfigEditorUtil.FloatTypes.Contains(_type));

        Sync();

        Slider.ValueChanged += (s, e) =>
        {
            if (_updating) return;
            Commit(Slider.Value);
        };

        ValueBox.LostFocus += (s, e) => CommitFromBox();
        ValueBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommitFromBox();
                e.Handled = true;
            }
        };
    }

    private void CommitFromBox()
    {
        if (ConfigEditorUtil.TryParseNumeric(ValueBox.Text, _type, _item.Attr, out object value, out _))
            Commit(ConfigEditorUtil.ToDouble(value));
        else
            Sync(); // invalid entry — revert to the current value
    }

    private void Commit(double raw)
    {
        double d = ConfigEditorUtil.Clamp(raw, _min, _max);
        if (_isInteger) d = Math.Round(d);

        object value;
        try { value = Convert.ChangeType(d, _type, CultureInfo.InvariantCulture); }
        catch { return; }

        _item.Set(value);
        Sync();
    }

    /// <summary>Pushes the member's current value into both the slider and the value box.</summary>
    private void Sync()
    {
        _updating = true;
        object readback = _item.Get();
        Slider.Value = ConfigEditorUtil.Clamp(ConfigEditorUtil.ToDouble(readback), _min, _max);
        ValueBox.Text = ConfigEditorUtil.FormatValue(readback);
        _updating = false;
    }
}
