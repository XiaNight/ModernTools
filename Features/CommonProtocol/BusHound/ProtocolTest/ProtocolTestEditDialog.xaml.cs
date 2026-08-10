namespace CommonProtocol.BusHound.ProtocolTest;

using System;
using System.Collections.Generic;
using System.Windows;
using ModernWpf.Controls;

/// <summary>
/// Modal editor for a single <see cref="TestProtocol"/>: name, request bytes, expected return
/// packets (one per line), total timeout, and the trailing-wildcard option. Hex is parsed the same
/// way as the Bus Hound page's ParseCommand (whitespace-insensitive). The Record button lets the
/// hosting page fire the request and stream received packets straight into the expected field.
/// </summary>
public partial class ProtocolTestEditDialog : ContentDialog
{
	private bool recording;

	public ProtocolTestEditDialog()
	{
		InitializeComponent();
		PrimaryButtonClick += OnPrimaryButtonClick;
		RecordButton.Click += (_, _) => ToggleRecord();
		Closing += (_, _) => StopRecordingIfActive();
	}

	/// <summary>Raised when the user starts recording; the page should send the request and stream packets in.</summary>
	public event EventHandler RecordStartRequested;

	/// <summary>Raised when the user stops recording (or the dialog closes mid-recording).</summary>
	public event EventHandler RecordStopRequested;

	/// <summary>Current request text, so the page knows what frame to send when recording.</summary>
	public string CurrentRequestHex => RequestBox.Text;

	/// <summary>Whether a recording session is currently active.</summary>
	public bool IsRecording => recording;

	/// <summary>
	/// Shows the dialog populated from <paramref name="test"/>. Returns true and applies the edits
	/// to <paramref name="test"/> if the user saved; false if cancelled.
	/// </summary>
	public async System.Threading.Tasks.Task<bool> EditAsync(TestProtocol test)
	{
		if (test == null) return false;

		Title = string.IsNullOrWhiteSpace(test.Name) ? "New test" : $"Edit '{test.Name}'";
		NameBox.Text = test.Name ?? string.Empty;
		RequestBox.Text = test.RequestHex ?? string.Empty;
		ExpectedBox.Text = string.Join(Environment.NewLine, test.ExpectedLines ?? new List<string>());
		TimeoutBox.Value = test.TotalTimeoutMs;
		TrailingWildcardCheck.IsChecked = test.AllowTrailingWildcard;
		SetRecordStatus(string.Empty);
		HideError();

		ContentDialogResult result = await ShowAsync();
		if (result != ContentDialogResult.Primary)
			return false;

		test.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "Untitled test" : NameBox.Text.Trim();
		test.RequestHex = RequestBox.Text.Trim();
		test.ExpectedLines = SplitLines(ExpectedBox.Text);
		test.TotalTimeoutMs = double.IsNaN(TimeoutBox.Value) ? 0 : (int)TimeoutBox.Value;
		test.AllowTrailingWildcard = TrailingWildcardCheck.IsChecked == true;
		return true;
	}

	// ---- recording (driven by the hosting page) ----

	/// <summary>Clears the expected field in preparation for a fresh recording session.</summary>
	public void ClearExpectedForRecording() => ExpectedBox.Text = string.Empty;

	/// <summary>Appends one recorded packet as a new expected line.</summary>
	public void AppendRecordedPacket(string hexLine)
	{
		if (string.IsNullOrWhiteSpace(hexLine)) return;

		if (ExpectedBox.Text.Length > 0 && !ExpectedBox.Text.EndsWith("\n"))
			ExpectedBox.AppendText(Environment.NewLine);
		ExpectedBox.AppendText(hexLine.Trim());
		ExpectedBox.ScrollToEnd();
	}

	/// <summary>Shows a short status message next to the Record button.</summary>
	public void SetRecordStatus(string message)
	{
		RecordStatusText.Text = message ?? string.Empty;
	}

	/// <summary>
	/// Called by the page to force-stop recording (e.g. when the interface could not be opened),
	/// resetting the button without re-raising the stop event.
	/// </summary>
	public void NotifyRecordingStopped(string message = null)
	{
		recording = false;
		RecordButtonText.Text = "Record";
		if (message != null) SetRecordStatus(message);
	}

	private void ToggleRecord()
	{
		if (recording)
		{
			recording = false;
			RecordButtonText.Text = "Record";
			RecordStopRequested?.Invoke(this, EventArgs.Empty);
		}
		else
		{
			recording = true;
			RecordButtonText.Text = "Stop";
			SetRecordStatus("Recording…");
			RecordStartRequested?.Invoke(this, EventArgs.Empty);
		}
	}

	private void StopRecordingIfActive()
	{
		if (!recording) return;
		recording = false;
		RecordStopRequested?.Invoke(this, EventArgs.Empty);
	}

	// ---- validation ----

	private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
	{
		if (!HexBytes.TryParse(RequestBox.Text, out _, out string requestError))
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