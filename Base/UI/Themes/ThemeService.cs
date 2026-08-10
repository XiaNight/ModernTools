using Base.Core;

namespace Base.UI.Themes
{
	/// <summary>
	/// Owns the app-wide appearance setting. As a <see cref="WpfBehaviourSingleton{T}"/> it is
	/// instantiated at startup and its <see cref="Mode"/> field is discovered by the settings system,
	/// so a "Theme" dropdown appears on the Settings page under the <c>Appearance</c> section. Changing
	/// it applies immediately (theme + accent), refreshes the window frame, notifies every behaviour,
	/// and persists the choice.
	/// </summary>
	public sealed class ThemeService : WpfBehaviourSingleton<ThemeService>
	{
		/// <summary>
		/// The selected appearance. Auto-persisted (key <c>ThemeService.Mode</c>) and rendered as a
		/// combo box on the Settings page. <see cref="OnModeChanged"/> runs whenever the user picks a
		/// different value.
		/// </summary>
		[Setting(
			Section = "Appearance",
			Name = "Theme",
			Order = 0,
			Changed = nameof(OnModeChanged),
			Hint = "Light and Dark use the ROG red accent. Black-Gold pairs the anniversary gold "
				 + "accent with a dark theme. Auto follows your Windows light/dark mode and accent colour.")]
		public ThemeMode Mode = ThemeMode.Light;

		public override void Start()
		{
			base.Start();
			// The catalogue has already loaded the persisted Mode by now; App.OnStartup applied it
			// before the window existed, so re-apply here to refresh the window frame and notify any
			// behaviours that came up early.
			ApplyAndNotify();
		}

		/// <summary>Sets the mode programmatically (e.g. from the title-bar theme toggle) and applies it.</summary>
		public void SetMode(ThemeMode mode)
		{
			Mode = mode;
			ApplyAndNotify();
		}

		/// <summary>Invoked by the settings editor after the user changes <see cref="Mode"/>.</summary>
		private void OnModeChanged() => ApplyAndNotify();

		private void ApplyAndNotify()
		{
			ThemeController.Apply(Mode);

			// Persist immediately (in addition to the settings save-on-quit) so the choice survives
			// even if the app is not closed cleanly.
			if (LocalAppDataStore.IsInitialised)
				LocalAppDataStore.Instance.Set(ThemeController.PersistKey, Mode);

			// Repaint the window frame and tell every behaviour to refresh its themed visuals.
			Main.OnThemeChangedExternally();
		}
	}
}
