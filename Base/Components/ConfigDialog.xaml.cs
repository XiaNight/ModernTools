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
            ConfigContainer.Children.Add(BuildRow(item));

        EmptyPlaceholder.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private FrameworkElement BuildRow(ConfigItem item)
    {
        // A field may be preceded by a header and followed by a help box, so wrap everything in a
        // vertical stack. The header / help box are purely decorative and optional.
        var outer = new StackPanel();

        if (!string.IsNullOrWhiteSpace(item.Attr.Header))
        {
            var header = new ConfigHeader();
            header.SetText(item.Attr.Header);
            outer.Children.Add(header);
        }

        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = new TextBlock
        {
            Text = item.Label,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 12, 0),
        };
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var editor = CreateEditor(item);
        editor.HorizontalAlignment = HorizontalAlignment.Stretch;
        editor.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);

        // The hint hovers over the whole row; fall back to Description when no Hint is set. WPF
        // tooltips don't bubble, so apply it to the row and both children to cover the full area.
        var rowTip = !string.IsNullOrWhiteSpace(item.Attr.Hint) ? item.Attr.Hint : item.Attr.Description;
        if (!string.IsNullOrWhiteSpace(rowTip))
        {
            grid.Background = Brushes.Transparent; // hit-test the gaps between children
            grid.ToolTip = rowTip;
            label.ToolTip = rowTip;
            editor.ToolTip = rowTip;
        }

        outer.Children.Add(grid);

        if (!string.IsNullOrWhiteSpace(item.Attr.HelpBox))
        {
            var help = new ConfigHelpBox();
            help.SetText(item.Attr.HelpBox);
            outer.Children.Add(help);
        }

        return outer;
    }

    /// <summary>Picks the editor control appropriate to the member and binds it.</summary>
    private static FrameworkElement CreateEditor(ConfigItem item)
    {
        Type type = item.UnderlyingType;

        IConfigEditor editor;
        if (type == typeof(bool))
            editor = new ConfigToggle();
        else if (type.IsEnum)
            editor = new ConfigEnumField();
        else if (type == typeof(DateTime))
            editor = new ConfigDateTimeField();
        else if (type == typeof(TimeSpan))
            editor = new ConfigTimeSpanField();
        else if ((ConfigEditorUtil.IntegerTypes.Contains(type) || ConfigEditorUtil.FloatTypes.Contains(type))
                 && item.Attr.Type == ConfigType.Slider)
            editor = new ConfigSlider();
        else
            // string / integer / float / hex all share the text input control.
            editor = new ConfigInputField();

        editor.Bind(item);
        return (FrameworkElement)editor;
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
                if (!EvaluateCondition(target, attr.Condition)) continue;

                results.Add(new ConfigItem
                {
                    Attr = attr,
                    ValueType = field.FieldType,
                    Label = ResolveLabel(attr, field.Name),
                    Get = () => field.GetValue(target),
                    Set = WrapSet(target, attr.Changed,
                        () => field.GetValue(target), v => field.SetValue(target, v)),
                });
            }

            foreach (var prop in t.GetProperties(flags))
            {
                var attr = prop.GetCustomAttribute<ConfigAttribute>(inherit: true);
                if (attr == null || !seenNames.Add(prop.Name)) continue;
                if (!prop.CanRead || !prop.CanWrite) continue;
                if (prop.GetIndexParameters().Length > 0) continue;
                if (!EvaluateCondition(target, attr.Condition)) continue;

                results.Add(new ConfigItem
                {
                    Attr = attr,
                    ValueType = prop.PropertyType,
                    Label = ResolveLabel(attr, prop.Name),
                    Get = () => prop.GetValue(target),
                    Set = WrapSet(target, attr.Changed,
                        () => prop.GetValue(target), v => prop.SetValue(target, v)),
                });
            }
        }

        return results;
    }

    /// <summary>
    /// Evaluates a <see cref="ConfigAttribute.Condition"/> against the target. The condition names a
    /// bool field, readable bool property, or parameterless bool method (searched up the hierarchy,
    /// including non-public members). Returns <c>true</c> (show) when empty or unresolvable.
    /// </summary>
    private static bool EvaluateCondition(object target, string condition)
    {
        if (string.IsNullOrWhiteSpace(condition)) return true;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        for (var t = target.GetType(); t != null && t != typeof(object); t = t.BaseType)
        {
            var prop = t.GetProperty(condition, flags);
            if (prop != null && prop.CanRead && prop.PropertyType == typeof(bool))
                return (bool)prop.GetValue(target);

            var field = t.GetField(condition, flags);
            if (field != null && field.FieldType == typeof(bool))
                return (bool)field.GetValue(target);

            var method = t.GetMethod(condition, flags, binder: null, types: Type.EmptyTypes, modifiers: null);
            if (method != null && method.ReturnType == typeof(bool))
                return (bool)method.Invoke(target, null);
        }

        return true; // unresolved — default to showing the entry
    }

    /// <summary>
    /// Wraps a member's setter so that a <see cref="ConfigAttribute.Changed"/> callback is invoked
    /// after the value is written — but only when the value actually changes (compared after any
    /// custom setter normalisation). When no callback is configured or it cannot be resolved, the
    /// original setter is returned unchanged.
    /// </summary>
    private static Action<object> WrapSet(object target, string changed, Func<object> get, Action<object> set)
    {
        if (string.IsNullOrWhiteSpace(changed)) return set;

        var callback = ResolveChangedCallback(target, changed);
        if (callback == null) return set;

        return v =>
        {
            var before = get();
            set(v);
            if (!Equals(before, get()))
                callback();
        };
    }

    /// <summary>
    /// Resolves a <see cref="ConfigAttribute.Changed"/> callback: a parameterless method on the
    /// target (searched up the hierarchy, including non-public members). Returns <c>null</c> when
    /// the name cannot be resolved.
    /// </summary>
    private static Action ResolveChangedCallback(object target, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        for (var t = target.GetType(); t != null && t != typeof(object); t = t.BaseType)
        {
            var method = t.GetMethod(name, flags, binder: null, types: Type.EmptyTypes, modifiers: null);
            if (method != null)
                return () => method.Invoke(target, null);
        }

        return null;
    }

    private static string ResolveLabel(ConfigAttribute attr, string memberName)
    {
        if (!string.IsNullOrWhiteSpace(attr.Name)) return attr.Name;
        if (!string.IsNullOrWhiteSpace(attr.Key)) return attr.Key;
        return memberName;
    }

    #endregion
}
