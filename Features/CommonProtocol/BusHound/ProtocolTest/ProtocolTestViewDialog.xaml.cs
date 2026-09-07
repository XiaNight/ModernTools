namespace CommonProtocol.BusHound.ProtocolTest;

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ModernWpf.Controls;

public partial class ProtocolTestViewDialog : ContentDialog
{
	public ProtocolTestViewDialog()
	{
		InitializeComponent();
	}

	public async System.Threading.Tasks.Task ShowForStepAsync(TestStep step, StepRunResult result)
	{
		if (step == null)
			return;

		Title = string.IsNullOrWhiteSpace(step.Name) ? "Step details" : step.Name;

		IReadOnlyList<byte[]> received = result?.Received ?? new List<byte[]>();

		switch (step)
		{
			case SendStep send:
				RequestText.Text = string.IsNullOrWhiteSpace(send.RequestHex) ? "(no request)" : send.RequestHex.Trim();
				BuildSendOnlyView();
				TrailingNote.Text = string.Empty;
				break;

			case SendAndWaitStep wait:
				RequestText.Text = string.IsNullOrWhiteSpace(wait.RequestHex) ? "(no request)" : wait.RequestHex.Trim();
				BuildWaitView(received, wait.ExpectedCount);
				TrailingNote.Text = string.Empty;
				break;

			case SendAndAssertStep assert:
				RequestText.Text = string.IsNullOrWhiteSpace(assert.RequestHex) ? "(no request)" : assert.RequestHex.Trim();
				BuildAssertView(assert, received);
				TrailingNote.Text = assert.AllowTrailingWildcard
					? "Extra trailing bytes on received packets are ignored (trailing wildcard on)."
					: "Received packets must match the expected length exactly.";
				break;

			case SleepStep sleep:
				RequestText.Text = $"Sleep {sleep.DurationMs}ms";
				PacketsPanel.Children.Clear();
				PacketsPanel.Children.Add(MutedLine(result?.Message ?? "(no result)"));
				TrailingNote.Text = string.Empty;
				break;
		}

		await ShowAsync();
	}

	private void BuildSendOnlyView()
	{
		PacketsPanel.Children.Clear();
		PacketsPanel.Children.Add(MutedLine("(fire and forget — no response captured)"));
	}

	private void BuildWaitView(IReadOnlyList<byte[]> received, int expectedCount)
	{
		PacketsPanel.Children.Clear();

		if (received.Count == 0)
		{
			PacketsPanel.Children.Add(MutedLine("(no packets received)"));
			return;
		}

		Brush labelBrush = (Brush)FindResource("SystemControlForegroundBaseMediumBrush");
		Brush normalBrush = (Brush)FindResource("SystemControlForegroundBaseHighBrush");

		for (int i = 0; i < received.Count; i++)
		{
			StackPanel group = new() { Margin = new Thickness(0, i == 0 ? 0 : 8, 0, 0) };
			group.Children.Add(new TextBlock
			{
				Text = $"Packet {i + 1}",
				FontWeight = FontWeights.SemiBold,
				Margin = new Thickness(0, 0, 0, 2),
			});
			group.Children.Add(LabeledLine("Received", HexFormat.FormatBrief(received[i]), labelBrush, normalBrush));
			PacketsPanel.Children.Add(group);
		}
	}

	private void BuildAssertView(SendAndAssertStep assert, IReadOnlyList<byte[]> received)
	{
		PacketsPanel.Children.Clear();

		List<string> expectedLines = assert.ExpectedLines ?? new List<string>();
		int groups = Math.Max(expectedLines.Count, received.Count);

		if (groups == 0)
		{
			PacketsPanel.Children.Add(MutedLine("(no packets)"));
			return;
		}

		Brush labelBrush = (Brush)FindResource("SystemControlForegroundBaseMediumBrush");
		Brush normalBrush = (Brush)FindResource("SystemControlForegroundBaseHighBrush");
		Brush mismatchFg = (Brush)FindResource("MismatchFg");
		Brush mismatchBg = (Brush)FindResource("MismatchBg");

		for (int i = 0; i < groups; i++)
		{
			bool hasExpected = i < expectedLines.Count;
			ExpectedPacket expected = null;
			if (hasExpected)
				ExpectedPacket.TryParse(expectedLines[i], out expected, out _);

			StackPanel group = new() { Margin = new Thickness(0, i == 0 ? 0 : 8, 0, 0) };

			group.Children.Add(new TextBlock
			{
				Text = $"Packet {i + 1}",
				FontWeight = FontWeights.SemiBold,
				Margin = new Thickness(0, 0, 0, 2),
			});

			string expectedText = hasExpected
				? (expected != null ? expected.ToString() : expectedLines[i].Trim())
				: "(no expected line)";
			group.Children.Add(LabeledLine("Expected", expectedText, labelBrush, normalBrush));

			byte[] actual = i < received.Count ? received[i] : null;
			if (actual == null)
			{
				group.Children.Add(LabeledLine("Actual", "(none received)", labelBrush, labelBrush));
			}
			else
			{
				group.Children.Add(ActualLine(actual, expected, assert.AllowTrailingWildcard,
					labelBrush, normalBrush, mismatchFg, mismatchBg));
			}

			PacketsPanel.Children.Add(group);
		}
	}

	private static TextBlock LabeledLine(string label, string value, Brush labelBrush, Brush valueBrush)
	{
		TextBlock tb = new()
		{
			FontFamily = new FontFamily("Consolas"),
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0, 1, 0, 0),
		};
		tb.Inlines.Add(new Run($"{label,-9}") { Foreground = labelBrush });
		tb.Inlines.Add(new Run(value) { Foreground = valueBrush });
		return tb;
	}

	private static TextBlock ActualLine(byte[] actual, ExpectedPacket expected, bool allowTrailing,
		Brush labelBrush, Brush normalBrush, Brush mismatchFg, Brush mismatchBg)
	{
		TextBlock tb = new()
		{
			FontFamily = new FontFamily("Consolas"),
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0, 1, 0, 0),
		};
		tb.Inlines.Add(new Run("Actual".PadRight(9)) { Foreground = labelBrush });

		for (int i = 0; i < actual.Length; i++)
		{
			bool matched = expected != null && expected.ByteMatches(i, actual[i], allowTrailing);

			Run run = new(actual[i].ToString("X2"));
			if (matched)
			{
				run.Foreground = normalBrush;
			}
			else
			{
				run.Foreground = mismatchFg;
				run.Background = mismatchBg;
			}

			tb.Inlines.Add(run);
			if (i < actual.Length - 1)
				tb.Inlines.Add(new Run(" ") { Foreground = normalBrush });
		}

		return tb;
	}

	private TextBlock MutedLine(string text)
		=> new()
		{
			Text = text,
			Foreground = (Brush)FindResource("SystemControlForegroundBaseMediumBrush"),
		};
}
