namespace CommonProtocol.BusHound.ProtocolTest;

using Base.Helpers;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

/// <summary>The outcome shown on a row's verdict badge.</summary>
public enum TestVerdict
{
	Pass,
	Timeout,
	Mismatch,
	Error,
}

/// <summary>
/// One row in the Bus Hound Tests panel. Shows the test name, a truncated preview of the
/// request/expected packets, a verdict badge, and Send / Edit / Delete actions. The row is a
/// thin view over a <see cref="TestProtocol"/>; the hosting page owns running and persistence
/// and drives the verdict through <see cref="ShowRunning"/> / <see cref="ShowResult"/> / <see cref="ResetVerdict"/>.
/// </summary>
public partial class ProtocolTestEntry : UserControl
{
	private TestProtocol test;
	private bool suppressNameEvent;
	private readonly List<byte[]> lastReceived = new();

	public ProtocolTestEntry()
	{
		InitializeComponent();

		SendButton.Click += (_, _) => SendRequested?.Invoke(this, EventArgs.Empty);
		EditButton.Click += (_, _) => EditRequested?.Invoke(this, EventArgs.Empty);
		DeleteButton.Click += (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty);
		PreviewBlock.MouseLeftButtonUp += (_, _) => ViewRequested?.Invoke(this, EventArgs.Empty);
		ReceivedBlock.MouseLeftButtonUp += (_, _) => ViewRequested?.Invoke(this, EventArgs.Empty);
		NameBox.TextChanged += NameBox_TextChanged;
		NameBox.LostFocus += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>The test this row is bound to.</summary>
	public TestProtocol Test => test;

	/// <summary>Packets captured by the most recent run, for the read-only view. Empty until a run.</summary>
	public IReadOnlyList<byte[]> LastReceived => lastReceived;

	public event EventHandler SendRequested;
	public event EventHandler EditRequested;
	public event EventHandler DeleteRequested;

	/// <summary>Raised when the preview text is clicked to open the read-only view.</summary>
	public event EventHandler ViewRequested;

	/// <summary>Raised when the row commits an in-place edit (e.g. the name) that should be persisted.</summary>
	public event EventHandler Changed;

	/// <summary>Binds the row to a test and refreshes its name, preview, and verdict.</summary>
	public void Bind(TestProtocol boundTest)
	{
		test = boundTest;

		suppressNameEvent = true;
		NameBox.Text = test?.Name ?? string.Empty;
		suppressNameEvent = false;

		RefreshPreview();
		ResetVerdict();
		ClearReceived();
	}

	/// <summary>Re-reads the request/expected summary into the preview line.</summary>
	public void RefreshPreview()
	{
		PreviewBlock.Text = test?.BuildPreview() ?? string.Empty;
	}

	/// <summary>
	/// Shows what a run received: the packet count (when more than one) and a brief view of the
	/// first packet with trailing zeros trimmed. The block is always visible; a null/empty list
	/// simply clears its text.
	/// </summary>
	public void ShowReceived(IReadOnlyList<byte[]> packets)
	{
		if (packets == null || packets.Count == 0)
		{
			ClearReceived();
			return;
		}

		lastReceived.Clear();
		lastReceived.AddRange(packets);

		string prefix = packets.Count > 1 ? $"IN  ×{packets.Count}  " : "IN   ";
		ReceivedBlock.Text = prefix + TestProtocol.FormatBrief(packets[0]);
	}

	/// <summary>Clears the received summary text and stored packets. The block stays visible (shows nothing).</summary>
	public void ClearReceived()
	{
		lastReceived.Clear();
		ReceivedBlock.Text = string.Empty;
	}

	/// <summary>Verdict of the most recent run, or null if not run since binding. For the API/check surface.</summary>
	public TestVerdict? LastVerdict { get; private set; }

	/// <summary>Response time of the most recent run in milliseconds, or null if not measured.</summary>
	public double? LastElapsedMs { get; private set; }

	/// <summary>Message from the most recent run (verdict detail), or null.</summary>
	public string LastMessage { get; private set; }

	public void ResetVerdict()
	{
		LastVerdict = null;
		LastElapsedMs = null;
		LastMessage = null;
		SetBadge("--", "SkipBg", null);
		SetResponseTime(null);
	}

	public void ShowRunning()
	{
		ClearReceived();
		SetBadge("…", "SkipBg", null);
		SetResponseTime(null);
	}

	/// <summary>
	/// Shows the verdict badge with the reason (PASS / TIMEOUT / MISMATCH / ERROR), the elapsed
	/// response time (when measured), and <paramref name="message"/> as the badge tooltip.
	/// </summary>
	public void ShowResult(TestVerdict verdict, string message, TimeSpan elapsed)
	{
		LastVerdict = verdict;
		LastMessage = message;
		LastElapsedMs = elapsed > TimeSpan.Zero ? elapsed.TotalMilliseconds : null;

		switch (verdict)
		{
			case TestVerdict.Pass:
				SetBadge("PASS", "PassBg", message, "PassFg");
				break;
			case TestVerdict.Timeout:
				SetBadge("TIMEOUT", "FailBg", message, "FailFg");
				break;
			case TestVerdict.Mismatch:
				SetBadge("MISMATCH", "FailBg", message, "FailFg");
				break;
			default:
				SetBadge("ERROR", "FailBg", message, "FailFg");
				break;
		}

		// A pre-run failure (invalid request, not connected) has no meaningful timing.
		SetResponseTime(elapsed > TimeSpan.Zero ? elapsed : null);
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

	private void SetResponseTime(TimeSpan? elapsed)
	{
		if (elapsed is null)
		{
			ResponseTimeText.Text = "--";
			return;
		}

		ResponseTimeText.Text = Utilities.FormatInterval(elapsed.Value);
	}

	private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (suppressNameEvent || test == null) return;
		test.Name = NameBox.Text;
	}
}