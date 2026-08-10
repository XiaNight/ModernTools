using Base.Core;
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
	/// <summary>
	/// Builds the full visual row for a single item: an optional header above, a 120px label column
	/// plus a stretched editor, an optional help box below, and a row-wide tooltip.
	/// </summary>
	public static FrameworkElement BuildRow(ConfigItem item)
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

		FrameworkElement editor = CreateEditor(item);
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
	public static FrameworkElement CreateEditor(ConfigItem item)
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
		else
			// string / integer / float / hex all share the text input control.
			editor = new ConfigInputField();

		editor.Bind(item);
		return (FrameworkElement)editor;
	}
}
