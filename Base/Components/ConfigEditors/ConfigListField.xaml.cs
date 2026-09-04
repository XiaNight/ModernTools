using Base.Core;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Base.Components;

/// <summary>
/// Editor for a <see cref="List{T}"/> member: one reorderable row per element, each hosting whichever
/// editor <see cref="ConfigEditorFactory"/> picks for the element type — so a list of strings gets
/// text boxes or path pickers, and a list of composites gets a nested block of rows per element.
/// <para>
/// Rows address their element by <em>identity</em>, not by an index captured when the row was built:
/// an element's get / set resolves the row's current position at call time. Reordering or removing a
/// row therefore cannot leave the surviving editors pointing at the wrong element, and every mutation
/// is applied to the backing list and the list view together.
/// </para>
/// </summary>
public partial class ConfigListField : UserControl, IConfigEditor
{
	private ConfigItem item;
	private ConfigAttribute elementAttr;
	private IList list;
	private Type elementType;
	private ReorderableListViewEntry lastFocused;

	public ConfigListField()
	{
		InitializeComponent();
		ContentList.OrderChanged += ContentList_OrderChanged;
	}

	/// <summary>
	/// Nesting depth of this editor, assigned by <see cref="ConfigEditorFactory"/> before
	/// <see cref="Bind"/> is called. Elements are built one level deeper.
	/// </summary>
	internal int Depth { get; set; }

	public void Bind(ConfigItem item)
	{
		this.item = item;
		elementType = item.UnderlyingType.GetGenericArguments()[0];
		elementAttr = BuildElementAttr(item.Attr);

		// A null list is repaired in place, so the buttons stay usable on a member that was never
		// initialised or that came back null from persistence.
		list = item.Get() as IList;
		if (list == null)
		{
			object created = ConfigEditorFactory.CreateDefault(item.UnderlyingType);
			if (created != null)
				item.Set(created);

			// Read back rather than trusting `created`: a custom setter may normalise or reject it.
			list = item.Get() as IList;
		}

		if (list == null)
		{
			AddButton.IsEnabled = false;
			RemoveButton.IsEnabled = false;
			EmptyText.Text = $"Cannot create a {item.UnderlyingType.Name}.";
			EmptyText.Visibility = Visibility.Visible;
			return;
		}

		int count = list.Count;
		for (int index = 0; index < count; index++)
			AddRow();

		UpdateEmptyState();
	}

	/// <summary>
	/// Derives the per-element attribute from the list's own. Only knobs that mean something for a
	/// single element are carried over; the list-level decoration (header, help box, hint, description,
	/// condition, change callback) describes the list's own row and must not be repeated per element.
	/// <para>
	/// The editor hint is forwarded only for string elements, where <see cref="ConfigType.File"/> /
	/// <see cref="ConfigType.Folder"/> select a path picker. On a composite element it would misroute
	/// the editor choice instead.
	/// </para>
	/// </summary>
	private ConfigAttribute BuildElementAttr(FieldAttribute source)
	{
		ConfigAttribute attr = new()
		{
			Placeholder = source.Placeholder,
			Regex = source.Regex,
			Min = source.Min,
			Max = source.Max,
			FileExtentions = source.FileExtentions,
		};

		if (elementType == typeof(string))
			attr.Type = source.Type;

		return attr;
	}

	/// <summary>
	/// Appends a row bound to whichever element occupies that row's position. The entry joins the list
	/// view <em>before</em> its editor is created, so the closures below can already resolve the row's
	/// index while the editor initialises its displayed value.
	/// </summary>
	private void AddRow()
	{
		ReorderableListViewEntry entry = new();
		entry.GotKeyboardFocus += OnEntryGotKeyboardFocus;
		ContentList.AddEntry(entry);

		ConfigItem elementItem = new()
		{
			ValueType = elementType,
			Attr = elementAttr,
			Label = string.Empty,
			Get = () => ReadElement(entry),
			Set = value => WriteElement(entry, value),
		};

		entry.EntryContent = ConfigEditorFactory.CreateEditor(elementItem, Depth + 1);
	}

	/// <summary>Current position of a row, or -1 once it has been removed.</summary>
	private int IndexOf(ReorderableListViewEntry entry)
	{
		IReadOnlyList<ReorderableListViewEntry> entries = ContentList.Entries;

		for (int index = 0; index < entries.Count; index++)
		{
			if (ReferenceEquals(entries[index], entry))
				return index;
		}

		return -1;
	}

	private object ReadElement(ReorderableListViewEntry entry)
	{
		int index = IndexOf(entry);
		return index >= 0 && index < list.Count ? list[index] : null;
	}

	private void WriteElement(ReorderableListViewEntry entry, object value)
	{
		int index = IndexOf(entry);
		if (index < 0 || index >= list.Count) return;

		list[index] = value;
	}

	private void OnEntryGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
	{
		if (sender is ReorderableListViewEntry entry)
			lastFocused = entry;
	}

	/// <summary>
	/// Mirrors a completed drag onto the backing list. The rows were already moved while dragging, so
	/// at this point the list view holds the new order and the list still holds the old one.
	/// </summary>
	private void ContentList_OrderChanged(object sender, ReorderedEventArgs e)
	{
		if (list == null) return;
		if (e.OldIndex < 0 || e.OldIndex >= list.Count) return;
		if (e.NewIndex < 0 || e.NewIndex >= list.Count) return;

		object moved = list[e.OldIndex];
		list.RemoveAt(e.OldIndex);
		list.Insert(e.NewIndex, moved);
	}

	private void AddButton_Click(object sender, RoutedEventArgs e)
	{
		if (list == null) return;

		list.Add(ConfigEditorFactory.CreateDefault(elementType));
		AddRow();
		UpdateEmptyState();
	}

	private void RemoveButton_Click(object sender, RoutedEventArgs e)
	{
		if (list == null) return;

		ReorderableListViewEntry target = ResolveRemoveTarget();
		if (target == null) return;

		// Resolve the index while the row is still in the list view, then drop both sides together.
		int index = IndexOf(target);

		target.GotKeyboardFocus -= OnEntryGotKeyboardFocus;
		ContentList.RemoveEntry(target);
		if (ReferenceEquals(lastFocused, target))
			lastFocused = null;

		if (index >= 0 && index < list.Count)
			list.RemoveAt(index);

		UpdateEmptyState();
	}

	/// <summary>
	/// The row Remove acts on: whatever currently holds keyboard focus, else the row the user last
	/// edited, else the final row. Clicking the button moves focus onto the button itself, so the
	/// focus check alone would almost always come up empty.
	/// </summary>
	private ReorderableListViewEntry ResolveRemoveTarget()
	{
		IReadOnlyList<ReorderableListViewEntry> entries = ContentList.Entries;

		foreach (ReorderableListViewEntry entry in entries)
		{
			if (entry.IsKeyboardFocused || entry.IsKeyboardFocusWithin)
				return entry;
		}

		if (lastFocused != null && entries.Contains(lastFocused))
			return lastFocused;

		return entries.LastOrDefault();
	}

	private void UpdateEmptyState()
	{
		EmptyText.Visibility = ContentList.Entries.Count == 0
			? Visibility.Visible
			: Visibility.Collapsed;
	}
}