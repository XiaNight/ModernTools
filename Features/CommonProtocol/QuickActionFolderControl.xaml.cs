namespace Base.UI.Pages;

using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

/// <summary>
/// A collapsible folder control that groups <see cref="QuickActionEntryControl"/> items.
/// Supports expand/collapse, per-entry send, export to JSON, and entry-level delete.
/// </summary>
public partial class QuickActionFolderControl : UserControl
{
    private QuickActionFolderData data;

    /// <summary>Raised when any data in the folder (name, entries) changes so the parent can persist.</summary>
    public event Action? DataChanged;

    /// <summary>Raised when the user requests deletion of the whole folder.</summary>
    public event Action<QuickActionFolderControl>? DeleteRequested;

    /// <summary>Raised when the user clicks Send on an entry inside this folder.</summary>
    public event Action<QuickActionEntryData>? SendRequested;

    /// <summary>The underlying data model for this folder.</summary>
    public QuickActionFolderData FolderData => data;

    public QuickActionFolderControl(QuickActionFolderData folderData)
    {
        data = folderData ?? throw new ArgumentNullException(nameof(folderData));

        InitializeComponent();

        FolderNameBox.Text = data.Name;
        FolderNameBox.LostFocus += OnFolderNameLostFocus;

        ExpandButton.Click += (_, _) => SetExpanded(!data.IsExpanded);
        ExportButton.Click += (_, _) => ExportFolder();
        AddEntryButton.Click += (_, _) => AddNewEntry();
        DeleteFolderButton.Click += (_, _) => DeleteRequested?.Invoke(this);

        // Populate existing entries
        foreach (var entry in data.Entries)
            FolderEntriesPanel.Children.Add(CreateEntryControl(entry));

        UpdateExpandedState();
    }

    // ─────────────────────────────────
    //  Public API
    // ─────────────────────────────────

    /// <summary>
    /// Show or hide entries that match <paramref name="searchText"/>.
    /// When searching the folder auto-expands if it contains any match.
    /// The folder itself becomes <see cref="Visibility.Collapsed"/> when no entries match.
    /// </summary>
    public void Filter(string searchText)
    {
        bool isSearching = !string.IsNullOrWhiteSpace(searchText);
        bool anyVisible = false;

        foreach (QuickActionEntryControl ctrl in FolderEntriesPanel.Children)
        {
            bool matches = !isSearching
                || ctrl.EntryData.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                || ctrl.EntryData.Commands.Any(c => c.Contains(searchText, StringComparison.OrdinalIgnoreCase));

            ctrl.Visibility = matches ? Visibility.Visible : Visibility.Collapsed;
            if (matches) anyVisible = true;
        }

        // Auto-expand to reveal search results
        if (isSearching && anyVisible && !data.IsExpanded)
            SetExpanded(true);

        Visibility = (!isSearching || anyVisible) ? Visibility.Visible : Visibility.Collapsed;
    }

    // ─────────────────────────────────
    //  Event handlers
    // ─────────────────────────────────

    private void OnFolderNameLostFocus(object sender, RoutedEventArgs e)
    {
        data.Name = FolderNameBox.Text;
        DataChanged?.Invoke();
    }

    // ─────────────────────────────────
    //  Expand / Collapse
    // ─────────────────────────────────

    private void SetExpanded(bool expanded)
    {
        data.IsExpanded = expanded;
        UpdateExpandedState();
        DataChanged?.Invoke();
    }

    private void UpdateExpandedState()
    {
        FolderEntriesPanel.Visibility = data.IsExpanded ? Visibility.Visible : Visibility.Collapsed;
        // ChevronDown (&#xE70E;) when expanded, ChevronRight (&#xE70D;) when collapsed
        ExpandChevron.Glyph = data.IsExpanded ? "\uE70E" : "\uE70D";
    }

    // ─────────────────────────────────
    //  Entry management
    // ─────────────────────────────────

    private void AddNewEntry()
    {
        var newEntry = new QuickActionEntryData();
        data.Entries.Add(newEntry);
        FolderEntriesPanel.Children.Add(CreateEntryControl(newEntry));
        SetExpanded(true);
        DataChanged?.Invoke();
    }

    private QuickActionEntryControl CreateEntryControl(QuickActionEntryData entryData)
    {
        var ctrl = new QuickActionEntryControl(entryData);
        ctrl.DataChanged += () => DataChanged?.Invoke();
        ctrl.SendRequested += entry => SendRequested?.Invoke(entry);
        ctrl.DeleteRequested += OnDeleteEntry;
        return ctrl;
    }

    private void OnDeleteEntry(QuickActionEntryControl control)
    {
        var result = MessageBox.Show(
            $"Delete quick action \"{control.EntryData.Name}\"?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        data.Entries.Remove(control.EntryData);
        FolderEntriesPanel.Children.Remove(control);
        DataChanged?.Invoke();
    }

    // ─────────────────────────────────
    //  Export
    // ─────────────────────────────────

    private void ExportFolder()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Export Folder",
            Filter = "JSON files (*.json)|*.json",
            FileName = data.Name
        };

        if (dlg.ShowDialog() != true) return;

        try
        {
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dlg.FileName, json);
            MessageBox.Show("Folder exported successfully.", "Export",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to export folder:\n{ex.Message}", "Export Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
