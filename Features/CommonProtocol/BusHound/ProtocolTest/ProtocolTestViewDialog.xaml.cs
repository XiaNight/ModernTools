namespace CommonProtocol.BusHound.ProtocolTest;

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ModernWpf.Controls;

/// <summary>
/// Read-only view of a <see cref="TestProtocol"/>: its request bytes and, per packet, the expected
/// line paired with the most recent actual reply. Bytes on the actual line that fail to match are
/// highlighted red. Opened by clicking a row's OUT/IN preview text; editing is only reachable via
/// the edit button.
/// </summary>
public partial class ProtocolTestViewDialog : ContentDialog
{
	public ProtocolTestViewDialog()
	{
		InitializeComponent();
	}

	/// <summary>
	/// Populates the view from <paramref name="test"/> and the packets captured by the most recent
	/// run (<paramref name="received"/>, may be empty) and shows it.
	/// </summary>
	public async System.Threading.Tasks.Task ShowForAsync(TestProtocol test, IReadOnlyList<byte[]> received)
	{
		if (test == null)
			return;

		Title = string.IsNullOrWhiteSpace(test.Name) ? "Test details" : test.Name;
		RequestText.Text = string.IsNullOrWhiteSpace(test.RequestHex) ? "(no request)" : test.RequestHex.Trim();

		BuildPackets(test, received ?? Array.Empty<byte[]>());

		TrailingNote.Text = test.AllowTrailingWildcard
			? "Extra trailing bytes on received packets are ignored (trailing wildcard on)."
			: "Received packets must match the expected length exactly.";

		await ShowAsync();
	}

	private void BuildPackets(TestProtocol test, IReadOnlyList<byte[]> received)
	{
		PacketsPanel.Children.Clear();

		List<string> expectedLines = test.ExpectedLines ?? new List<string>();
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

			// Expected line (plain text; X marks wildcard nibbles).
			string expectedText = hasExpected
				? (expected != null ? expected.ToString() : expectedLines[i].Trim())
				: "(no expected line)";
			group.Children.Add(LabeledLine("Expected", expectedText, labelBrush, normalBrush));

			// Actual line, colouring each mismatched byte red.
			byte[] actual = i < received.Count ? received[i] : null;
			if (actual == null)
				group.Children.Add(LabeledLine("Actual", "(none received)", labelBrush, labelBrush));
			else
				group.Children.Add(ActualLine(actual, expected, test.AllowTrailingWildcard,
					labelBrush, normalBrush, mismatchFg, mismatchBg));

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
			// No expected line at all means every received byte is unexpected.
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