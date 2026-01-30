using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Gamepad
{
    /// <summary>
    /// Interaction logic for GamepadButton.xaml
    /// </summary>
    public partial class GamepadButton : UserControl
    {
        public static readonly DependencyProperty TitleTextProperty =
        DependencyProperty.Register(
            nameof(TitleText),
            typeof(string),
            typeof(GamepadButton),
            new PropertyMetadata(string.Empty));

        public string TitleText
        {
            get => (string)GetValue(TitleTextProperty);
            set => SetValue(TitleTextProperty, value);
        }
        public GamepadButton()
        {
            InitializeComponent();
        }

        public void SetValue(bool value)
        {
            FillRect.Height = (int)(FillContainer.ActualHeight * (value ? 1 : 0));
        }

        public void SetCounter(int value)
        {
            if (value <= 0) value = 0;
            ValueText.Text = value.ToString("0");
        }
    }
}
