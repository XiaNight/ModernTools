using System.Windows.Controls;
using System.Windows.Media;


namespace KeyboardHallSensor
{
    internal class CustomizedSegmentPage : MFGKeyboardStreamingPage
    {
        public override string PageName => "Customized Segment";
        public override string ShortName => "CSG";
        protected override string MfgCmdName => "hall_analog";
        protected override byte MfdCmdCode => 0x04;
        protected override int MfgCmdPackageSize => 3;
        protected override int MaxValue { get; set; } = 2560;
        protected override bool CanRecord => true;

        private List<SegmentEntry> segmentEntries = new List<SegmentEntry>();
        private StackPanel entryPanel;

        public override void Awake()
        {
            base.Awake();

            AddButton("Add Segment", () =>
            {
                int index = segmentEntries.Count;
                var last = segmentEntries.LastOrDefault();
                InsertSegmentAt(index, last.Input, last.Output);
            });

            entryPanel = new StackPanel()
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right
            };

            InsertSegmentAt(0, 0, 0);
            InsertSegmentAt(1, 2559, 350);

            AddProperty(entryPanel);
        }

        protected override int ParseValue(ReadOnlyMemory<byte> values)
        {
            int rawValue = values.Span[2] << 8 | values.Span[1];
            return SegmentLinearConversion(rawValue);
        }

        private int SegmentLinearConversion(int value)
        {
            for (int i = 0; i < segmentEntries.Count - 1; i++)
            {
                var segA = segmentEntries[i];
                var segB = segmentEntries[i + 1];
                if (value >= segA.Input && value <= segB.Input)
                {
                    float t = (float)(value - segA.Input) / (segB.Input - segA.Input);
                    return (int)(segA.Output + t * (segB.Output - segA.Output));
                }
            }
            // Out of range
            if (value < segmentEntries[0].Input)
            {
                return segmentEntries[0].Output;
            }
            else
            {
                return segmentEntries.Last().Output;
            }
        }

        private void RemoveSegmentAt(int index)
        {
            var segmentEntry = segmentEntries[index];
            segmentEntries.RemoveAt(index);

            for (int i = index; i < segmentEntries.Count; i++)
            {
                segmentEntries[i].SetIndex(i);
            }

            entryPanel.Children.Remove(segmentEntry);
        }

        private void InsertSegmentAt(int index, int input, int output)
        {
            var segmentEntry = new SegmentEntry(index, input, output);

            segmentEntries.Insert(index, segmentEntry);
            segmentEntry.OnDeleteRequested += () => { 
                int localIndex = segmentEntries.IndexOf(segmentEntry);
                RemoveSegmentAt(localIndex);
            };

            for (int i = index; i < segmentEntries.Count; i++)
            {
                segmentEntries[i].SetIndex(i);
            }

            entryPanel.Children.Add(segmentEntry);
        }

        private class SegmentEntry : StackPanel
        {
            public int Input { get; private set; }
            public int Output { get; private set; }

            public Label indexLabel;
            public TextBox inputTextBox;
            public TextBox outputTextBox;
            public Button deleteButton;

            public event Action OnDeleteRequested;

            public SegmentEntry(int index, int input, int output) : base()
            {
                Orientation = Orientation.Horizontal;
                Margin = new System.Windows.Thickness(0, 5, 0, 5);

                Input = input;
                Output = output;

                indexLabel = new Label() { Content = $"{index + 1}:" };

                inputTextBox = new TextBox()
                {
                    Width = 50,
                    Text = input.ToString()
                };
                outputTextBox = new TextBox()
                {
                    Width = 50,
                    Text = output.ToString(),
                    Margin = new System.Windows.Thickness(10, 0, 0, 0)
                };
                deleteButton = new Button()
                {
                    Content = "Delete",
                    Margin = new System.Windows.Thickness(10, 0, 0, 0),
                };

                inputTextBox.TextChanged += (s, e) =>
                {
                    if (int.TryParse(inputTextBox.Text, out var value))
                    {
                        Input = value;
                    }
                    else
                    {
                        inputTextBox.Text = Input.ToString();
                    }
                };
                outputTextBox.TextChanged += (s, e) =>
                {
                    if (int.TryParse(outputTextBox.Text, out var value))
                    {
                        Output = value;
                    }
                    else
                    {
                        outputTextBox.Text = Output.ToString();
                    }
                };
                deleteButton.Click += (s, e) =>
                {
                    OnDeleteRequested?.Invoke();
                };

                Children.Add(indexLabel);
                Children.Add(inputTextBox);
                Children.Add(outputTextBox);
                Children.Add(deleteButton);
            }

            public void SetIndex(int index)
            {
                indexLabel.Content = $"{index + 1}:";
            }
        }
    }
}