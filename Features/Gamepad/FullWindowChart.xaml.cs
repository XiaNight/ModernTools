using System;
using System.Linq;
using System.Windows;
using System.Windows.Forms;

namespace Gamepad
{
    /// <summary>
    /// Interaction logic for FullWindowChart.xaml
    /// </summary>
    public partial class FullWindowChart : Window
    {
        public event Action OnClosedEvent;
        public FullWindowChart(int index) : base()
        {
            InitializeComponent();

            // Show in full window if there is a second monitor
            var screens = Screen.AllScreens;
            index %= Screen.AllScreens.Length;
            var firstSecondary = screens[index];
            if (firstSecondary != null)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                // Ensure Window is minimized on creation
                WindowState = WindowState.Minimized;
                // Define Position on Secondary screen, for "Normal" window-mode
                // ( Here Top/Left-Position )
                Left = firstSecondary.Bounds.Left;
                Top = firstSecondary.Bounds.Top;
            }

            Loaded += WindowLoaded;
            SizeChanged += (_, _) => UpdateChartSize();
        }

        private void WindowLoaded(object sender, RoutedEventArgs e)
        {
            // Maximize after position is defined in constructor
            WindowState = WindowState.Maximized;

            StripChart.Start();
            StripChart.MaxY = 1200;
            StripChart.Capacity = 10000;
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);

            UpdateChartSize();
        }

        private void UpdateChartSize()
        {
            double size = Math.Min(ActualWidth * 0.95, ActualHeight * 0.95);
            XYChartContainer.Width = size;
            XYChartContainer.Height = size;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            OnClosedEvent?.Invoke();
        }
    }
}
