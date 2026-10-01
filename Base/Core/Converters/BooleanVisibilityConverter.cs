using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Base;
public sealed class BooleanVisibilityConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        return value is true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        return value is Visibility visibility &&
               visibility == Visibility.Visible;
    }
}

public sealed class InverseBooleanVisibilityConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        return value is bool enabled && !enabled
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        return value is Visibility visibility &&
               visibility != Visibility.Visible;
    }
}