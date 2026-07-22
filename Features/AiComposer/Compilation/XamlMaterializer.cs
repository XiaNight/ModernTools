using System.Windows;
using System.Windows.Markup;

namespace AiComposer.Compilation;

/// <summary>
/// Parses a loose-XAML fragment into a live <see cref="FrameworkElement"/>. A pre-seeded
/// <see cref="ParserContext"/> registers the default presentation namespace plus <c>x:</c> and the
/// ModernWpf <c>ui:</c> namespace, so generated fragments need no xmlns boilerplate. Loose XAML
/// cannot declare a code-behind class or CLR event handlers — all interactivity flows through
/// {Binding} against the DataContext (the generated logic object). Must run on the UI thread.
/// </summary>
internal static class XamlMaterializer
{
	private const string PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
	private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";
	private const string ModernWpfNs = "http://schemas.modernwpf.com/2019";

	public static FrameworkElement Parse(string xaml)
	{
		if (string.IsNullOrWhiteSpace(xaml))
			throw new InvalidOperationException("No XAML source provided.");

		ParserContext context = new();
		context.XmlnsDictionary[""] = PresentationNs;
		context.XmlnsDictionary["x"] = XamlNs;
		context.XmlnsDictionary["ui"] = ModernWpfNs;

		object parsed = XamlReader.Parse(xaml, context);
		return parsed as FrameworkElement
			?? throw new InvalidOperationException(
				$"Root XAML element must be a FrameworkElement, but was '{parsed?.GetType().Name ?? "null"}'.");
	}
}
