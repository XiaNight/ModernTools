using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Base.Components
{
	public class DragNDropField
	{
		public TextBlock title;
		private string path;
		public Grid grid;
		public TextBox inputField;
		public Button browseButton;
		private UIElement overlay;
		public event Action<string> OnPathChanged;
		public string Path => path;

		public DragNDropField(Panel dragArea, string title, string path = "")
		{
			this.path = path;

			grid = new Grid();
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

			this.title = new TextBlock { Text = title, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
			inputField = new TextBox { Text = path, Padding = new Thickness(5), AllowDrop = true };
			inputField.TextChanged += (s, e) => { this.path = inputField.Text; OnPathChanged?.Invoke(this.path); };

			browseButton = new Button { Content = "Browse", Margin = new Thickness(10, 0, 10, 0), Padding = new Thickness(10, 5, 10, 5) };
			browseButton.Click += OnBrowseClick;

			Grid.SetColumn(this.title, 0);
			Grid.SetColumn(inputField, 1);
			Grid.SetColumn(browseButton, 2);

			grid.Children.Add(this.title);
			grid.Children.Add(inputField);
			grid.Children.Add(browseButton);

			dragArea ??= grid;
			dragArea.PreviewDragEnter += OnPreviewDragEnter;
			dragArea.PreviewDrop += OnPreviewDrop;
			dragArea.PreviewDragLeave += (s, e) => overlay.Visibility = Visibility.Collapsed;

			CreateFileDropOverlay(dragArea);
		}

		private void CreateFileDropOverlay(Panel dragArea)
		{
			overlay = new Image
			{
				Source = new BitmapImage(new Uri("pack://application:,,,/Base;component/Assets/DropFile.png")),
				Opacity = 0.3,
				Visibility = Visibility.Collapsed,
				Stretch = Stretch.Fill,
				IsHitTestVisible = false
			};
			Panel.SetZIndex(overlay, int.MaxValue);
			dragArea.Children.Add(overlay);
		}

		private void OnBrowseClick(object obj, RoutedEventArgs e)
		{
			string safePath = string.IsNullOrWhiteSpace(path) ? Environment.CurrentDirectory : System.IO.Path.GetDirectoryName(path);
			var dlg = new OpenFileDialog { Filter = "Batch Files (*.bat)|*.bat", InitialDirectory = safePath, Title = $"Select {title} File" };
			if (dlg.ShowDialog() == true)
			{
				path = dlg.FileName;
				inputField.Text = path;
				OnPathChanged?.Invoke(path);
			}
		}

		public void OnPreviewDragEnter(object sender, DragEventArgs e)
		{
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				overlay.Visibility = Visibility.Visible;
				var files = (string[])e.Data.GetData(DataFormats.FileDrop);
				if (files.Length > 0 && files[0].EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
				{
					e.Effects = DragDropEffects.Copy;
					e.Handled = true;
					return;
				}
			}
			e.Effects = DragDropEffects.None;
			e.Handled = true;
		}

		public void OnPreviewDrop(object sender, DragEventArgs e)
		{
			overlay.Visibility = Visibility.Collapsed;
			if (e.Data.GetDataPresent(DataFormats.FileDrop))
			{
				var files = (string[])e.Data.GetData(DataFormats.FileDrop);
				if (files.Length > 0 && files[0].EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
				{
					path = files[0];
					inputField.Text = path;
					OnPathChanged?.Invoke(path);
				}
				else
				{
					MessageBox.Show("Please drop a valid .bat file.");
				}
			}
			e.Handled = true;
		}
	}
}
