namespace Base.UI.Pages;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>
/// Serializable data model for a Quick Action folder.
/// Holds a list of <see cref="QuickActionEntryData"/> grouped under a user-named folder.
/// </summary>
public sealed class QuickActionFolderData
{
    /// <summary>Unique id for this folder.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>User-editable display name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "New Folder";

    /// <summary>Whether the folder is expanded in the UI.</summary>
    [JsonPropertyName("isExpanded")]
    public bool IsExpanded { get; set; } = true;

    /// <summary>The quick action entries contained in this folder.</summary>
    [JsonPropertyName("entries")]
    public List<QuickActionEntryData> Entries { get; set; } = new();
}
