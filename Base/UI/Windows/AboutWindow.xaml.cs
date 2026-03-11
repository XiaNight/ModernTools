using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Base.Components
{
    /// <summary>
    /// Interaction logic for AboutWindow.xaml
    /// </summary>
    public partial class AboutWindow : Window
    {
        private bool _isClosing;   // prevents double-close

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[] phiconLarge, IntPtr[] phiconSmall, uint nIcons);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        public AboutWindow()
        {
            InitializeComponent();

            AppNameText.Text = MainWindow.appName;
            VersionText.Text = MainWindow.version;
            ToolBaseVersionText.Text = MainWindow.toolBaseVersion;

            Deactivated += OnDeactivated;
            Closing += OnClosing;
        }

        public static void Show(Window owner)
        {
            var w = new AboutWindow { Owner = owner };
            w.ShowDialog();
        }

        private void OnDeactivated(object? sender, EventArgs e)
        {
            if (_isClosing) return;            // already closing via X or programmatically
            _isClosing = true;

            // Close on the next dispatcher tick to avoid "closing while deactivating" glitches
            Dispatcher.BeginInvoke(new Action(Close), DispatcherPriority.Background);
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            _isClosing = true;                 // user clicked X, Alt+F4, etc.
            Deactivated -= OnDeactivated;      // no more deactivation handling
        }
    }
}
