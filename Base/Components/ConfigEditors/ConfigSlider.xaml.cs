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
    private ConfigItem item;
    private Type type;
    private bool isInteger;
    private double min;
    private double max;
    private bool isUpdating;

    public ConfigSlider()
    {
        InitializeComponent();
    }

    public void Bind(ConfigItem item)
    {
        this.item = item;
        type = item.UnderlyingType;
        isInteger = ConfigEditorUtil.IntegerTypes.Contains(type);

        min = item.Attr.HasMin ? item.Attr.Min : 0;
        max = item.Attr.HasMax ? item.Attr.Max : 100;
        if (max < min) (min, max) = (max, min);

        Slider.Minimum = min;
        Slider.Maximum = max;
        if (isInteger)
        {
            Slider.IsSnapToTickEnabled = true;
            Slider.TickFrequency = 1;
        }

        ConfigEditorUtil.AttachNumericFilter(
            ValueBox,
            allowNegative: ConfigEditorUtil.SignedTypes.Contains(type),
            allowDecimal: ConfigEditorUtil.FloatTypes.Contains(type));

        Sync();

        Slider.ValueChanged += (s, e) =>
        {
            if (isUpdating) return;
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
        if (ConfigEditorUtil.TryParseNumeric(ValueBox.Text, type, item.Attr, out object value, out _))
            Commit(ConfigEditorUtil.ToDouble(value));
        else
            Sync(); // invalid entry — revert to the current value
    }

    private void Commit(double raw)
    {
        double d = ConfigEditorUtil.Clamp(raw, min, max);
        if (isInteger) d = Math.Round(d);

        object value;
        try { value = Convert.ChangeType(d, type, CultureInfo.InvariantCulture); }
        catch { return; }

        item.Set(value);
        Sync();
    }

    /// <summary>Pushes the member's current value into both the slider and the value box.</summary>
    private void Sync()
    {
        isUpdating = true;
        object readback = item.Get();
        Slider.Value = ConfigEditorUtil.Clamp(ConfigEditorUtil.ToDouble(readback), min, max);
        ValueBox.Text = ConfigEditorUtil.FormatValue(readback);
        isUpdating = false;
    }
}
