using System.Windows;
using System.Windows.Controls;

namespace Gamepad
{
    public partial class TriggerCounter : UserControl
    {
        public static readonly DependencyProperty TitleTextProperty =
        DependencyProperty.Register(
            nameof(TitleText),
            typeof(string),
            typeof(TriggerCounter),
            new PropertyMetadata(string.Empty));

        public string TitleText
        {
            get => (string)GetValue(TitleTextProperty);
            set => SetValue(TitleTextProperty, value);
        }
        public TriggerCounter()
        {
            InitializeComponent();
        }

        public void SetValue(byte value)
        {
            if (value < 0) return;
            FillRect.Width = (int)(FillContainer.ActualWidth * value / 255f);
            ValueText.Text = value.ToString();

            if(value > maxValue) {
                maxValue = value;
                MaxText.Text = maxValue.ToString();
                Canvas.SetLeft(MaxIndicator, FillRect.Width);
            }
        }

        public void SetCounter(int value)
        {
            if (value <= 0) value = 0;
            CountText.Text = value.ToString("0");
        }

        public void Clear()
        {
            maxValue = 0;
            MaxText.Text = "0";
            CountText.Text = "0";
            FillRect.Width = 0;
            ValueText.Text = "0";
            Canvas.SetLeft(MaxIndicator, 0);
        }

        private byte maxValue = 0;
    }
}
