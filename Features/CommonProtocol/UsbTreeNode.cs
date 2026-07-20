namespace Base.UI.Pages;

using System.Collections.ObjectModel;

/// <summary>
/// A node in the Bus Hound device tree. A device is a root node whose children
/// are its interfaces; each node carries an icon glyph, a title, and a detail line.
/// </summary>
public sealed class UsbTreeNode
{
	public string Glyph { get; set; }

	public string Title { get; set; }

	public string Detail { get; set; }

	public ObservableCollection<UsbTreeNode> Children { get; } = new();
}