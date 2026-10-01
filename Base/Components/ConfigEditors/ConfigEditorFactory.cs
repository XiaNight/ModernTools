using Base.Core;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Base.Components;

/// <summary>
/// The single, shared field-rendering system used by both the per-page <see cref="ConfigDialog"/> and
/// the app-wide <see cref="SettingRegistry"/> / Settings page. Given a <see cref="ConfigItem"/> it
/// produces the fully laid-out row (optional header, label, editor, optional help box, row tooltip)
/// and picks the editor control appropriate to the member's type. Neither caller duplicates this
/// logic — they only differ in how the <see cref="ConfigItem"/>s are discovered.
/// </summary>
public static class ConfigEditorFactory
{
	private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public |
											 BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

	/// <summary>
	/// How many levels of composite member are expanded before the factory stops and renders a notice
	/// instead. Guards against a type that, directly or through a list, contains itself — which would
	/// otherwise recurse until the stack runs out while the dialog is being built.
	/// </summary>
	internal const int MaxNestingDepth = 4;

	/// <summary>
	/// Memoises <see cref="HasConfigMembers"/>: the reflection scan is not free, and the same handful
	/// of types are re-tested every time a dialog opens.
	/// </summary>
	private static readonly Dictionary<Type, bool> configMemberCache = [];

	/// <summary>
	/// Builds the full visual row for a single item: an optional header above, a 120px label column
	/// plus a stretched editor, an optional help box below, and a row-wide tooltip.
	/// </summary>
	/// <param name="item">The discovered member to render.</param>
	/// <param name="depth">
	/// Nesting level of this row — 0 for a member of the target object itself, incremented for each
	/// composite member the factory descends into. Callers other than the editors leave it default.
	/// </param>
	public static FrameworkElement BuildRow(ConfigItem item, int depth = 0)
	{
		// A field may be preceded by a header and followed by a help box, so wrap everything in a
		// vertical stack. The header / help box are purely decorative and optional.
		StackPanel outer = new();

		if (!string.IsNullOrWhiteSpace(item.Attr.Header))
		{
			ConfigHeader header = new();
			header.SetText(item.Attr.Header);
			outer.Children.Add(header);
		}

		Grid grid = new() { Margin = new Thickness(0, 4, 0, 4) };
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

		TextBlock label = new()
		{
			Text = item.Label,
			FontSize = 14,
			VerticalAlignment = VerticalAlignment.Center,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0, 0, 12, 0),
		};
		Grid.SetColumn(label, 0);
		grid.Children.Add(label);

		FrameworkElement editor = CreateEditor(item, depth);
		editor.HorizontalAlignment = HorizontalAlignment.Stretch;
		editor.VerticalAlignment = VerticalAlignment.Center;
		Grid.SetColumn(editor, 1);
		grid.Children.Add(editor);

		// The hint hovers over the whole row; fall back to Description when no Hint is set. WPF
		// tooltips don't bubble, so apply it to the row and both children to cover the full area.
		string rowTip = !string.IsNullOrWhiteSpace(item.Attr.Hint) ? item.Attr.Hint : item.Attr.Description;
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
			ConfigHelpBox help = new();
			help.SetText(item.Attr.HelpBox);
			outer.Children.Add(help);
		}

		return outer;
	}

	/// <summary>Picks the editor control appropriate to the member and binds it.</summary>
	/// <param name="item">The discovered member to render.</param>
	/// <param name="depth">See <see cref="BuildRow"/>.</param>
	public static FrameworkElement CreateEditor(ConfigItem item, int depth = 0)
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
		else if (type == typeof(string)
				 && (item.Attr.Type == ConfigType.File || item.Attr.Type == ConfigType.Folder))
			editor = new ConfigPathField();
		else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
			editor = new ConfigListField { Depth = depth };
		else if (HasConfigMembers(type))
		{
			// A composite member expands into a nested block of its own rows, rather than being
			// stringified into the text box below, which could neither display nor parse it.
			if (depth >= MaxNestingDepth)
				return BuildDepthLimitNotice(type);

			editor = new ConfigObjectField { Depth = depth };
		}
		else
			// string / integer / float / hex all share the text input control.
			editor = new ConfigInputField();

		editor.Bind(item);
		return (FrameworkElement)editor;
	}

	/// <summary>
	/// <c>true</c> when the type is a composite carrying at least one <see cref="ConfigAttribute"/>
	/// member, and so can be expanded by <see cref="ConfigObjectField"/>. Types with no config members
	/// deliberately fail this test and fall through to the text editor, preserving the previous
	/// behaviour for everything that has not opted in.
	/// </summary>
	internal static bool HasConfigMembers(Type type)
	{
		if (type == null || type.IsPrimitive || type.IsEnum || type == typeof(string))
			return false;

		lock (configMemberCache)
		{
			if (configMemberCache.TryGetValue(type, out bool cached))
				return cached;
		}

		bool found = false;
		for (Type t = type; t != null && t != typeof(object) && !found; t = t.BaseType)
		{
			foreach (FieldInfo field in t.GetFields(MemberFlags))
			{
				if (field.GetCustomAttribute<ConfigAttribute>(inherit: true) != null)
				{
					found = true;
					break;
				}
			}

			if (found) break;

			foreach (PropertyInfo prop in t.GetProperties(MemberFlags))
			{
				if (prop.CanRead && prop.CanWrite
					&& prop.GetIndexParameters().Length == 0
					&& prop.GetCustomAttribute<ConfigAttribute>(inherit: true) != null)
				{
					found = true;
					break;
				}
			}
		}

		lock (configMemberCache)
			configMemberCache[type] = found;

		return found;
	}

	/// <summary>
	/// Best-effort default instance for a member type: an empty string for <see cref="string"/>, the
	/// zero value for a value type, and a new instance for any reference type with a public
	/// parameterless constructor. Returns <c>null</c> when the type cannot be constructed — such a
	/// list element stays null and its editor reports that it could not be created.
	/// </summary>
	public static object CreateDefault(Type type)
	{
		if (type == null) return null;
		if (type == typeof(string)) return string.Empty;

		try
		{
			if (type.IsValueType) return Activator.CreateInstance(type);
			if (type.IsAbstract || type.IsInterface) return null;

			return type.GetConstructor(Type.EmptyTypes) != null
				? Activator.CreateInstance(type)
				: null;
		}
		catch
		{
			// Constructing an arbitrary type can throw anything; a null element is recoverable.
			return null;
		}
	}

	/// <summary>Stand-in shown where nesting is cut off by <see cref="MaxNestingDepth"/>.</summary>
	private static FrameworkElement BuildDepthLimitNotice(Type type)
	{
		TextBlock block = new()
		{
			Text = $"{type.Name} is nested too deeply to edit here.",
			FontSize = 12,
			TextWrapping = TextWrapping.Wrap,
		};
		// Resource reference, not a fixed brush, so the text follows a live theme switch.
		block.SetResourceReference(TextBlock.ForegroundProperty, "SystemControlForegroundBaseMediumBrush");

		return block;
	}

	public static IEnumerable<ConfigItem> GetConfigItems(object target)
	{
		List<ConfigItem> results = [];
		HashSet<string> seenNames = [];

		for (Type t = target.GetType(); t != null && t != typeof(object); t = t.BaseType)
		{
			foreach (FieldInfo field in t.GetFields(MemberFlags))
			{
				ConfigAttribute attr = field.GetCustomAttribute<ConfigAttribute>(inherit: true);
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

			foreach (PropertyInfo prop in t.GetProperties(MemberFlags))
			{
				ConfigAttribute attr = prop.GetCustomAttribute<ConfigAttribute>(inherit: true);
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
}