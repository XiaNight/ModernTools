using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Base.Components;

/// <summary>
/// A single row in a <see cref="ReorderableListView"/>: a drag handle on the left and an
/// arbitrary content container on the right. The handle raises <see cref="DragHandlePressed"/>
/// so the owning list can start a reorder drag; the entry itself holds no ordering logic.
/// </summary>
public partial class ReorderableListViewEntry : UserControl
{
	public ReorderableListViewEntry()
	{
		InitializeComponent();
		DragHandle.PreviewMouseLeftButtonDown += OnDragHandlePressed;
	}

	public static readonly DependencyProperty EntryContentProperty =
		DependencyProperty.Register(nameof(EntryContent), typeof(object), typeof(ReorderableListViewEntry),
			new PropertyMetadata(null));

	/// <summary>Whatever this row should display in its right-hand container.</summary>
	public object EntryContent
	{
		get => GetValue(EntryContentProperty);
		set => SetValue(EntryContentProperty, value);
	}

	/// <summary>Raised when the drag handle is pressed. Sender is this entry.</summary>
	public event MouseButtonEventHandler DragHandlePressed;

	private void OnDragHandlePressed(object sender, MouseButtonEventArgs e)
	{
		DragHandlePressed?.Invoke(this, e);
	}
}
