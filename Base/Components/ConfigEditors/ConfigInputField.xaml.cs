using Base.Core;
using ModernWpf.Controls.Primitives;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Base.Components;

/// <summary>
/// Text-box based editor for string, integer, floating-point and hexadecimal members. The concrete
/// behaviour (input filtering, parsing, formatting) is selected from the bound member's type and the
/// <see cref="ConfigAttribute.Type"/> hint.
/// </summary>
public partial class ConfigInputField : UserControl, IConfigEditor
{
    private enum InputMode { String, Integer, Float, Hex }

    private ConfigItem _item;
    private InputMode _mode;
    private Brush _defaultBorder;

    public ConfigInputField()
    {
        InitializeComponent();
        _defaultBorder = Input.BorderBrush;
    }

    public void Bind(ConfigItem item)
    {
        _item = item;
        Type type = item.UnderlyingType;

        if (type == typeof(string))
            _mode = InputMode.String;
        else if (ConfigEditorUtil.FloatTypes.Contains(type))
            _mode = InputMode.Float;
        else if (ConfigEditorUtil.IntegerTypes.Contains(type))
            _mode = item.Attr.Type == ConfigType.Hex ? InputMode.Hex : InputMode.Integer;
        else
            _mode = InputMode.String; // best-effort fallback

        switch (_mode)
        {
            case InputMode.Integer:
                ConfigEditorUtil.AttachNumericFilter(Input, ConfigEditorUtil.SignedTypes.Contains(type), allowDecimal: false);
                break;
            case InputMode.Float:
                ConfigEditorUtil.AttachNumericFilter(Input, allowNegative: true, allowDecimal: true);
                break;
            case InputMode.Hex:
                ConfigEditorUtil.AttachHexFilter(Input);
                break;
        }

        if (!string.IsNullOrEmpty(item.Attr.Placeholder))
            ControlHelper.SetPlaceholderText(Input, item.Attr.Placeholder);

        Input.Text = FormatCurrent();
        Input.LostFocus += (s, e) => Commit();
        Input.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
        };
    }

    private string FormatCurrent()
        => _mode == InputMode.Hex
            ? ConfigEditorUtil.FormatHex(_item.Get())
            : ConfigEditorUtil.FormatValue(_item.Get());

    private void Commit()
    {
        Type type = _item.UnderlyingType;
        bool ok;
        object value;
        string err;

        switch (_mode)
        {
            case InputMode.Hex:
                ok = ConfigEditorUtil.TryParseHex(Input.Text, type, _item.Attr, out value, out err);
                break;
            case InputMode.Integer:
            case InputMode.Float:
                ok = ConfigEditorUtil.TryParseNumeric(Input.Text, type, _item.Attr, out value, out err);
                break;
            default:
                ok = ConfigEditorUtil.TryParseText(Input.Text, type, _item.Attr, out value, out err);
                break;
        }

        if (ok)
        {
            _item.Set(value);
            // Read back so any custom setter clamping / normalisation is reflected.
            Input.Text = FormatCurrent();
            ErrorText.Visibility = Visibility.Collapsed;
            Input.BorderBrush = _defaultBorder;
        }
        else
        {
            ErrorText.Text = err;
            ErrorText.Visibility = Visibility.Visible;
            Input.BorderBrush = Brushes.IndianRed;
        }
    }
}
