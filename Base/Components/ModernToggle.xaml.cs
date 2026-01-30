using System.Windows;
using System.Windows.Controls;

namespace Base.Components
{
	public partial class ModernToggle : UserControl
	{
		public event Action<bool> OnValueChanged;

		public ModernToggle()
		{
			InitializeComponent();

			PART_Toggle.Checked += (s, e) => UpdateIsOn(true);
			PART_Toggle.Unchecked += (s, e) => UpdateIsOn(false);
		}

		public static readonly DependencyProperty IsOnProperty =
			DependencyProperty.Register(nameof(IsOn), typeof(bool), typeof(ModernToggle),
				new PropertyMetadata(false, IsOnPropertyChanged));

		private static void IsOnPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			var control = (ModernToggle)d;
			bool newValue = (bool)e.NewValue;
			control.PART_Toggle.IsChecked = newValue;
			control.IsOn = newValue;
			control.OnValueChanged?.Invoke(newValue);
		}

		private void UpdateIsOn(bool value)
		{
			if (IsOn != value)
			{
				IsOn = value;
			}
		}

		public bool IsOn
		{
			get => (bool)GetValue(IsOnProperty);
			set => SetValue(IsOnProperty, value);
		}
	}
}
