namespace CommonProtocol;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

/// <summary>
/// One row in the Bus Hound Tests panel. Shows the test name, a truncated preview of the
/// request/expected packets, a verdict badge, and Send / Edit / Delete actions. The row is a
/// thin view over a <see cref="ProtocolTest"/>; the hosting page owns running and persistence
/// and drives the verdict through <see cref="ShowRunning"/> / <see cref="ShowResult"/> / <see cref="ResetVerdict"/>.
/// </summary>
public partial class ProtocolTestEntry : UserControl
{
	private ProtocolTest test;
	private bool suppressNameEvent;

	public ProtocolTestEntry()
	{
		InitializeComponent();

		SendButton.Click += (_, _) => SendRequested?.Invoke(this, EventArgs.Empty);
		EditButton.Click += (_, _) => EditRequested?.Invoke(this, EventArgs.Empty);
		DeleteButton.Click += (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty);
		PreviewBlock.MouseLeftButtonUp += (_, _) => EditRequested?.Invoke(this, EventArgs.Empty);
		NameBox.TextChanged += NameBox_TextChanged;
		NameBox.LostFocus += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>The test this row is bound to.</summary>
	public ProtocolTest Test => test;

	public event EventHandler SendRequested;
	public event EventHandler EditRequested;
	public event EventHandler DeleteRequested;

	/// <summary>Raised when the row commits an in-place edit (e.g. the name) that should be persisted.</summary>
	public event EventHandler Changed;

	/// <summary>Binds the row to a test and refreshes its name, preview, and verdict.</summary>
	public void Bind(ProtocolTest boundTest)
	{
		test = boundTest;

		suppressNameEvent = true;
		NameBox.Text = test?.Name ?? string.Empty;
		suppressNameEvent = false;

		RefreshPreview();
		ResetVerdict();
	}

	/// <summary>Re-reads the request/expected summary into the preview line.</summary>
	public void RefreshPreview()
	{
		PreviewBlock.Text = test?.BuildPreview() ?? string.Empty;
	}

	public void ResetVerdict() => SetBadge("—", "SkipBg", null);

	public void ShowRunning() => SetBadge("…", "SkipBg", null);

	/// <summary>Shows a pass/fail verdict; <paramref name="message"/> becomes the badge tooltip.</summary>
	public void ShowResult(bool pass, string message = null)
	{
		if (pass)
			SetBadge("PASS", "PassBg", message, "PassFg");
		else
			SetBadge("FAIL", "FailBg", message, "FailFg");
	}

	private void SetBadge(string text, string backgroundKey, string tooltip, string foregroundKey = null)
	{
		BadgeText.Text = text;

		if (TryFindResource(backgroundKey) is Brush background)
			Badge.Background = background;

		BadgeText.Foreground = foregroundKey != null && TryFindResource(foregroundKey) is Brush foreground
			? foreground
			: (Brush)FindResource("SystemControlForegroundBaseHighBrush");

		Badge.ToolTip = string.IsNullOrWhiteSpace(tooltip) ? null : tooltip;
	}

	private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (suppressNameEvent || test == null) return;
		test.Name = NameBox.Text;
	}
}