using System.Globalization;
using System.Windows.Controls;
using System.Windows.Input;

namespace Base.Components;

/// <summary>TimeSpan / duration editor: hour / minute / second boxes.</summary>
public partial class ConfigTimeSpanField : UserControl, IConfigEditor
{
    private ConfigItem _item;
    private bool _updating;

    public ConfigTimeSpanField()
    {
        InitializeComponent();
    }

    public void Bind(ConfigItem item)
    {
        _item = item;

        ConfigEditorUtil.AttachNumericFilter(Hours, allowNegative: false, allowDecimal: false);
        ConfigEditorUtil.AttachNumericFilter(Minutes, allowNegative: false, allowDecimal: false);
        ConfigEditorUtil.AttachNumericFilter(Seconds, allowNegative: false, allowDecimal: false);

        WriteControls(Current());

        WireCommit(Hours);
        WireCommit(Minutes);
        WireCommit(Seconds);
    }

    private TimeSpan Current() => _item.Get() is TimeSpan ts ? ts : TimeSpan.Zero;

    private void WriteControls(TimeSpan value)
    {
        Hours.Text = ((int)value.TotalHours).ToString(CultureInfo.InvariantCulture);
        Minutes.Text = value.Minutes.ToString("00", CultureInfo.InvariantCulture);
        Seconds.Text = value.Seconds.ToString("00", CultureInfo.InvariantCulture);
    }

    private void WireCommit(TextBox box)
    {
        box.LostFocus += (s, e) => Recompose();
        box.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Recompose();
                e.Handled = true;
            }
        };
    }

    private void Recompose()
    {
        if (_updating) return;

        int h = Math.Max(0, ConfigEditorUtil.ReadInt(Hours, 0));
        int m = Math.Max(0, ConfigEditorUtil.ReadInt(Minutes, 0));
        int s = Math.Max(0, ConfigEditorUtil.ReadInt(Seconds, 0));

        _item.Set(new TimeSpan(h, m, s));

        _updating = true;
        WriteControls(Current());
        _updating = false;
    }
}
