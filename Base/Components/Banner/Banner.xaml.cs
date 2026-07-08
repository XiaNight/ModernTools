using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Base.Components;

/// <summary>
/// Severity of a <see cref="Banner"/>. Drives the icon and colour scheme.
/// </summary>
public enum BannerSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// A single dismissible/non-dismissible message bar shown at the top of the window.
/// Create and display banners through <see cref="BannerManager"/> rather than instantiating directly.
/// </summary>
public partial class Banner : UserControl
{
    /// <summary>Raised when the user dismisses the banner via the close button.</summary>
    public event Action<Banner> Dismissed;

    #region Text

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(Banner),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Banner banner)
            banner.MessageText.Text = (string)e.NewValue;
    }

    #endregion

    #region Severity

    public static readonly DependencyProperty SeverityProperty =
        DependencyProperty.Register(
            nameof(Severity),
            typeof(BannerSeverity),
            typeof(Banner),
            new PropertyMetadata(BannerSeverity.Warning, OnSeverityChanged));

    public BannerSeverity Severity
    {
        get => (BannerSeverity)GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    private static void OnSeverityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Banner banner)
            banner.ApplySeverity((BannerSeverity)e.NewValue);
    }

    #endregion

    #region Dismissible

    public static readonly DependencyProperty DismissibleProperty =
        DependencyProperty.Register(
            nameof(Dismissible),
            typeof(bool),
            typeof(Banner),
            new PropertyMetadata(false, OnDismissibleChanged));

    /// <summary>When true, a close (cross) button is shown at the right end.</summary>
    public bool Dismissible
    {
        get => (bool)GetValue(DismissibleProperty);
        set => SetValue(DismissibleProperty, value);
    }

    private static void OnDismissibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Banner banner)
            banner.CloseButton.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;
    }

    #endregion

    public Banner()
    {
        InitializeComponent();
        ApplySeverity(Severity);
    }

    /// <summary>Updates the background and glyph to match the given severity.</summary>
    private void ApplySeverity(BannerSeverity severity)
    {
        (string backgroundKey, string glyph) = severity switch
        {
            BannerSeverity.Info    => ("InfoBackgroundBrush",  ""), // Info
            BannerSeverity.Error   => ("ErrorBackgroundBrush", ""), // ErrorBadge
            _                      => ("WarningBackgroundBrush", ""), // Warning
        };

        if (TryFindResource(backgroundKey) is Brush background)
            RootBorder.Background = background;

        SeverityIcon.Glyph = glyph;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Dismissed?.Invoke(this);
    }
}
