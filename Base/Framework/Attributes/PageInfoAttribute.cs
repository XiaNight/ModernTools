namespace Base.Core
{
    /// <summary>
    /// Provides navigation metadata for a <see cref="Base.Pages.PageBase"/> subclass so that the
    /// navigation tab can be built without instantiating the page class at startup.
    /// Apply this attribute to every concrete page class to enable lazy initialization.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class PageInfoAttribute : Attribute
    {
        /// <summary>Name shown in the navigation sidebar.</summary>
        public string PageName { get; }

        /// <summary>
        /// Segoe MDL2 / Fluent icon glyph (default matches <see cref="Base.Pages.PageBase.Glyph"/>).<br/>
        /// Icon Library: <a href="https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-ui-symbol-font">Segoe Fluent Icons</a>
        /// </summary>
        public string Glyph { get; init; } = "\uE878";

        /// <summary>Optional secondary glyph overlaid on the icon.</summary>
        public string SecondaryGlyph { get; init; } = "";

        /// <summary>Short abbreviation shown in compact navigation mode.</summary>
        public string ShortName { get; init; } = "";

        /// <summary>Tooltip / description text.</summary>
        public string Description { get; init; } = "There is no description for this page.";

        /// <summary>
        /// Controls the position of the tab in the navigation bar.
        /// Negative values hide the tab. Default is <see cref="int.MaxValue"/> (append at end).
        /// </summary>
        public int NavOrder { get; init; } = int.MaxValue;

        /// <summary>
        /// Whether the tab appears in the top (Front) or bottom (Back) section of the sidebar.
        /// Uses 0 for Front and 1 for Back, mirroring <see cref="Base.Pages.PageBase.NavigationAlignment"/>.
        /// </summary>
        public int NavAlignment { get; init; } = 0; // 0 = Front, 1 = Back

        /// <summary>Whether the device-selection control is shown when this page is active.</summary>
        public bool ShowDeviceSelection { get; init; } = true;

        /// <summary>Optional sub-path for nested navigation grouping (e.g. ["Keyboard", "Hall Effect"]).</summary>
        public string[] Path { get; init; } = [];

        public PageInfoAttribute(string pageName)
        {
            PageName = pageName;
        }
    }
}
