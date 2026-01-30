using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Base.Components
{
    /// <summary>
    /// Interaction logic for Console.xaml
    /// </summary>
    public partial class Console : UserControl
    {
        const int MaxChars = 200_000; // hard cap
        const int TrimTo = 150_000;  // after trimming, keep ~150k newest chars

        public string defaultPath = string.Empty;
        public delegate string SavePathHandlerDelegate();
        public SavePathHandlerDelegate SavePathHandler { get; set; } = null;

        public Console()
        {
            InitializeComponent();
        }

        public void Clear()
        {
            ConsoleTextBox.Clear();
        }

        public void Copy()
        {
            Clipboard.SetText(ConsoleTextBox.Text);
        }

        public void WriteLine(string text)
        {
            // Ensure we're on UI thread if you call from workers
            if (!ConsoleTextBox.Dispatcher.CheckAccess())
            {
                ConsoleTextBox.Dispatcher.BeginInvoke(new Action(() => WriteLine(text)));
                return;
            }

            // Append new line
            ConsoleTextBox.AppendText(text);
            ConsoleTextBox.AppendText(Environment.NewLine);

            // Trim the oldest text if we exceed the cap
            if (ConsoleTextBox.Text.Length > MaxChars)
            {
                int removeCount = ConsoleTextBox.Text.Length - TrimTo;
                ConsoleTextBox.Select(0, removeCount);
                ConsoleTextBox.SelectedText = string.Empty; // deletes selected range
            }

            ConsoleTextBox.ScrollToEnd();
        }

        public void Save(string path = null)
        {
            if (string.IsNullOrEmpty(path))
            {
                if (SavePathHandler != null)
                    path = SavePathHandler();
                else if (!string.IsNullOrEmpty(defaultPath))
                    path = defaultPath;
                else
                {
                    var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                    {
                        Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                        DefaultExt = ".txt"
                    };
                    if (saveFileDialog.ShowDialog() != true) return;
                    path = saveFileDialog.FileName;
                }
            }
            if (Directory.Exists(Path.GetDirectoryName(path)) == false)
            {
                MessageBox.Show("The specified directory does not exist.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            File.WriteAllText(path, ConsoleTextBox.Text);
        }

        public void AddUtil(FrameworkElement element)
        {
            Utils.Children.Add(element);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e) => Save(defaultPath);
        private void ClearButton_Click(object sender, RoutedEventArgs e) => Clear();
        private void CopyButton_Click(object sender, RoutedEventArgs e) => Copy();
    }
}
