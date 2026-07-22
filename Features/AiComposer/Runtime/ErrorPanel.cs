using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AiComposer.Runtime;

/// <summary>
/// Builds the themed inline panel shown when a generated page fails to parse or compile. Colours
/// come from ModernWpf theme brushes via DynamicResource so the panel tracks light/dark like the
/// rest of the app. This keeps failures visible and self-explanatory instead of crashing.
/// </summary>
internal static class ErrorPanel
{
	public static FrameworkElement Build(string title, IReadOnlyList<string> details)
	{
		Border card = new()
		{
			Margin = new Thickness(16),
			Padding = new Thickness(16),
			CornerRadius = new CornerRadius(6),
			BorderThickness = new Thickness(1),
			VerticalAlignment = VerticalAlignment.Top,
		};
		card.SetResourceReference(Border.BackgroundProperty, "SystemControlBackgroundChromeMediumLowBrush");
		card.SetResourceReference(Border.BorderBrushProperty, "SystemControlForegroundBaseLowBrush");

		StackPanel stack = new();

		StackPanel header = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
		TextBlock icon = new()
		{
			Text = "", // Segoe Fluent "Error" glyph
			FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
			FontSize = 18,
			Margin = new Thickness(0, 0, 8, 0),
			VerticalAlignment = VerticalAlignment.Center,
		};
		icon.SetResourceReference(TextBlock.ForegroundProperty, "SystemControlForegroundAccentBrush");
		TextBlock heading = new()
		{
			Text = title,
			FontWeight = FontWeights.SemiBold,
			FontSize = 16,
			VerticalAlignment = VerticalAlignment.Center,
		};
		heading.SetResourceReference(TextBlock.ForegroundProperty, "SystemControlForegroundBaseHighBrush");
		header.Children.Add(icon);
		header.Children.Add(heading);
		stack.Children.Add(header);

		TextBox body = new()
		{
			Text = string.Join(Environment.NewLine + Environment.NewLine, details ?? []),
			IsReadOnly = true,
			BorderThickness = new Thickness(0),
			Background = Brushes.Transparent,
			TextWrapping = TextWrapping.Wrap,
			FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
			FontSize = 12,
		};
		body.SetResourceReference(TextBox.ForegroundProperty, "SystemControlForegroundBaseMediumBrush");
		stack.Children.Add(body);

		card.Child = stack;

		return new ScrollViewer
		{
			Content = card,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
		};
	}
}
