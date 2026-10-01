namespace CommonProtocol.BusHound.ProtocolTest;

using System.Windows;
using ModernWpf.Controls;

public partial class SuiteEditDialog : ContentDialog
{
	public SuiteEditDialog()
	{
		InitializeComponent();
		PrimaryButtonClick += OnPrimaryButtonClick;
	}

	public async System.Threading.Tasks.Task<bool> EditAsync(TestSuite suite)
	{
		if (suite == null) return false;

		Title = string.IsNullOrWhiteSpace(suite.Name) || suite.Name == "New suite"
			? "New suite"
			: $"Edit '{suite.Name}'";

		NameBox.Text = suite.Name ?? string.Empty;
		DescriptionBox.Text = suite.Description ?? string.Empty;
		ResetWhenDoneCheck.IsChecked = suite.ResetWhenDone;
		StopOnFirstFailureCheck.IsChecked = suite.StopOnFirstFailure;
		HideError();

		ContentDialogResult result = await ShowAsync();
		if (result != ContentDialogResult.Primary)
			return false;

		suite.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "Untitled suite" : NameBox.Text.Trim();
		suite.Description = DescriptionBox.Text.Trim();
		suite.ResetWhenDone = ResetWhenDoneCheck.IsChecked == true;
		suite.StopOnFirstFailure = StopOnFirstFailureCheck.IsChecked == true;
		return true;
	}

	private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
	{
		if (string.IsNullOrWhiteSpace(NameBox.Text))
		{
			ShowError("Name is required.");
			args.Cancel = true;
			return;
		}

		HideError();
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
