using Base.Core;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Base.Components;

/// <summary>
/// A reflection-driven configuration dialog for the current page. Every field or property decorated
/// with <see cref="ConfigAttribute"/> is discovered and rendered by instantiating the appropriate
/// per-type editor control (see the <c>ConfigEditors</c> folder) and binding it to the member. The
/// dialog itself only lays out the header / row / help box — all edit, validate and read-back logic
/// lives inside the individual editor controls.
/// </summary>
public partial class ConfigDialog : UserControl
{
    private FrameworkElement _dismissCard;

    public ConfigDialog()
    {
        InitializeComponent();
        // ModernWpf's ContentDialog is modal but does not light-dismiss by default, so wire up
        // click-outside-to-close once the template is realised (on open).
        Dialog.Opened += (s, e) => WireLightDismiss();
        // Escape closes the dialog (there is no built-in command-bar button anymore).
        Dialog.PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Dialog.Hide();
                e.Handled = true;
            }
        };
    }

    /// <summary>
    /// Populates the dialog with every <see cref="ConfigAttribute"/>-decorated member on the given
    /// target and shows it. Safe to call repeatedly; the content is rebuilt each time.
    /// </summary>
    public async void Open(object target, string pageName = null)
    {
        PageNameText.Text = pageName ?? string.Empty;
        PageNameText.Visibility = string.IsNullOrWhiteSpace(pageName)
            ? Visibility.Collapsed
            : Visibility.Visible;

        Build(target);

        try
        {
            await Dialog.ShowAsync();
        }
        catch (System.InvalidOperationException)
        {
            // ShowAsync throws if the dialog is already open; ignore the second request.
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Dialog.Hide();

    #region Light dismiss

    private void WireLightDismiss()
    {
        try
        {
            var tpl = Dialog.Template;
            if (tpl == null) return;

            _dismissCard = tpl.FindName("BackgroundElement", Dialog) as FrameworkElement;
            var overlay = tpl.FindName("Container", Dialog) as FrameworkElement
                       ?? tpl.FindName("LayoutRoot", Dialog) as FrameworkElement;

            if (_dismissCard == null || overlay == null) return;

            overlay.PreviewMouseLeftButtonDown -= OnOverlayMouseDown;
            overlay.PreviewMouseLeftButtonDown += OnOverlayMouseDown;
        }
        catch
        {
            // Template layout differs from what we expect — silently fall back to the Close button.
        }
    }

    private void OnOverlayMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_dismissCard == null) return;
        if (e.OriginalSource is DependencyObject src && IsDescendantOf(src, _dismissCard)) return;
        Dialog.Hide();
    }

    private static bool IsDescendantOf(DependencyObject node, DependencyObject ancestor)
    {
        while (node != null)
        {
            if (ReferenceEquals(node, ancestor)) return true;
            node = VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node);
        }
        return false;
    }

    #endregion

    #region Build

    private void Build(object target)
    {
        ConfigContainer.Children.Clear();
        ConfigContainer.Children.Add(EmptyPlaceholder);

        var items = target == null
            ? new List<ConfigItem>()
            : GetConfigItems(target).ToList();

        foreach (var item in items)
            ConfigContainer.Children.Add(ConfigEditorFactory.BuildRow(item));

        EmptyPlaceholder.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    #endregion

    #region Reflection

    private static IEnumerable<ConfigItem> GetConfigItems(object target)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        var results = new List<ConfigItem>();
        var seenNames = new HashSet<string>();

        for (var t = target.GetType(); t != null && t != typeof(object); t = t.BaseType)
        {
            foreach (var field in t.GetFields(flags))
            {
                var attr = field.GetCustomAttribute<ConfigAttribute>(inherit: true);
                if (attr == null || !seenNames.Add(field.Name)) continue;
                if (!MemberBinding.EvaluateCondition(target, attr.Condition)) continue;

                results.Add(new ConfigItem
                {
                    Attr = attr,
                    ValueType = field.FieldType,
                    Label = MemberBinding.ResolveLabel(attr, field.Name),
                    Get = () => field.GetValue(target),
                    Set = MemberBinding.WrapSet(target, attr.Changed,
                        () => field.GetValue(target), v => field.SetValue(target, v)),
                });
            }

            foreach (var prop in t.GetProperties(flags))
            {
                var attr = prop.GetCustomAttribute<ConfigAttribute>(inherit: true);
                if (attr == null || !seenNames.Add(prop.Name)) continue;
                if (!prop.CanRead || !prop.CanWrite) continue;
                if (prop.GetIndexParameters().Length > 0) continue;
                if (!MemberBinding.EvaluateCondition(target, attr.Condition)) continue;

                results.Add(new ConfigItem
                {
                    Attr = attr,
                    ValueType = prop.PropertyType,
                    Label = MemberBinding.ResolveLabel(attr, prop.Name),
                    Get = () => prop.GetValue(target),
                    Set = MemberBinding.WrapSet(target, attr.Changed,
                        () => prop.GetValue(target), v => prop.SetValue(target, v)),
                });
            }
        }

        return results;
    }

    #endregion
}
