using System.Windows;
using System.Windows.Controls;

namespace QuickScan;

/// <summary>
/// UI container for a set of sub-indexed protocol rows (e.g. the fixed sub-indexes of
/// Get FW Info). The group runs no scan itself — its children are ordinary entries that
/// the scan runs individually. The master checkbox bulk-toggles the children's enable state.
/// </summary>
public partial class QuickScanGroupControl : UserControl
{
	private readonly List<QuickScanEntryControl> children = new();
	private bool suppress;

	public QuickScanGroupControl()
	{
		// MasterCheck's IsChecked="True" raises Checked during InitializeComponent, before
		// the rest of the template (GroupHint, ChildPanel) exists. Suppress the cascade so
		// SetAll doesn't run against not-yet-created elements.
		suppress = true;
		InitializeComponent();
		suppress = false;
	}

	public void SetHeader(string name)
	{
		GroupName.Text = name;
	}

	public void AddRow(QuickScanEntryControl row)
	{
		children.Add(row);
		ChildPanel.Children.Add(row);
	}

	/// <summary>Reflects the children's enabled state in the master checkbox and hint (no cascade).</summary>
	public void SyncMaster()
	{
		int enabled = children.Count(c => c.Entry?.Enabled == true);

		suppress = true;
		MasterCheck.IsChecked = enabled == 0 ? false : enabled == children.Count ? true : (bool?)null;
		suppress = false;

		GroupHint.Text = $"{enabled}/{children.Count} enabled";
	}

	private void MasterCheck_Checked(object sender, RoutedEventArgs e)
	{
		if (!suppress) SetAll(true);
	}

	private void MasterCheck_Unchecked(object sender, RoutedEventArgs e)
	{
		if (!suppress) SetAll(false);
	}

	private void SetAll(bool enabled)
	{
		foreach (QuickScanEntryControl child in children) child.SetEnabled(enabled);
		if (GroupHint != null)
			GroupHint.Text = $"{children.Count(c => c.Entry?.Enabled == true)}/{children.Count} enabled";
	}
}
