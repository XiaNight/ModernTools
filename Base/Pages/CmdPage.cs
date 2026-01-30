namespace Base.Pages
{
    using Services;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Media;

    public class CmdPage : PageBase
    {
        public override string PageName => "Commands";
        public override int NavOrder => -1;

        private Button sendButton;
        private Button clearButton;
        private TextBox textBox;
        private StackPanel stackPanel;
        private byte battery1;
        private bool hasBattery2;
        private byte battery2;

        public override void Awake()
        {
            base.Awake();

            stackPanel = new StackPanel()
            {
                Orientation = Orientation.Vertical,
                Margin = new Thickness(10)
            };

            sendButton = new Button()
            {
                Content = "Send Command",
                Margin = new Thickness(10)
            };

            clearButton = new Button()
            {
                Content = "Clear",
                Margin = new Thickness(10)
            };

            textBox = new TextBox()
            {
                Margin = new Thickness(10),
                Height = 600,
                TextWrapping = TextWrapping.Wrap,
                IsReadOnly = true,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                Foreground = Brushes.LightGreen,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
            };

            sendButton.Click += (s, e) => SendCommand();
            clearButton.Click += (s, e) => textBox.Clear();

            stackPanel.Children.Add(sendButton);
            stackPanel.Children.Add(clearButton);
            stackPanel.Children.Add(textBox);

            root.Children.Add(stackPanel);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (ActiveInterface == null) return;

            ActiveInterface.OnDataReceived += Parse;

            ProtocalService.EnterFactory(ActiveInterface);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (ActiveInterface == null) return;

            ActiveInterface.OnDataReceived -= Parse;

            ProtocalService.ExitFactory(ActiveInterface);
        }

        private void Parse(ReadOnlyMemory<byte> data)
        {
            if (data.IsEmpty) return;

            //- Skip a byte sequence returned for response, real data comes after the first byte
            int skip = 1;
            ReadOnlySpan<byte> span = data.Span;

            ReadOnlySpan<byte> checkBytes = [0xFA, 0x0D, 0x00, 0x00];

            if (span.Length < skip + checkBytes.Length || !span.Slice(skip, checkBytes.Length).SequenceEqual(checkBytes))
            {
                return; // Not a valid command response
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                //textBox.Clear();
                //if (span.Length >= skip + 13)
                //{
                //	textBox.AppendText($"Charger_ID  :  SY6974\n");
                //	for (int i = 0; i < 12; i++)
                //	{
                //		textBox.AppendText($"SY6974 REG{i:X2}:  0x{span[i]:X2}\n");
                //	}
                //}

                //if (span.Length >= skip + 13 + 13)
                //{
                //	textBox.AppendText($"Charger_ID  :  SY6974\n");
                //	for (int i = 0; i < 12; i++)
                //	{
                //		textBox.AppendText($"SY6974 REG{i:X2}:  0x{span[skip + 14 + i]:X2}\n");
                //	}
                //}

                //textBox.ScrollToEnd();
            });
        }

        private void SendCommand()
        {
            if (ActiveInterface == null) return;

            ProtocalService.AppendCmd(ActiveInterface, "get_charger_info", false);
        }
    }
}