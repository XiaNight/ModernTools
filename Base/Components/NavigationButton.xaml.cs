using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Base.Components
{
    /// <summary>
    /// Interaction logic for NavigationButton.xaml
    /// </summary>
    public partial class NavigationButton : UserControl
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(NavigationButton),
                new PropertyMetadata("Home"));

        public static readonly DependencyProperty GlyphProperty =
            DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(NavigationButton),
                new PropertyMetadata("\uE879"));

        public static readonly DependencyProperty SecondaryGlyphProperty =
            DependencyProperty.Register(nameof(SecondaryGlyph), typeof(string), typeof(NavigationButton),
                new PropertyMetadata(""));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public string Glyph
        {
            get => (string)GetValue(GlyphProperty);
            set => SetValue(GlyphProperty, value);
        }

        public string SecondaryGlyph
        {
            get => (string)GetValue(SecondaryGlyphProperty);
            set
            { 
                SetValue(SecondaryGlyphProperty, value);
                if (string.IsNullOrEmpty(value))
                {
                    SecondaryIcon.Visibility = Visibility.Collapsed;
                    return;
                }
            }
        }

        public int OrderIndex { get; set; } = 0;

        public event Action OnClick;

        public NavigationButton()
        {
            InitializeComponent();

            NavButton.Click += (s, e) => OnClick?.Invoke();
        }

        public void Expand()
        {
            Label.Visibility = System.Windows.Visibility.Visible;
        }

        public void Collapse()
        {
            Label.Visibility = System.Windows.Visibility.Collapsed;
        }

        public void SetHighlightedState(bool state)
        {
            Highlight.Visibility = state ? Visibility.Visible : Visibility.Collapsed;
            string foreground = state ? "SystemControlForegroundAccentBrush" : "TextControlForeground";
            NavIcon.SetResourceReference(ForegroundProperty, foreground);
            Label.SetResourceReference(ForegroundProperty, foreground);
            SecondaryLabel.SetResourceReference(ForegroundProperty, foreground);
            Label.FontWeight = state ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }
}
