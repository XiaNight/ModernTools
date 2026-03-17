namespace Base.Services;

using System.Text;
using System.Windows;

public static class Debug
{
    public static event Action<string> OnLog;
    public static void Log(params object[] messages)
    {
        if (OnLog == null) return;
        StringBuilder sb = new();
        foreach (var message in messages)
        {
            sb.Append(message);
            sb.Append(' ');
        }

        bool isOnUiThread = Application.Current?.Dispatcher?.CheckAccess() ?? false;
        if (isOnUiThread)
        {
            OnLog?.Invoke(sb.ToString());
        }
        else
        {
            try
            {
                Application.Current?.Dispatcher?.Invoke(() => OnLog?.Invoke(sb.ToString()));
            }
            catch { }
        }
    }
}
