using System.Windows.Controls;
using System.Windows.Threading;

namespace Base.Components;

/// <summary>
/// Project-wide manager for the message banners shown at the top of the main window.
/// Anyone can raise a banner from anywhere via <see cref="Instance"/>, e.g.
/// <code>BannerManager.Instance.ShowWarning("Something needs attention");</code>
/// The manager owns a container panel and stacks multiple banners on top of each other.
/// All members are safe to call from any thread; work is marshalled onto the UI thread.
/// </summary>
public sealed class BannerManager
{
    private static BannerManager instance;

    public static BannerManager Instance =>
        instance ?? throw new InvalidOperationException(
            "BannerManager is not initialized. Call BannerManager.Init(container) from the main window first.");

    public static bool IsInitialised => instance != null;

    private readonly Panel container;
    private Dispatcher Dispatcher => container.Dispatcher;

    private BannerManager(Panel container)
    {
        this.container = container ?? throw new ArgumentNullException(nameof(container));
    }

    /// <summary>
    /// Initialises the manager with the panel that hosts the banners.
    /// Called once by the main window during startup.
    /// </summary>
    public static BannerManager Init(Panel container)
    {
        instance = new BannerManager(container);
        return instance;
    }

    /// <summary>
    /// Adds a banner to the container and returns its handle so callers can remove it later.
    /// </summary>
    /// <param name="text">Message to display.</param>
    /// <param name="severity">Colour scheme / icon to use.</param>
    /// <param name="dismissible">When true, shows a close cross the user can click to dismiss it.</param>
    public Banner Show(string text, BannerSeverity severity = BannerSeverity.Warning, bool dismissible = true)
    {
        if (Dispatcher.CheckAccess())
            return CreateAndAdd(text, severity, dismissible);

        return Dispatcher.Invoke(() => CreateAndAdd(text, severity, dismissible));
    }

    public Banner ShowInfo(string text, bool dismissible = true)
        => Show(text, BannerSeverity.Info, dismissible);

    public Banner ShowWarning(string text, bool dismissible = true)
        => Show(text, BannerSeverity.Warning, dismissible);

    public Banner ShowError(string text, bool dismissible = true)
        => Show(text, BannerSeverity.Error, dismissible);

    private Banner CreateAndAdd(string text, BannerSeverity severity, bool dismissible)
    {
        var banner = new Banner
        {
            Text = text,
            Severity = severity,
            Dismissible = dismissible
        };
        banner.Dismissed += OnBannerDismissed;
        container.Children.Add(banner);
        return banner;
    }

    private void OnBannerDismissed(Banner banner) => Remove(banner);

    /// <summary>Removes a previously shown banner. Safe to call more than once.</summary>
    public void Remove(Banner banner)
    {
        if (banner == null) return;

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => Remove(banner));
            return;
        }

        banner.Dismissed -= OnBannerDismissed;
        container.Children.Remove(banner);
    }

    /// <summary>Removes every banner currently shown.</summary>
    public void Clear()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(Clear);
            return;
        }

        foreach (var banner in container.Children.OfType<Banner>().ToList())
            Remove(banner);
    }
}
