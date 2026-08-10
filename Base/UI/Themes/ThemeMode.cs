using System.ComponentModel;

namespace Base.UI.Themes
{
	/// <summary>
	/// The user-selectable appearance modes offered on the Settings page. Each maps to a ModernWpf
	/// <c>ApplicationTheme</c> plus an accent colour (see <see cref="ThemeController"/>). The
	/// <see cref="DescriptionAttribute"/> on a member is used as its label in the Settings dropdown.
	/// </summary>
	public enum ThemeMode
	{
		/// <summary>Light theme with the ROG red accent.</summary>
		Light = 0,

		/// <summary>Dark theme with the ROG red accent.</summary>
		Dark = 1,

		/// <summary>Dark theme with the anniversary gold accent.</summary>
		[Description("ROG 20th")]
		BlackGold = 2,

		/// <summary>
		/// Follow Windows: light or dark to match the PC's app theme, and the PC's accent colour.
		/// Tracks live changes to the Windows theme / accent while selected.
		/// </summary>
		[Description("System Default")]
		Auto = 3,
	}
}
