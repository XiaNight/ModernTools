namespace Base.Services
{
    using Base.Core;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// A single "recently visited page" entry surfaced on the Home page.
    /// </summary>
    public sealed class RecentPageRecord
    {
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Glyph { get; set; } = string.Empty;
        public DateTime LastOpenedUtc { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Tracks the pages the user has actually navigated to, most-recent first, and persists them
    /// via <see cref="LocalAppDataStore"/> so the list survives restarts. The Home page subscribes
    /// to <see cref="Changed"/> to refresh its "Recent Pages" panel.
    /// </summary>
    public static class RecentPagesService
    {
        private const string StoreKey = "Home.RecentPages";
        private const int MaxEntries = 8;

        // Placeholder text that PageInfoAttribute assigns when a page declares no description.
        // Treated as "no subtitle" so recent rows stay clean.
        private const string DefaultDescription = "There is no description for this page.";

        private static readonly object gate = new();
        private static List<RecentPageRecord> items;

        /// <summary>Raised (on the calling thread) whenever the recent list changes.</summary>
        public static event Action Changed;

        /// <summary>The current recent pages, most-recent first. Never null.</summary>
        public static IReadOnlyList<RecentPageRecord> Items
        {
            get
            {
                EnsureLoaded();
                lock (gate)
                    return items.ToList();
            }
        }

        /// <summary>
        /// Records a navigation to <paramref name="title"/>. Existing entries with the same title
        /// are moved to the top rather than duplicated.
        /// </summary>
        public static void Record(string title, string subtitle, string glyph)
        {
            if (string.IsNullOrWhiteSpace(title)) return;

            EnsureLoaded();

            lock (gate)
            {
                items.RemoveAll(r => string.Equals(r.Title, title, StringComparison.OrdinalIgnoreCase));

                items.Insert(0, new RecentPageRecord
                {
                    Title = title,
                    Subtitle = NormalizeSubtitle(subtitle),
                    Glyph = glyph ?? string.Empty,
                    LastOpenedUtc = DateTime.UtcNow
                });

                if (items.Count > MaxEntries)
                    items.RemoveRange(MaxEntries, items.Count - MaxEntries);

                Save();
            }

            Changed?.Invoke();
        }

        /// <summary>Clears the recent list.</summary>
        public static void Clear()
        {
            lock (gate)
            {
                items = new List<RecentPageRecord>();
                Save();
            }
            Changed?.Invoke();
        }

        private static string NormalizeSubtitle(string subtitle)
        {
            if (string.IsNullOrWhiteSpace(subtitle)) return string.Empty;
            return string.Equals(subtitle, DefaultDescription, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : subtitle;
        }

        private static void EnsureLoaded()
        {
            lock (gate)
            {
                if (items != null) return;

                if (LocalAppDataStore.IsInitialised)
                {
                    try
                    {
                        items = LocalAppDataStore.Instance.Get<List<RecentPageRecord>>(StoreKey)
                                ?? new List<RecentPageRecord>();
                    }
                    catch
                    {
                        items = new List<RecentPageRecord>();
                    }
                }
                else
                {
                    items = new List<RecentPageRecord>();
                }
            }
        }

        private static void Save()
        {
            if (!LocalAppDataStore.IsInitialised) return;
            try
            {
                LocalAppDataStore.Instance.Set(StoreKey, items);
            }
            catch
            {
                // Persisting recent pages is best-effort; never let it break navigation.
            }
        }
    }
}
