
namespace Base.Services
{
	using System.Text;
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
				sb.Append(" ");
			}
			OnLog?.Invoke(sb.ToString());
		}
	}
}
