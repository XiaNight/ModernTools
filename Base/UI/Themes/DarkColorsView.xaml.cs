// DarkColorsView.xaml.cs (WITH SEARCH)

using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;

namespace Base.Helpers
{
    public partial class DarkColorsView : Pages.PageBase
    {
        public override string PageName => "Dark Colors";
        public override int NavOrder => -1;

        private readonly ObservableCollection<ColorKeyItem> _items = new();
        private ICollectionView? _view;

        private const string Url =
            "https://raw.githubusercontent.com/Kinnara/ModernWpf/83ecedc452cc9f06c628c0bdadd50cd4ae76f8e5/ModernWpf/ThemeResources/Dark.xaml";

        public DarkColorsView()
        {
            InitializeComponent();

            GridView.ItemsSource = _items;
            _view = CollectionViewSource.GetDefaultView(_items);
            _view.Filter = FilterPredicate;

            Loaded += async (_, __) =>
            {
                try
                {
                    var xaml = await new HttpClient().GetStringAsync(Url);
                    foreach (var i in ParseKeys(xaml))
                        _items.Add(i);
                }
                catch
                {

                }
            };
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
            => _view?.Refresh();

        private bool FilterPredicate(object obj)
        {
            if (obj is not ColorKeyItem item) return false;
            var q = SearchBox.Text;
            if (string.IsNullOrWhiteSpace(q)) return true;
            return item.Key.Contains(q, StringComparison.OrdinalIgnoreCase);
        }

        private static ColorKeyItem[] ParseKeys(string xaml)
        {
            var doc = XDocument.Load(XmlReader.Create(
                new StringReader(xaml),
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }));

            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

            return doc.Descendants()
                .Select(e => new { Key = (string?)e.Attribute(x + "Key"), Kind = e.Name.LocalName })
                .Where(e => !string.IsNullOrWhiteSpace(e.Key))
                .Select(e => new ColorKeyItem { Key = e.Key!, Kind = e.Kind })
                .ToArray();
        }

        private void Swatch_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Border b) return;
            if (b.DataContext is not ColorKeyItem item) return;


            var value = TryFindResource(item.Key);
            Brush brush = CoerceToBrush(value);
            b.Background = brush;
            item.Hex = brush.ToString();
        }

        private static Brush CoerceToBrush(object? value)
        {
            if (value is Brush br) return br;

            if (value is Color c)
                return new SolidColorBrush(c);

            if (value is string s)
            {
                s = s.Trim();
                if (TryParseHex(s, out var hc))
                    return new SolidColorBrush(hc);

                try
                {
                    var obj = System.Windows.Media.ColorConverter.ConvertFromString(s);
                    if (obj is Color cc)
                        return new SolidColorBrush(cc);
                }
                catch { }
            }

            return Brushes.Transparent;
        }

        private static bool TryParseHex(string text, out Color c)
        {
            c = default;
            if (text.StartsWith("#")) text = text[1..];

            if (text.Length == 6)
                text = "FF" + text;

            if (text.Length != 8) return false;

            bool aOk = byte.TryParse(text[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a);
            bool rOk = byte.TryParse(text.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r);
            bool gOk = byte.TryParse(text.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g);
            bool bOk = byte.TryParse(text.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b);

            if (!aOk || !rOk || !gOk || !bOk) return false;

            c = Color.FromArgb(a, r, g, b);
            return true;
        }
    }

    public sealed class ColorKeyItem
    {
        public string Key { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Hex { get; set; } = "";
    }
}
