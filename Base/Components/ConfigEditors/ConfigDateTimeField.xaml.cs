using System.Globalization;
using System.Windows.Controls;
using System.Windows.Input;

namespace Base.Components;

/// <summary>DateTime editor: a date picker plus hour / minute / second boxes.</summary>
public partial class ConfigDateTimeField : UserControl, IConfigEditor
{
    private ConfigItem _item;
    private bool _updating;

    public ConfigDateTimeField()
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

        Picker.SelectedDateChanged += (s, e) => Recompose();
        WireCommit(Hours);
        WireCommit(Minutes);
        WireCommit(Seconds);
    }

    private DateTime Current() => _item.Get() is DateTime dt ? dt : DateTime.Now;

    private void WriteControls(DateTime value)
    {
        Picker.SelectedDate = value.Date;
        Hours.Text = value.Hour.ToString(CultureInfo.InvariantCulture);
        Minutes.Text = value.Minute.ToString("00", CultureInfo.InvariantCulture);
        Seconds.Text = value.Second.ToString("00", CultureInfo.InvariantCulture);
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

        DateTime baseDate = (Picker.SelectedDate ?? Current()).Date;
        int h = Math.Clamp(ConfigEditorUtil.ReadInt(Hours, 0), 0, 23);
        int m = Math.Clamp(ConfigEditorUtil.ReadInt(Minutes, 0), 0, 59);
        int s = Math.Clamp(ConfigEditorUtil.ReadInt(Seconds, 0), 0, 59);

        _item.Set(baseDate.AddHours(h).AddMinutes(m).AddSeconds(s));

        _updating = true;
        WriteControls(Current());
        _updating = false;
    }
}
