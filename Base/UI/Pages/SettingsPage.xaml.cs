using Base.Components;
using Base.Core;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Base.Pages;

/// <summary>
/// The application's Settings page. Unlike the per-page <see cref="ConfigDialog"/> (which discovers
/// <see cref="ConfigAttribute"/> members on the current page on demand), this page renders the
/// app / feature-wide <see cref="SettingAttribute"/> members that were catalogued at startup by
/// <see cref="SettingRegistry"/>. The catalogue is built once during startup; the editor controls are
/// materialised lazily here, only when the page is opened, and rebuilt on each open so the values and
/// visibility conditions reflect the live state.
/// </summary>
[PageInfo("Settings",
	Glyph = "\uE713",       // Settings gear (Segoe Fluent Icons).
	Description = "Application and feature-wide settings.",
	NavAlignment = 1,        // Back — sits at the bottom of the navigation sidebar.
	ShowDeviceSelection = false)]
public partial class SettingsPage : PageBase
{
	public SettingsPage()
	{
		InitializeComponent();
	}

	protected override void OnEnable()
	{
		base.OnEnable();
		BuildSettingsUi();
	}

	protected override void OnDisable()
	{
		base.OnDisable();
		// Free the materialised editors while the page is hidden; they respawn on next open.
		SettingsContainer.Children.Clear();
	}

	/// <summary>Spawns the editor rows from the startup catalogue, grouped into sections.</summary>
	private void BuildSettingsUi()
	{
		SettingsContainer.Children.Clear();

		var sections = SettingRegistry.Instance.GetVisibleSections();

		EmptyPlaceholder.Visibility = sections.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

		foreach (var section in sections)
		{
			SettingsContainer.Children.Add(CreateSectionHeader(section.Key));

			foreach (SettingRegistry.SettingEntry entry in section)
				SettingsContainer.Children.Add(ConfigEditorFactory.BuildRow(entry.ToConfigItem()));
		}
	}

	/// <summary>A section title with a hairline rule, themed via dynamic resources.</summary>
	private static FrameworkElement CreateSectionHeader(string text)
	{
		StackPanel panel = new() { Margin = new Thickness(0, 20, 0, 6) };

		TextBlock title = new()
		{
			Text = text,
			FontSize = 16,
			FontWeight = FontWeights.SemiBold,
		};
		title.SetResourceReference(TextBlock.ForegroundProperty, "SystemControlForegroundBaseHighBrush");
		panel.Children.Add(title);

		Border rule = new() { Height = 1, Margin = new Thickness(0, 6, 0, 0) };
		rule.SetResourceReference(Border.BackgroundProperty, "SystemControlForegroundBaseLowBrush");
		panel.Children.Add(rule);

		return panel;
	}
}
