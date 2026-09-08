using Base.Core;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Base.Components;

/// <summary>
/// Editor for a composite member — any class or struct that itself carries
/// <see cref="ConfigAttribute"/>-decorated members. The object's own config members are discovered
/// with <see cref="ConfigEditorFactory.GetConfigItems"/> and rendered as a nested block of rows, so a
/// page can expose a structured object (or, through <see cref="ConfigListField"/>, a
/// <see cref="List{T}"/> of them) instead of flattening every leaf onto the page itself.
/// </summary>
public partial class ConfigObjectField : UserControl, IConfigEditor
{
	private ConfigItem item;

	public ConfigObjectField()
	{
		InitializeComponent();
	}

	/// <summary>
	/// Nesting depth of this editor, assigned by <see cref="ConfigEditorFactory"/> before
	/// <see cref="Bind"/> is called. Top-level members sit at depth 0.
	/// </summary>
	internal int Depth { get; set; }

	public void Bind(ConfigItem item)
	{
		this.item = item;
		Rebuild();
	}

	/// <summary>
	/// Builds the nested rows from the member's current value. A null instance is repaired in place —
	/// constructed and written back through the member — so that a freshly added list element, which
	/// starts life as null, becomes editable straight away.
	/// </summary>
	private void Rebuild()
	{
		FieldsHost.Children.Clear();

		object instance = item.Get();
		if (instance == null)
		{
			object created = ConfigEditorFactory.CreateDefault(item.UnderlyingType);
			if (created != null)
				item.Set(created);

			// Read back rather than trusting `created`: a custom setter may normalise or reject it.
			instance = item.Get();
		}

		if (instance == null)
		{
			ShowPlaceholder($"Cannot create a {item.UnderlyingType.Name}.");
			return;
		}

		List<ConfigItem> children = ConfigEditorFactory.GetConfigItems(instance).ToList();
		if (children.Count == 0)
		{
			ShowPlaceholder($"{item.UnderlyingType.Name} has no [Config] members.");
			return;
		}

		foreach (ConfigItem child in children)
			FieldsHost.Children.Add(ConfigEditorFactory.BuildRow(child, Depth + 1));
	}

	/// <summary>Shows a muted one-line explanation in place of the rows we could not build.</summary>
	private void ShowPlaceholder(string text)
	{
		TextBlock block = new()
		{
			Text = text,
			FontSize = 12,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0, 4, 0, 4),
		};
		// Resource reference, not a fixed brush, so the text follows a live theme switch.
		block.SetResourceReference(ForegroundProperty, "SystemControlForegroundBaseMediumBrush");

		FieldsHost.Children.Add(block);
	}
}