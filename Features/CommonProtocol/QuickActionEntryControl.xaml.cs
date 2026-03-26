namespace Base.UI.Pages;

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

/// <summary>
/// Code-behind for QuickActionEntryControl.xaml.
/// Manages data interaction, popup editing, and event forwarding.
/// </summary>
public partial class QuickActionEntryControl : UserControl
{
    private QuickActionEntryData data;

    // ?? Command edit popup (separated into its own control) ??
    private readonly CommandEditPopup commandEditPopup = new();

    /// <summary>Raised when user changes any data so the parent can persist.</summary>
    public event Action? DataChanged;

    /// <summary>Raised when the user clicks Send. The parent hooks this to actually dispatch commands.</summary>
    public event Action<QuickActionEntryData>? SendRequested;

    /// <summary>Raised when the user clicks Delete.</summary>
    public event Action<QuickActionEntryControl>? DeleteRequested;

    /// <summary>The underlying data model for this entry.</summary>
    public QuickActionEntryData EntryData => data;

    public QuickActionEntryControl(QuickActionEntryData entryData)
    {
        data = entryData ?? throw new ArgumentNullException(nameof(entryData));

        InitializeComponent();

        // ?? Populate UI from data ??
        NameBox.Text = data.Name;

        // ?? Wire XAML element events ??
        SendButton.Click += OnSendButtonClick;
        NameBox.LostFocus += OnNameBoxLostFocus;
        PreviewBlock.MouseLeftButtonUp += OnPreviewBlockClick;
        EditButton.Click += OnEditButtonClick;
        DeleteButton.Click += OnDeleteButtonClick;

        // ?? Wire command edit popup ??
        commandEditPopup.CommandsSaved += OnCommandsSaved;

        RefreshPreview();
    }

    // ??????????????????????????????
    //  Event handlers (UI ? Logic)
    // ??????????????????????????????

    private void OnSendButtonClick(object sender, RoutedEventArgs e)
    {
        SendRequested?.Invoke(data);
    }

    private void OnNameBoxLostFocus(object sender, RoutedEventArgs e)
    {
        data.Name = NameBox.Text;
        DataChanged?.Invoke();
    }

    private void OnPreviewBlockClick(object sender, MouseButtonEventArgs e)
    {
        OpenCommandPopup();
        e.Handled = true;
    }

    private void OnEditButtonClick(object sender, RoutedEventArgs e)
    {
        OpenCommandPopup();
    }

    private void OnDeleteButtonClick(object sender, RoutedEventArgs e)
    {
        DeleteRequested?.Invoke(this);
    }

    // ??????????????????????????????
    //  Command popup interaction
    // ??????????????????????????????

    private void OpenCommandPopup()
    {
        commandEditPopup.Open(PreviewBlock, data.Commands, data.IntervalMs);
    }

    private void OnCommandsSaved(List<string> commands, int intervalMs)
    {
        data.Commands.Clear();
        data.Commands.AddRange(commands);
        data.IntervalMs = intervalMs;
        RefreshPreview();
        DataChanged?.Invoke();
    }

    // ??????????????????????????????
    //  Preview text helpers
    // ??????????????????????????????

    private void RefreshPreview()
    {
        if (data.Commands.Count == 0)
        {
            PreviewBlock.Text = "(no commands)";
        }
        else if (data.Commands.Count == 1)
        {
            PreviewBlock.Text = data.Commands[0];
        }
        else
        {
            PreviewBlock.Text = $"[{data.Commands.Count} cmds] {data.Commands[0]} ...";
        }

        PreviewBlock.ToolTip = string.Join("\n", data.Commands);
    }

    // ??????????????????????????????
    //  Public API
    // ??????????????????????????????

    /// <summary>Replace data (e.g. after a re-load) and refresh visuals.</summary>
    public void SetData(QuickActionEntryData newData)
    {
        data = newData;
        NameBox.Text = data.Name;
        RefreshPreview();
    }
}
