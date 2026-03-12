//using System.Windows;
// FIXME
//namespace Base.Pages
//{
//    using Base.core;
//    using Services;
//    using System.Collections.Concurrent;

//    public class LogsPage : PageBase
//    {
//        private static readonly Lazy<LogsPage> instance = new(() => new LogsPage());
//        public static LogsPage Instance => instance.Value;
//        public override string PageName => "Logs";
//        private const int MaxLogLength = 10000;
//        private readonly ConcurrentQueue<string> logQueue = new();
//        private bool isWriting = false;
//        private readonly object writeLock = new();

//        public override void Awake()
//        {
//            Debug.OnLog += AppendLog;
//        }

//        private void AppendLog(string message)
//        {
//            logQueue.Enqueue(message);

//            lock (writeLock)
//            {
//                if (isWriting) return;
//            }
//            Application.Current?.Dispatcher.Invoke(PendingCmdParser);
//        }

//        [AppMenuItem("Clear Logs", Key = System.Windows.Input.Key.R, ModifierKeys = System.Windows.Input.ModifierKeys.Control)]
//        public void ClearLogs()
//        {
//            Main.LogTextBox.Text = string.Empty;
//        }

//        [AppMenuItem("Test Logs", Key = System.Windows.Input.Key.F6)]
//        public void Test()
//        {
//            for (int i = 0; i < 10; i++)
//            {
//                Debug.Log("This is a test log message number", i + 1);
//            }
//        }

//        public void ScrollToBottom()
//        {
//            Main.LogScrollViewer.ScrollToEnd();
//        }

//        private void PendingCmdParser()
//        {
//            lock (writeLock)
//            {
//                if (isWriting) return;
//                isWriting = true;
//                while (!logQueue.IsEmpty)
//                {
//                    if (!logQueue.TryDequeue(out string message)) continue;
//                    WriteLog(message);
//                }
//                isWriting = false;
//            }
//        }

//        public void WriteLog(params string[] messages)
//        {
//            if (Main == null) return;
//            bool isAtBottom = Main.LogScrollViewer.VerticalOffset >= Main.LogScrollViewer.ScrollableHeight - 10;

//            string prefix = $"[{DateTime.Now:HH:mm:ss}]";

//            for (int i = 0; i < messages.Length; i++)
//            {
//                string message = messages[i];
//                if (i == 0)
//                    Main.LogTextBox.Text += $"{prefix} {message}\n";
//                else
//                    Main.LogTextBox.Text += $"{new string(' ', prefix.Length)} {message}\n";
//            }

//            if (Main.LogTextBox.Text.Length > MaxLogLength)
//            {
//                Main.LogTextBox.Text = Main.LogTextBox.Text.Substring(Main.LogTextBox.Text.Length - MaxLogLength);
//                Main.LogTextBox.CaretIndex = Main.LogTextBox.Text.Length;
//            }

//            if (isAtBottom)
//            {
//                Main.LogScrollViewer.ScrollToEnd();
//            }
//        }
//    }
//}
