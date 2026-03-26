namespace Base.UI.Pages;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

/// <summary>
/// A reusable popup control for editing a list of hex-string commands
/// and the interval (ms) between them.
/// Owns a <see cref="System.Windows.Controls.Primitives.Popup"/> that can be
/// opened against an arbitrary placement target.
/// </summary>
public partial class CommandEditPopup : UserControl
{
    /// <summary>
    /// Raised when the user clicks Save and all lines are valid.
    /// Carries the normalised command list and the interval in milliseconds.
    /// </summary>
    public event Action<List<string>, int>? CommandsSaved;

    public CommandEditPopup()
    {
        InitializeComponent();
        SaveButton.Click += OnSaveButtonClick;
        IntervalBox.PreviewTextInput += OnIntervalPreviewTextInput;
    }

    // ??????????????????????????????
    //  Public API
    // ??????????????????????????????

    /// <summary>
    /// Open the popup, pre-populated with the given commands and interval,
    /// positioned relative to <paramref name="placementTarget"/>.
    /// </summary>
    public void Open(UIElement placementTarget, IEnumerable<string> commands, int intervalMs)
    {
        PopupRoot.PlacementTarget = placementTarget;
        CommandEditBox.Text = string.Join(Environment.NewLine, commands);
        IntervalBox.Text = intervalMs.ToString();
        PopupRoot.IsOpen = true;
        CommandEditBox.Focus();
    }

    /// <summary>Close the popup without saving.</summary>
    public void Close()
    {
        PopupRoot.IsOpen = false;
    }

    /// <summary>Whether the popup is currently shown.</summary>
    public bool IsPopupOpen => PopupRoot.IsOpen;

    // ??????????????????????????????
    //  Event handlers
    // ??????????????????????????????

    private void OnIntervalPreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
    {
        foreach (char c in e.Text)
        {
            if (!char.IsDigit(c))
            {
                e.Handled = true;
                break;
            }
        }
    }

    private void OnSaveButtonClick(object sender, RoutedEventArgs e)
    {
        // ?? Parse interval ??
        if (!int.TryParse(IntervalBox.Text, out int intervalMs) || intervalMs < 0)
        {
            MessageBox.Show(
                "Interval must be a non-negative whole number of milliseconds.",
                "Invalid Interval",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // ?? Parse and validate command lines ??
        var lines = CommandEditBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        foreach (var line in lines)
        {
            if (QuickActionEntryData.HexToBytes(line) is null)
            {
                MessageBox.Show(
                    "One or more lines contain invalid hex data.\n" +
                    "Each line should be space-separated hex bytes (max 64 bytes), e.g.:\n" +
                    "12 00 01 34 CA",
                    "Invalid Command",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        // ?? Normalise: parse ? reformat so spacing is consistent ??
        var normalised = new List<string>(lines.Count);
        foreach (var line in lines)
        {
            var bytes = QuickActionEntryData.HexToBytes(line);
            if (bytes is not null && bytes.Length > 0)
                normalised.Add(QuickActionEntryData.BytesToHex(bytes));
        }

        PopupRoot.IsOpen = false;
        CommandsSaved?.Invoke(normalised, intervalMs);
    }
}
