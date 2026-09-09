using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Base.Components;

/// <summary>
/// A vertical list of <see cref="ReorderableListViewEntry"/> rows that the user can reorder by
/// dragging each row's handle up or down. Rows are added programmatically via <see cref="Add(object)"/>
/// or <see cref="AddEntry"/>; the entry's content is left to the caller. Raises <see cref="OrderChanged"/>
/// after a drag that changes the order.
/// </summary>
public partial class ReorderableListView : UserControl
{
	private ReorderableListViewEntry draggedEntry;
	private int dragStartIndex = -1;
	private bool isDragging;

	public ReorderableListView()
	{
		InitializeComponent();
		PreviewMouseMove += OnPreviewMouseMove;
		PreviewMouseLeftButtonUp += OnPreviewMouseLeftButtonUp;
	}

	/// <summary>
	/// Raised after a drag reorders the rows, carrying the row's start and end index.
	/// Not raised if the order is unchanged.
	/// </summary>
	public event EventHandler<ReorderedEventArgs> OrderChanged;

	/// <summary>The rows in their current visual order.</summary>
	public IReadOnlyList<ReorderableListViewEntry> Entries =>
		ItemsHost.Children.OfType<ReorderableListViewEntry>().ToList();

	/// <summary>Wraps <paramref name="content"/> in a new row, appends it, and returns the row.</summary>
	public ReorderableListViewEntry Add(object content)
	{
		ReorderableListViewEntry entry = new() { EntryContent = content };
		AddEntry(entry);
		return entry;
	}

	/// <summary>Appends an existing row and hooks up its drag handle.</summary>
	public void AddEntry(ReorderableListViewEntry entry)
	{
		if (entry is null) return;

		entry.DragHandlePressed += OnEntryDragHandlePressed;
		ItemsHost.Children.Add(entry);
	}

	/// <summary>Removes a row and unhooks its drag handle.</summary>
	public void RemoveEntry(ReorderableListViewEntry entry)
	{
		if (entry is null) return;

		entry.DragHandlePressed -= OnEntryDragHandlePressed;
		ItemsHost.Children.Remove(entry);
	}

	/// <summary>Removes all rows.</summary>
	public void Clear()
	{
		foreach (ReorderableListViewEntry entry in Entries)
		{
			entry.DragHandlePressed -= OnEntryDragHandlePressed;
		}

		ItemsHost.Children.Clear();
	}

	private void OnEntryDragHandlePressed(object sender, MouseButtonEventArgs e)
	{
		if (sender is not ReorderableListViewEntry entry) return;

		draggedEntry = entry;
		dragStartIndex = ItemsHost.Children.IndexOf(entry);
		isDragging = true;
		entry.Opacity = 0.6;
		CaptureMouse();
		e.Handled = true;
	}

	private void OnPreviewMouseMove(object sender, MouseEventArgs e)
	{
		if (!isDragging || draggedEntry is null) return;

		double pointerY = e.GetPosition(ItemsHost).Y;
		int oldIndex = ItemsHost.Children.IndexOf(draggedEntry);
		int newIndex = oldIndex;

		for (int i = 0; i < ItemsHost.Children.Count; i++)
		{
			if (i == oldIndex) continue;

			FrameworkElement child = (FrameworkElement)ItemsHost.Children[i];
			double center = child.TranslatePoint(new Point(0, 0), ItemsHost).Y + (child.ActualHeight / 2);

			// Moving up: land in front of the first row above us whose midpoint the pointer has passed.
			if (i < oldIndex && pointerY < center)
			{
				newIndex = i;
				break;
			}

			// Moving down: land after the last row below us whose midpoint the pointer has passed.
			if (i > oldIndex && pointerY > center)
			{
				newIndex = i;
			}
		}

		if (newIndex != oldIndex)
		{
			ItemsHost.Children.Remove(draggedEntry);
			ItemsHost.Children.Insert(newIndex, draggedEntry);
		}
	}

	private void OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (!isDragging) return;

		int endIndex = ItemsHost.Children.IndexOf(draggedEntry);
		int startIndex = dragStartIndex;
		draggedEntry.Opacity = 1.0;

		bool orderChanged = endIndex != startIndex;

		isDragging = false;
		draggedEntry = null;
		dragStartIndex = -1;
		ReleaseMouseCapture();

		if (orderChanged)
		{
			OrderChanged?.Invoke(this, new ReorderedEventArgs(startIndex, endIndex));
		}
	}
}

/// <summary>Describes a single reorder: the row moved from <see cref="OldIndex"/> to <see cref="NewIndex"/>.</summary>
public sealed class ReorderedEventArgs(int oldIndex, int newIndex) : EventArgs
{
	/// <summary>The row's index before the drag.</summary>
	public int OldIndex { get; } = oldIndex;

	/// <summary>The row's index after the drag.</summary>
	public int NewIndex { get; } = newIndex;
}
