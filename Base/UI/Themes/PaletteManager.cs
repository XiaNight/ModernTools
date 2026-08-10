using System;
using System.Windows;
using ModernWpf;

namespace Base.UI.Themes
{
    /// <summary>
    /// Swaps the active colour palette (<c>Palette.Light.xaml</c> / <c>Palette.Dark.xaml</c>) that
    /// is merged, via <c>Brushes.xaml</c>, into the application resource tree.
    ///
    /// The palette only defines <see cref="System.Windows.Media.Color"/> keys; the brushes in
    /// <c>Brushes.xaml</c> reference those keys with <c>DynamicResource</c>, so replacing the
    /// palette dictionary at runtime makes every dependent brush refresh automatically.
    /// </summary>
    public static class PaletteManager
    {
        private static readonly Uri LightPalette =
            new("pack://application:,,,/Base;component/UI/Themes/Palette.Light.xaml", UriKind.Absolute);

        private static readonly Uri DarkPalette =
            new("pack://application:,,,/Base;component/UI/Themes/Palette.Dark.xaml", UriKind.Absolute);

        /// <summary>
        /// Applies the palette that matches <paramref name="theme"/>. <see cref="ApplicationTheme.Default"/>
        /// (or a null) resolves to the light palette. Safe to call before the main window exists, as it
        /// operates on <see cref="Application.Current"/>'s resources.
        /// </summary>
        public static void Apply(ApplicationTheme? theme)
        {
            var app = Application.Current;
            if (app == null) return;

            var target = theme == ApplicationTheme.Dark ? DarkPalette : LightPalette;

            if (TryFindPalette(app.Resources, out var owner, out var index))
            {
                // Skip if the correct palette is already active, otherwise swap in place so the
                // DynamicResource consumers are invalidated and re-evaluate their colours.
                var current = owner.MergedDictionaries[index].Source;
                if (current != null && SameFile(current, target)) return;

                owner.MergedDictionaries[index] = new ResourceDictionary { Source = target };
            }
        }

        /// <summary>
        /// Depth-first search for the merged dictionary whose source is one of the palette files,
        /// returning the owning dictionary and the index within its MergedDictionaries collection.
        /// </summary>
        private static bool TryFindPalette(ResourceDictionary root, out ResourceDictionary owner, out int index)
        {
            for (int i = 0; i < root.MergedDictionaries.Count; i++)
            {
                var dict = root.MergedDictionaries[i];
                if (IsPalette(dict.Source))
                {
                    owner = root;
                    index = i;
                    return true;
                }

                if (TryFindPalette(dict, out owner, out index))
                    return true;
            }

            owner = null!;
            index = -1;
            return false;
        }

        private static bool IsPalette(Uri? source)
        {
            if (source == null) return false;
            var s = source.OriginalString;
            return s.EndsWith("Palette.Light.xaml", StringComparison.OrdinalIgnoreCase)
                || s.EndsWith("Palette.Dark.xaml", StringComparison.OrdinalIgnoreCase);
        }

        private static bool SameFile(Uri a, Uri b) =>
            a.OriginalString.EndsWith(FileName(b), StringComparison.OrdinalIgnoreCase);

        private static string FileName(Uri u)
        {
            var s = u.OriginalString;
            var slash = s.LastIndexOf('/');
            return slash >= 0 ? s[(slash + 1)..] : s;
        }
    }
}
