namespace Base.UI.Pages;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

/// <summary>
/// Serializable data model for a single Quick Action entry.
/// Each entry stores a user-visible name, one or more commands (byte arrays, max 64 bytes each),
/// and an interval (ms) used when sending multiple commands sequentially.
/// </summary>
public sealed class QuickActionEntryData
{
    /// <summary>Unique id so we can track entries across edits.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>User-editable display name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Untitled";

    /// <summary>
    /// Commands stored as hex strings (e.g. "12 00 01 34 CA").
    /// We store hex strings instead of byte[] for human-readable JSON.
    /// </summary>
    [JsonPropertyName("commands")]
    public List<string> Commands { get; set; } = new();

    /// <summary>Interval in milliseconds between sending consecutive commands.</summary>
    [JsonPropertyName("intervalMs")]
    public int IntervalMs { get; set; } = 50;

    // ?? helpers ??

    public const int MaxCommandBytes = 64;

    /// <summary>Convert one hex-string command back to bytes. Returns null if invalid.</summary>
    public static byte[]? HexToBytes(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return Array.Empty<byte>();

        var clean = hex.AsSpan();
        int hexCount = 0;
        for (int i = 0; i < clean.Length; i++)
        {
            char c = clean[i];
            if (IsHex(c)) hexCount++;
            else if (!char.IsWhiteSpace(c)) return null;
        }

        if ((hexCount & 1) != 0) return null;

        int byteCount = hexCount >> 1;
        if (byteCount > MaxCommandBytes) return null;

        byte[] result = new byte[byteCount];
        int ri = 0, hi = -1;
        for (int i = 0; i < clean.Length; i++)
        {
            char c = clean[i];
            if (!IsHex(c)) continue;
            int val = HexVal(c);
            if (hi < 0) hi = val;
            else { result[ri++] = (byte)((hi << 4) | val); hi = -1; }
        }
        return result;
    }

    /// <summary>Convert bytes to a formatted hex string like "12 00 01 34 CA".</summary>
    public static string BytesToHex(byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0) return string.Empty;
        return string.Join(" ", bytes.Select(b => b.ToString("X2")));
    }

    private static bool IsHex(char c)
        => (uint)(c - '0') <= 9
        || (uint)((c | 32) - 'a') <= 5;

    private static int HexVal(char c)
        => (uint)(c - '0') <= 9
            ? c - '0'
            : ((c | 32) - 'a' + 10);
}
