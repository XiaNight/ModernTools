using System.Windows;
using System.Windows.Controls;

namespace Base.UI.Controls
{
    /// <summary>
    /// A single Home-page "Quick Access" tile: an accent icon with a title and subtitle that
    /// raises <see cref="Click"/> when pressed. Purely presentational — the host decides where
    /// <see cref="Target"/> navigates.
    /// </summary>
    public partial class QuickAccessTile : UserControl
    {
        public QuickAccessTile()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty GlyphProperty =
            DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(QuickAccessTile),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(nameof(Title), typeof(string), typeof(QuickAccessTile),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty SubtitleProperty =
            DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(QuickAccessTile),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty TargetProperty =
            DependencyProperty.Register(nameof(Target), typeof(string), typeof(QuickAccessTile),
                new PropertyMetadata(string.Empty));

        /// <summary>Segoe Fluent / MDL2 icon glyph shown in the accent colour.</summary>
        public string Glyph
        {
            get => (string)GetValue(GlyphProperty);
            set => SetValue(GlyphProperty, value);
        }

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public string Subtitle
        {
            get => (string)GetValue(SubtitleProperty);
            set => SetValue(SubtitleProperty, value);
        }

        /// <summary>Logical navigation target name, read by the host in its <see cref="Click"/> handler.</summary>
        public string Target
        {
            get => (string)GetValue(TargetProperty);
            set => SetValue(TargetProperty, value);
        }

        /// <summary>Raised when the tile is activated. Sender is this <see cref="QuickAccessTile"/>.</summary>
        public event RoutedEventHandler Click;

        private void OnButtonClick(object sender, RoutedEventArgs e)
        {
            Click?.Invoke(this, e);
        }
    }
}
