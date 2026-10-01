namespace CommonProtocol.BusHound.ProtocolTest;

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ModernWpf.Controls;

public partial class StepEditDialog : ContentDialog
{
	private bool recording;
	private StepType currentType = StepType.Send;

	public StepEditDialog()
	{
		InitializeComponent();
		PrimaryButtonClick += OnPrimaryButtonClick;
		RecordButton.Click += (_, _) => ToggleRecord();
		Closing += (_, _) => StopRecordingIfActive();
		TypeCombo.SelectionChanged += TypeCombo_SelectionChanged;
	}

	public event EventHandler RecordStartRequested;
	public event EventHandler RecordStopRequested;

	public string CurrentRequestHex => RequestBox.Text;
	public bool IsRecording => recording;

	public async System.Threading.Tasks.Task<bool> EditAsync(TestStep step)
	{
		if (step == null) return false;

		Title = string.IsNullOrWhiteSpace(step.Name) || step.Name == "New step"
			? "New step"
			: $"Edit '{step.Name}'";

		NameBox.Text = step.Name ?? string.Empty;

		switch (step)
		{
			case SendStep send:
				SelectType(StepType.Send);
				RequestBox.Text = send.RequestHex ?? string.Empty;
				break;

			case SendAndWaitStep wait:
				SelectType(StepType.SendAndWait);
				RequestBox.Text = wait.RequestHex ?? string.Empty;
				TimeoutBox.Value = wait.TotalTimeoutMs;
				ExpectedCountBox.Value = wait.ExpectedCount;
				break;

			case SendAndAssertStep assert:
				SelectType(StepType.SendAndAssert);
				RequestBox.Text = assert.RequestHex ?? string.Empty;
				ExpectedBox.Text = string.Join(Environment.NewLine, assert.ExpectedLines ?? new List<string>());
				TimeoutBox.Value = assert.TotalTimeoutMs;
				TrailingWildcardCheck.IsChecked = assert.AllowTrailingWildcard;
				break;

			case SleepStep sleep:
				SelectType(StepType.Sleep);
				DurationBox.Value = sleep.DurationMs;
				break;
		}

		SetRecordStatus(string.Empty);
		HideError();

		ContentDialogResult result = await ShowAsync();
		if (result != ContentDialogResult.Primary)
			return false;

		ApplyToStep(step);
		return true;
	}

	public async System.Threading.Tasks.Task<TestStep> CreateNewAsync(StepType defaultType = StepType.SendAndAssert)
	{
		Title = "New step";
		NameBox.Text = "New step";
		RequestBox.Text = string.Empty;
		ExpectedBox.Text = string.Empty;
		TimeoutBox.Value = defaultType == StepType.SendAndWait ? 100 : 10;
		ExpectedCountBox.Value = 1;
		DurationBox.Value = 100;
		TrailingWildcardCheck.IsChecked = true;
		SelectType(defaultType);
		SetRecordStatus(string.Empty);
		HideError();

		ContentDialogResult result = await ShowAsync();
		if (result != ContentDialogResult.Primary)
			return null;

		TestStep step = currentType switch
		{
			StepType.Send => new SendStep(),
			StepType.SendAndWait => new SendAndWaitStep(),
			StepType.SendAndAssert => new SendAndAssertStep(),
			StepType.Sleep => new SleepStep(),
			_ => new SendStep(),
		};

		ApplyToStep(step);
		return step;
	}

	private void ApplyToStep(TestStep step)
	{
		step.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "Untitled step" : NameBox.Text.Trim();

		switch (step)
		{
			case SendStep send:
				send.RequestHex = RequestBox.Text.Trim();
				break;

			case SendAndWaitStep wait:
				wait.RequestHex = RequestBox.Text.Trim();
				wait.TotalTimeoutMs = double.IsNaN(TimeoutBox.Value) ? 100 : (int)TimeoutBox.Value;
				wait.ExpectedCount = double.IsNaN(ExpectedCountBox.Value) ? 1 : (int)ExpectedCountBox.Value;
				break;

			case SendAndAssertStep assert:
				assert.RequestHex = RequestBox.Text.Trim();
				assert.ExpectedLines = SplitLines(ExpectedBox.Text);
				assert.TotalTimeoutMs = double.IsNaN(TimeoutBox.Value) ? 10 : (int)TimeoutBox.Value;
				assert.AllowTrailingWildcard = TrailingWildcardCheck.IsChecked == true;
				break;

			case SleepStep sleep:
				sleep.DurationMs = double.IsNaN(DurationBox.Value) ? 100 : (int)DurationBox.Value;
				break;
		}
	}

	private void SelectType(StepType type)
	{
		currentType = type;
		int index = type switch
		{
			StepType.Send => 0,
			StepType.SendAndWait => 1,
			StepType.SendAndAssert => 2,
			StepType.Sleep => 3,
			_ => 0,
		};
		TypeCombo.SelectedIndex = index;
		UpdateFieldVisibility();
	}

	private void TypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (TypeCombo.SelectedItem is ComboBoxItem item && item.Tag is string tag)
		{
			currentType = tag switch
			{
				"Send" => StepType.Send,
				"SendAndWait" => StepType.SendAndWait,
				"SendAndAssert" => StepType.SendAndAssert,
				"Sleep" => StepType.Sleep,
				_ => StepType.Send,
			};
			UpdateFieldVisibility();
		}
	}

	private void UpdateFieldVisibility()
	{
		bool hasSend = currentType != StepType.Sleep;
		bool hasExpected = currentType == StepType.SendAndAssert;
		bool hasTimeout = currentType == StepType.SendAndWait || currentType == StepType.SendAndAssert;
		bool hasExpectedCount = currentType == StepType.SendAndWait;
		bool hasSleep = currentType == StepType.Sleep;

		RequestPanel.Visibility = hasSend ? Visibility.Visible : Visibility.Collapsed;
		ExpectedPanel.Visibility = hasExpected ? Visibility.Visible : Visibility.Collapsed;
		TimeoutPanel.Visibility = hasTimeout ? Visibility.Visible : Visibility.Collapsed;
		ExpectedCountPanel.Visibility = hasExpectedCount ? Visibility.Visible : Visibility.Collapsed;
		SleepPanel.Visibility = hasSleep ? Visibility.Visible : Visibility.Collapsed;
	}

	// ---- recording ----

	public void ClearExpectedForRecording() => ExpectedBox.Text = string.Empty;

	public void AppendRecordedPacket(string hexLine)
	{
		if (string.IsNullOrWhiteSpace(hexLine)) return;

		if (ExpectedBox.Text.Length > 0 && !ExpectedBox.Text.EndsWith("\n"))
			ExpectedBox.AppendText(Environment.NewLine);
		ExpectedBox.AppendText(hexLine.Trim());
		ExpectedBox.ScrollToEnd();
	}

	public void SetRecordStatus(string message)
	{
		RecordStatusText.Text = message ?? string.Empty;
	}

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
		if (currentType != StepType.Sleep)
		{
			if (!HexBytes.TryParse(RequestBox.Text, out _, out string requestError))
			{
				ShowError($"Request bytes: {requestError}");
				args.Cancel = true;
				return;
			}
		}

		if (currentType == StepType.SendAndAssert)
		{
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
