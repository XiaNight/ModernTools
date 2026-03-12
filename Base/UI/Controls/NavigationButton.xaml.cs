using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Base.Components
{
    /// <summary>
    /// Interaction logic for NavigationButton.xaml
    /// </summary>
    public partial class NavigationButton : UserControl, INavigationItem
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(NavigationButton),
                new PropertyMetadata("Button"));

        public static readonly DependencyProperty ShortTextProperty =
            DependencyProperty.Register(nameof(ShortText), typeof(string), typeof(NavigationButton),
                new PropertyMetadata(""));

        public static readonly DependencyProperty GlyphProperty =
            DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(NavigationButton),
                new PropertyMetadata("\uE879"));

        public static readonly DependencyProperty SecondaryGlyphProperty =
            DependencyProperty.Register(nameof(SecondaryGlyph), typeof(string), typeof(NavigationButton),
                new PropertyMetadata(""));

        public static readonly DependencyProperty ItemHeightProperty =
            DependencyProperty.Register(nameof(ItemHeight), typeof(int), typeof(NavigationButton),
                new PropertyMetadata(48));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }
        public string ShortText
        {
            get => (string)GetValue(ShortTextProperty);
            set => SetValue(ShortTextProperty, value);
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

        public int ItemHeight
        {
            get => (int)GetValue(ItemHeightProperty);
            set => SetValue(ItemHeightProperty, value);
        }

        public int OrderIndex { get; set; } = 0;
        public bool IsChild { get; set; } = false;

        public int Size => 1;

        public event Action OnClick;

        public NavigationButton()
        {
            InitializeComponent();

            NavButton.Click += (s, e) => OnClick?.Invoke();
        }

        public void ExitCompactMode()
        {
        }

        public void EnterCompactMode()
        {
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
