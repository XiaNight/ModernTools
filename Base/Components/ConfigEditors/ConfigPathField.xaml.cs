using Base.Core;
using Microsoft.Win32;
using ModernWpf.Controls.Primitives;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Base.Components;

/// <summary>
/// Text-box editor with a <c>Browse…</c> button for string members that hold a filesystem path.
/// <see cref="ConfigType.File"/> opens a single-selection file picker and <see cref="ConfigType.Folder"/>
/// a single-selection folder picker; the chosen full path is written back to the member. The path may
/// also be typed directly and is validated the same way as a plain string field (length / regex
/// bounds from the attribute).
/// </summary>
public partial class ConfigPathField : UserControl, IConfigEditor
{
	private ConfigItem _item;
	private bool _pickFolder;
	private Brush _defaultBorder;

	public ConfigPathField()
	{
		InitializeComponent();
		_defaultBorder = Input.BorderBrush;
	}

	public void Bind(ConfigItem item)
	{
		_item = item;
		_pickFolder = item.Attr.Type == ConfigType.Folder;

		if (!string.IsNullOrEmpty(item.Attr.Placeholder))
			ControlHelper.SetPlaceholderText(Input, item.Attr.Placeholder);

		Input.Text = CurrentPath();
		Input.LostFocus += (s, e) => Commit();
		Input.KeyDown += (s, e) =>
		{
			if (e.Key == Key.Enter)
			{
				Commit();
				e.Handled = true;
			}
		};
	}

	private string CurrentPath() => ConfigEditorUtil.FormatValue(_item.Get());

	private void Browse_Click(object sender, RoutedEventArgs e)
	{
		string picked = _pickFolder ? PickFolder() : PickFile();
		if (picked == null) return;

		Input.Text = picked;
		Commit();
	}

	private string PickFile()
	{
		OpenFileDialog dialog = new()
		{
			Multiselect = false,
			CheckFileExists = true,
			Title = _item.Label,
		};
		SeedInitialDirectory(dir => dialog.InitialDirectory = dir);
		return dialog.ShowDialog() == true ? dialog.FileName : null;
	}

	private string PickFolder()
	{
		OpenFolderDialog dialog = new()
		{
			Multiselect = false,
			Title = _item.Label,
		};
		SeedInitialDirectory(dir => dialog.InitialDirectory = dir);
		return dialog.ShowDialog() == true ? dialog.FolderName : null;
	}

	/// <summary>
	/// Points the picker at the current value's directory when it resolves to a real location, so
	/// re-browsing starts where the user left off. Malformed paths are ignored.
	/// </summary>
	private void SeedInitialDirectory(Action<string> setDirectory)
	{
		string current = CurrentPath();
		if (string.IsNullOrWhiteSpace(current)) return;

		try
		{
			string dir = Directory.Exists(current) ? current : Path.GetDirectoryName(current);
			if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
				setDirectory(dir);
		}
		catch
		{
			// Ignore malformed paths — just open the picker at its default location.
		}
	}

	private void Commit()
	{
		bool ok = ConfigEditorUtil.TryParseText(Input.Text, _item.UnderlyingType, _item.Attr, out object value, out string err);

		if (ok)
		{
			_item.Set(value);
			// Read back so any custom setter normalisation is reflected.
			Input.Text = CurrentPath();
			ErrorText.Visibility = Visibility.Collapsed;
			Input.BorderBrush = _defaultBorder;
		}
		else
		{
			ErrorText.Text = err;
			ErrorText.Visibility = Visibility.Visible;
			Input.BorderBrush = Brushes.IndianRed;
		}
	}
}
