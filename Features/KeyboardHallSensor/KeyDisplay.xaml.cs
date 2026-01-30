using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static System.Runtime.CompilerServices.RuntimeHelpers;

namespace KeyboardHallSensor
{
    /// <summary>
    /// Interaction logic for KeyDisplay.xaml
    /// </summary>
    public partial class KeyDisplay : UserControl
    {
        public static readonly DependencyProperty IsMinMaxShownProperty =
            DependencyProperty.Register(
                nameof(IsMinMaxShown),
                typeof(bool),
                typeof(KeyDisplay),
                new PropertyMetadata(false, OnIsMinMaxShownChanged));

        private static void OnIsMinMaxShownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is KeyDisplay keyDisplay && e.NewValue is bool newValue)
            {
                keyDisplay.SetMinMaxDisplay(newValue);
            }
        }
        public bool IsMinMaxShown
        {
            get => (bool)GetValue(IsMinMaxShownProperty);
            set => SetValue(IsMinMaxShownProperty, value);
        }

        private void OnLeftClick(object sender, MouseButtonEventArgs e)
        {
            OnLeftClicked?.Invoke();
        }

        private void OnRightClick(object sender, MouseButtonEventArgs e)
        {
            OnRightClicked?.Invoke();

            // Copy value to clipboard
            Clipboard.SetText(IsMinMaxShown ? $"{min:F0}, {max:F0}" : ValueText.Text);
        }

        public byte Keycode { get; private set; }
        public string Label { get; private set; }
        public event Action OnLeftClicked;
        public event Action OnRightClicked;

        private double min;
        private bool isMinSet = false;
        private double max;
        private bool isMaxSet = false;
        private double topValue = double.MaxValue;

        public KeyDisplay(byte keycode, float w, float h, string label = "")
        {
            InitializeComponent();

            Width = w;
            Height = h;
            Keycode = keycode;
            Label = label;

            SetText("");
            SetFill(0, 1);
            SetMinMaxDisplay(false);
        }

        public void SetText(string text)
        {
            ValueText.Text = text;
        }

        public void SetFill(double value, double maxValue)
        {
            topValue = maxValue;

            FillRect.Height = Height * value / maxValue;

            min = Math.Min(min, value);
            max = Math.Max(max, value);
            isMinSet = true;
            isMaxSet = true;
            if (IsMinMaxShown)
            {
                UpdateMinMax();
            }
        }

        public void SetFillColor(Brush color)
        {
            FillRect.Fill = color;
        }
        public void SetFillColor(string colorKey)
        {
            FillRect.Fill = (Brush)Application.Current.Resources[colorKey];
        }

        public void SetBorderColor(Brush color)
        {
            MainBorder.BorderBrush = color;
        }
        public void SetBorderColor(string colorKey)
        {
            MainBorder.SetResourceReference(BorderBrushProperty, colorKey);
        }

        public void ShowLabel()
        {
            string displayText = Label.Replace("\\n", "\n");
            LabelText.Text = displayText;
            LabelText.FontSize = displayText.Length > 3 ? 10 : 14;
        }

        public void ResetMinMax()
        {
            min = double.MaxValue;
            max = 0;
            isMinSet = false;
            isMaxSet = false;
            UpdateMinMax();
        }

        public void SetMinMaxDisplay(bool state)
        {
            if (state) ResetMinMax();

            IsMinMaxShown = state;
            Visibility normalVis = state ? Visibility.Collapsed : Visibility.Visible;
            Visibility minMaxVis = state ? Visibility.Visible : Visibility.Collapsed;

            MinRect.Visibility = minMaxVis;
            MinText.Visibility = minMaxVis;
            MaxRect.Visibility = minMaxVis;
            MaxText.Visibility = minMaxVis;

            UpdateMinMax();
        }

        private void UpdateMinMax()
        {
            ((TranslateTransform)MinRect.RenderTransform).Y = Height * -Math.Min(min / topValue, 1);
            ((TranslateTransform)MaxRect.RenderTransform).Y = Height * -Math.Min(max / topValue, 1);
            MinText.Text = isMinSet ? min.ToString("F0") : "";
            MaxText.Text = isMaxSet ? max.ToString("F0") : "";

            MinRect.Visibility = IsMinMaxShown && isMinSet ? Visibility.Visible : Visibility.Collapsed;
            MaxRect.Visibility = IsMinMaxShown && isMaxSet ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
