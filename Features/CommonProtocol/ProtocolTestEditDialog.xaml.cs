namespace CommonProtocol;

using System;
using System.Collections.Generic;
using System.Windows;
using ModernWpf.Controls;

/// <summary>
/// Modal editor for a single <see cref="ProtocolTest"/>: name, request bytes, expected return
/// packets (one per line), and total timeout. Validates the hex on Save and only writes the
/// edited values back into the test when the user confirms.
/// </summary>
public partial class ProtocolTestEditDialog : ContentDialog
{
	public ProtocolTestEditDialog()
	{
		InitializeComponent();
		PrimaryButtonClick += OnPrimaryButtonClick;
	}

	/// <summary>
	/// Shows the dialog populated from <paramref name="test"/>. Returns true and applies the edits
	/// to <paramref name="test"/> if the user saved; false if cancelled.
	/// </summary>
	public async System.Threading.Tasks.Task<bool> EditAsync(ProtocolTest test)
	{
		if (test == null) return false;

		Title = string.IsNullOrWhiteSpace(test.Name) ? "New test" : $"Edit '{test.Name}'";
		NameBox.Text = test.Name ?? string.Empty;
		RequestBox.Text = test.RequestHex ?? string.Empty;
		ExpectedBox.Text = string.Join(Environment.NewLine, test.ExpectedLines ?? new List<string>());
		TimeoutBox.Value = test.TotalTimeoutMs;
		HideError();

		ContentDialogResult result = await ShowAsync();
		if (result != ContentDialogResult.Primary)
			return false;

		test.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "Untitled test" : NameBox.Text.Trim();
		test.RequestHex = RequestBox.Text.Trim();
		test.ExpectedLines = SplitLines(ExpectedBox.Text);
		test.TotalTimeoutMs = double.IsNaN(TimeoutBox.Value) ? 0 : (int)TimeoutBox.Value;
		return true;
	}

	private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
	{
		if (!TryValidateHex(RequestBox.Text, out string requestError))
		{
			ShowError($"Request bytes: {requestError}");
			args.Cancel = true;
			return;
		}

		List<string> lines = SplitLines(ExpectedBox.Text);
		if (lines.Count == 0)
		{
			ShowError("Add at least one expected packet line.");
			args.Cancel = true;
			return;
		}

		for (int i = 0; i < lines.Count; i++)
		{
			if (!ExpectedPacket.TryParse(lines[i], out _, out string lineError))
			{
				ShowError($"Expected line {i + 1}: {lineError}");
				args.Cancel = true;
				return;
			}
		}

		HideError();
	}

	/// <summary>Validates a plain hex string (no wildcards): whitespace-separated two-char hex bytes.</summary>
	private static bool TryValidateHex(string text, out string error)
	{
		error = null;

		string[] tokens = (text ?? string.Empty).Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
		if (tokens.Length == 0)
		{
			error = "enter at least one byte to send.";
			return false;
		}

		foreach (string token in tokens)
		{
			if (token.Length != 2 || !IsHexDigit(token[0]) || !IsHexDigit(token[1]))
			{
				error = $"'{token}' is not a two-character hex byte.";
				return false;
			}
		}

		return true;
	}

	private static bool IsHexDigit(char c)
		=> (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

	private static List<string> SplitLines(string text)
	{
		List<string> result = new();
		if (string.IsNullOrEmpty(text)) return result;

		string[] rawLines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
		foreach (string raw in rawLines)
		{
			string trimmed = raw.Trim();
			if (trimmed.Length > 0)
				result.Add(trimmed);
		}

		return result;
	}

	private void ShowError(string message)
	{
		ErrorText.Text = message;
		ErrorText.Visibility = Visibility.Visible;
	}

	private void HideError()
	{
		ErrorText.Text = string.Empty;
		ErrorText.Visibility = Visibility.Collapsed;
	}
}