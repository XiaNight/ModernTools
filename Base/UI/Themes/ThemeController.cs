using Base.Core;
using ModernWpf;
using System.Windows.Media;

namespace Base.UI.Themes
{
	/// <summary>
	/// Translates a <see cref="ThemeMode"/> into concrete ModernWpf state (application theme + accent
	/// colour) and keeps the custom palette in sync. This is deliberately static and window-free so it
	/// can run at the very start of <c>App.OnStartup</c> — before the main window exists — as well as
	/// from <see cref="ThemeService"/> at runtime. Window-frame refresh and behaviour notification are
	/// the caller's job (they need the <see cref="MainWindow"/>).
	/// </summary>
	public static class ThemeController
	{
		/// <summary>ROG brand accent (used by the Light and Dark modes).</summary>
		private static readonly Color RogRed = Color.FromRgb(0xFF, 0x19, 0x29);

		/// <summary>Anniversary gold accent (used by the Black-Gold mode).</summary>
		private static readonly Color AnniversaryGold = Color.FromRgb(0xEE, 0xBE, 0x38);

		/// <summary>
		/// Persistence key for the chosen mode. Matches the auto-persist key the
		/// <see cref="Base.Core.SettingAttribute"/> on <see cref="ThemeService.Mode"/> produces
		/// (<c>"{OwnerType}.{Member}"</c>), so startup and the Settings page read/write the same slot.
		/// </summary>
		public const string PersistKey = "ThemeService.Mode";

		/// <summary>Legacy key written by the old theme toggle (a ModernWpf <c>ApplicationTheme</c>).</summary>
		private const string LegacyThemeKey = "Theme";

		/// <summary>
		/// Reads the saved mode. Falls back to the legacy <c>"Theme"</c> key (dark/light) for users
		/// upgrading from before the theme setting existed, then to <see cref="ThemeMode.Light"/>.
		/// </summary>
		public static ThemeMode LoadSaved()
		{
			if (!LocalAppDataStore.IsInitialised)
				return ThemeMode.Light;

			if (LocalAppDataStore.Instance.TryGet(PersistKey, out ThemeMode mode))
				return mode;

			if (LocalAppDataStore.Instance.TryGet(LegacyThemeKey, out ApplicationTheme legacy))
				return legacy == ApplicationTheme.Dark ? ThemeMode.Dark : ThemeMode.Light;

			return ThemeMode.Light;
		}

		/// <summary>
		/// Applies the mode to ModernWpf (theme + accent) and refreshes the custom palette. Does not
		/// touch the window frame or broadcast to behaviours — see <see cref="MainWindow"/>.
		/// </summary>
		public static void Apply(ThemeMode mode)
		{
			switch (mode)
			{
				case ThemeMode.Light:
					ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;
					ThemeManager.Current.AccentColor = RogRed;
					break;

				case ThemeMode.Dark:
					ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
					ThemeManager.Current.AccentColor = RogRed;
					break;

				case ThemeMode.BlackGold:
					ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
					ThemeManager.Current.AccentColor = AnniversaryGold;
					break;

				case ThemeMode.Auto:
					// null = follow Windows. ModernWpf then tracks the OS app theme and accent colour,
					// updating (and raising ActualApplicationThemeChanged / ActualAccentColorChanged)
					// whenever the user changes them in Windows settings.
					ThemeManager.Current.ApplicationTheme = null;
					ThemeManager.Current.AccentColor = null;
					break;
			}

			// Keep the custom colour palette aligned with the resolved light/dark theme.
			PaletteManager.Apply(ThemeManager.Current.ActualApplicationTheme);
		}
	}
}
