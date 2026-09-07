namespace CommonProtocol.BusHound.ProtocolTest;

using Base.Helpers;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

public partial class TestStepEntry : UserControl
{
	private TestStep step;
	private readonly List<byte[]> lastReceived = new();

	public TestStepEntry()
	{
		InitializeComponent();

		EditButton.Click += (_, _) => EditRequested?.Invoke(this, EventArgs.Empty);
		DeleteButton.Click += (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty);
		PreviewBlock.MouseLeftButtonUp += (_, _) => ViewRequested?.Invoke(this, EventArgs.Empty);
		ReceivedBlock.MouseLeftButtonUp += (_, _) => ViewRequested?.Invoke(this, EventArgs.Empty);
	}

	public TestStep Step => step;

	public IReadOnlyList<byte[]> LastReceived => lastReceived;

	public event EventHandler EditRequested;
	public event EventHandler DeleteRequested;
	public event EventHandler ViewRequested;

	public TestVerdict? LastVerdict { get; private set; }
	public double? LastElapsedMs { get; private set; }
	public string LastMessage { get; private set; }

	public void Bind(TestStep boundStep)
	{
		step = boundStep;
		RefreshPreview();
		ResetVerdict();
		ClearReceived();
	}

	public void RefreshPreview()
	{
		if (step == null)
		{
			PreviewBlock.Text = string.Empty;
			TypeIcon.Glyph = "";
			return;
		}

		PreviewBlock.Text = step.BuildPreview();

		TypeIcon.Glyph = step.StepType switch
		{
			StepType.Send => "",
			StepType.SendAndWait => "",
			StepType.SendAndAssert => "",
			StepType.Sleep => "",
			_ => "",
		};
	}

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
		ReceivedBlock.Text = prefix + HexFormat.FormatBrief(packets[0]);
	}

	public void ClearReceived()
	{
		lastReceived.Clear();
		ReceivedBlock.Text = string.Empty;
	}

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
			case TestVerdict.Skipped:
				SetBadge("SKIP", "SkipBg", message);
				break;
			default:
				SetBadge("ERROR", "FailBg", message, "FailFg");
				break;
		}

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
}
