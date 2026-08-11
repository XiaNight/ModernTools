using Base.Core;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;

namespace Base.Helpers
{
    [PageInfo("Dark Colors", NavOrder = -1)]
    public partial class DarkColorsView : Pages.PageBase
    {

        private readonly ObservableCollection<ColorKeyItem> items = new();
        private readonly ICollectionView view;

        private const string Url =
            "https://raw.githubusercontent.com/Kinnara/ModernWpf/83ecedc452cc9f06c628c0bdadd50cd4ae76f8e5/ModernWpf/ThemeResources/Dark.xaml";

        public DarkColorsView()
        {
            InitializeComponent();

            GridView.ItemsSource = items;
            view = CollectionViewSource.GetDefaultView(items);
            view.Filter = FilterPredicate;

            Loaded += async (_, __) =>
            {
                try
                {
                    string xaml = await new HttpClient().GetStringAsync(Url);
                    foreach (ColorKeyItem i in ParseKeys(xaml))
                    {
                        Resolve(i);
                        items.Add(i);
                    }
                }
                catch
                {

                }
            };
        }

        public override void ThemeChanged()
        {
            base.ThemeChanged();
            RefreshColors();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            RefreshColors();
        }

        private void RefreshColors()
        {
            foreach (ColorKeyItem i in items)
                Resolve(i);
        }

        private void Resolve(ColorKeyItem item)
        {
            Brush brush = CoerceToBrush(TryFindResource(item.Key));
            item.Swatch = brush;
            item.Hex = brush.ToString();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
            => view?.Refresh();

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
            if (text.StartsWith('#')) text = text[1..];

            if (text.Length == 6)
                text = "FF" + text;

            if (text.Length != 8) return false;

            bool aOk = byte.TryParse(text[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte a);
            bool rOk = byte.TryParse(text.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte r);
            bool gOk = byte.TryParse(text.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte g);
            bool bOk = byte.TryParse(text.AsSpan(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b);

            if (!aOk || !rOk || !gOk || !bOk) return false;

            c = Color.FromArgb(a, r, g, b);
            return true;
        }
    }

    public sealed class ColorKeyItem : INotifyPropertyChanged
    {
        public string Key { get; init; } = "";
        public string Kind { get; init; } = "";

        private string hex = "";
        public string Hex
        {
            get => hex;
            set
            {
                if (hex != value)
                {
                    hex = value;
                    Raise(nameof(Hex));
                }
            }
        }

        private Brush swatch = Brushes.Transparent;
        public Brush Swatch
        {
            get => swatch;
            set
            {
                if (!Equals(swatch, value))
                {
                    swatch = value;
                    Raise(nameof(Swatch));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise(string name)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
