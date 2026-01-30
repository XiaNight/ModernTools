using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace Base.Core
{
	public static class PopupInfo
	{
		private static ToolTip tooltip;

		public static void Show(string message, int duration = 2000)
		{
			// If there's an existing tooltip, close it before creating a new one
			if (tooltip != null && tooltip.IsOpen)
			{
				tooltip.IsOpen = false; // Close the existing tooltip
				tooltip = null; // Clear the reference to the previous tooltip
			}

			// Create and configure a new ToolTip
			tooltip = new ToolTip
			{
				Content = message,
				IsOpen = true
			};

			var tooltipCopy = tooltip;

			// Close the ToolTip after the specified duration
			Task.Delay(duration).ContinueWith(t =>
			{
				// Ensure we're updating the UI on the correct thread (UI thread)
				Application.Current.Dispatcher.Invoke(() =>
				{
					if (tooltipCopy == null || !tooltipCopy.IsOpen) return;
					tooltipCopy.IsOpen = false; // Close the tooltip
					tooltipCopy = null; // Clear the reference once it's closed
				});
			});
		}
    }

	public sealed class Util
	{
        public static string GetAssemblyAttribute<T>(Func<T, string> valueSelector) where T : Attribute
        {
            return GetAssemblyAttribute(Application.ResourceAssembly, valueSelector)
				?? GetAssemblyAttribute(Assembly.GetExecutingAssembly(), valueSelector);
        }

        public static string GetAssemblyAttribute<T>(Assembly assembly, Func<T, string> valueSelector) where T : Attribute
        {
            var attribute = assembly.GetCustomAttribute<T>();
			if (attribute == null) return "";

            var value = valueSelector(attribute);

            var plusIndex = value.IndexOf('+');
            return plusIndex >= 0 ? value.Substring(0, plusIndex) : value;
        }
    }
}