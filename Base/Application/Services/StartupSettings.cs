using Base.Core;
using System.ComponentModel;

namespace Base.Services
{
	/// <summary>Where the app navigates to on launch.</summary>
	public enum LandingPage
	{
		/// <summary>Always open the Home dashboard.</summary>
		Home = 0,

		/// <summary>Reopen the page that was last visited in the previous session.</summary>
		[Description("Last visited")]
		Last = 1,
	}

	/// <summary>
	/// App-wide startup preferences. As a <see cref="WpfBehaviourSingleton{T}"/> it is instantiated at
	/// startup and its <see cref="Landing"/> field is discovered by the settings system, so a
	/// "Landing page" dropdown appears on the Settings page under the <c>Startup</c> section. The value
	/// is read once during startup navigation (see <c>MainWindow</c>).
	/// </summary>
	public sealed class StartupSettings : WpfBehaviourSingleton<StartupSettings>
	{
		/// <summary>
		/// Which page opens on launch. Auto-persisted (key <c>StartupSettings.Landing</c>).
		/// </summary>
		[Setting(
			Section = "Startup",
			Name = "Landing page",
			Order = 0,
			Hint = "Which page to open when the app starts: the Home dashboard, or the page you last had open.")]
		public LandingPage Landing = LandingPage.Home;
	}
}
