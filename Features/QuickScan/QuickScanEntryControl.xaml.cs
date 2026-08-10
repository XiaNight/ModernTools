using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Base.Protocol;

namespace QuickScan;

/// <summary>
/// One protocol row: enable toggle, verdict badge, name/detail, validation-mode picker,
/// and editable index/data bytes. Writes edits straight back to its <see cref="QuickScanEntry"/>.
/// </summary>
public partial class QuickScanEntryControl : UserControl
{
	public QuickScanEntry Entry { get; private set; }

	public QuickScanEntryControl()
	{
		InitializeComponent();
	}

	/// <summary>Binds an entry to the row and initialises the controls.</summary>
	public void Bind(QuickScanEntry entry)
	{
		Entry = entry;

		NameText.Text = entry.Name + (entry.RequiresParam ? "  • needs param" : "");
		if (!string.IsNullOrEmpty(entry.Description)) NameText.ToolTip = entry.Description;

		EnableCheck.IsChecked = entry.Enabled;
		ModeCombo.SelectedIndex = (int)entry.Mode;
		IdxBox.Text = entry.Index.ToString("X4");
		DataBox.Text = HexOf(entry.ParamBytes);

		// Editors appear only for entries whose argument the user is meant to fill in.
		// Fixed sub-index entries (grouped) carry IndexBytes but need no editing — their
		// index shows read-only in the detail line instead.
		bool showEditors = entry.RequiresParam || (entry.ParamBytes is { Length: > 0 });
		ParamPanel.Visibility = showEditors ? Visibility.Visible : Visibility.Collapsed;

		Reset();
		RefreshBaseline();
	}

	// ---- write-back handlers ----

	/// <summary>Sets the enable toggle (used by a group's master checkbox).</summary>
	public void SetEnabled(bool enabled) => EnableCheck.IsChecked = enabled;

	private void EnableCheck_Checked(object sender, RoutedEventArgs e)
	{
		if (Entry != null) Entry.Enabled = true;
	}

	private void EnableCheck_Unchecked(object sender, RoutedEventArgs e)
	{
		if (Entry != null) Entry.Enabled = false;
	}

	private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (Entry != null && ModeCombo.SelectedIndex >= 0)
			Entry.Mode = (ValidationMode)ModeCombo.SelectedIndex;
	}

	private void IdxBox_LostFocus(object sender, RoutedEventArgs e)
	{
		byte[] hex = ParseHex(IdxBox.Text);
		if (Entry != null) Entry.Index = hex.Length >= 2 ? BitConverter.ToUInt16(hex, 0) : (ushort)0;
	}

	private void DataBox_LostFocus(object sender, RoutedEventArgs e)
	{
		if (Entry != null) Entry.ParamBytes = ParseHex(DataBox.Text);
	}

	// ---- verdict display ----

	public void Reset()
	{
		BadgeText.Text = "—";
		BadgeText.ClearValue(TextBlock.ForegroundProperty);
		Badge.Background = (Brush)FindResource("SkipBg");
		DetailText.Text = Entry != null ? $"req {Entry.HeaderHex()}" : "";
		DetailText.ClearValue(TextBlock.ToolTipProperty);
	}

	public void ShowScanning()
	{
		BadgeText.Text = "…";
		Badge.Background = (Brush)FindResource("SkipBg");
	}

	public void ShowResult(ScanResult result)
	{
		switch (result.Verdict)
		{
			case ScanVerdict.Pass:
				BadgeText.Text = "PASS";
				BadgeText.Foreground = (Brush)FindResource("PassFg");
				Badge.Background = (Brush)FindResource("PassBg");
				break;
			case ScanVerdict.Fail:
				BadgeText.Text = "FAIL";
				BadgeText.Foreground = (Brush)FindResource("FailFg");
				Badge.Background = (Brush)FindResource("FailBg");
				break;
			default:
				BadgeText.Text = "—";
				BadgeText.ClearValue(TextBlock.ForegroundProperty);
				Badge.Background = (Brush)FindResource("SkipBg");
				break;
		}

		string responseHex = Trim(result.ResponseHex, 60);
		DetailText.Text = result.Verdict == ScanVerdict.Pass
			? $"{result.Message} · {result.DurationMs:0} ms · resp {(string.IsNullOrEmpty(responseHex) ? "—" : responseHex)}"
			: $"{result.Message} · resp {(string.IsNullOrEmpty(responseHex) ? "—" : responseHex)}";
		DetailText.ToolTip = $"request  {result.RequestHex}\nresponse {result.ResponseHex}";
	}

	public void RefreshBaseline()
	{
		if (Entry?.Baseline is { Length: > 0 })
		{
			string when = Entry.BaselineCapturedUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "";
			BaselineText.Text = $"baseline {Entry.Baseline.Length}B {when}";
		}
		else
		{
			BaselineText.Text = "";
		}
	}

	// ---- helpers ----

	private static byte[] ParseHex(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return Array.Empty<byte>();

		char[] digits = text.Where(Uri.IsHexDigit).ToArray();
		int count = digits.Length - (digits.Length % 2);
		byte[] result = new byte[count / 2];
		for (int i = 0; i < result.Length; i++)
			result[i] = byte.Parse(new string(digits, i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
		return result;
	}

	private static string HexOf(byte[] bytes)
		=> bytes == null || bytes.Length == 0 ? "" : BitConverter.ToString(bytes).Replace('-', ' ');

	private static string Trim(string value, int max)
		=> string.IsNullOrEmpty(value) || value.Length <= max ? value : value.Substring(0, max) + "…";
}
