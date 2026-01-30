using Base.Pages;
using Base.Services;
using Base.Services.APIService;
using Base.Services.Peripheral;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace KeyboardHallSensor
{
    /// <summary>
    /// This class provides a framework for creating a keyboard page in a WPF application.
    /// It handles the setup of the keyboard layout, the display of key states, and the management of UI elements.
    /// It also provides methods for adding properties and buttons to the page.
    /// The class is generic and can be used with any type that inherits from KeyboardPageBase.
    /// </summary>
    /// <typeparam name="T">The type of the page that inherits from KeyboardPageBase for instance management.</typeparam>
    public abstract class KeyboardPageBase : PageBase
    {
        public override string Glyph => "\uE765";
        protected ConcurrentDictionary<byte, KeyDisplay> KeyDisplays { get; private set; } = new();
        private ConcurrentStack<KeyDisplay> spawnedKeys = new();
        protected PeripheralInterface ActiveInterface => KeyboardCommonProtocol.Instance.ActiveInterface;

        protected Canvas Canvas { get; private set; }
        protected UniformGrid RogueKeysGrid { get; private set; }
        private StackPanel propertyStack;
        private int currentRow = 0;
        private int currentColumn = 0;

        public override void Awake()
        {
            base.Awake();
            FormPage();
            SetupKeyboard();
        }

        protected override void OnEnable()
        {
            base.OnEnable();

            Enter();
            if (ActiveInterface == null) return;

            ActiveInterface.OnDataReceived += Parse;
            DeviceSelection.Instance.OnActiveDeviceDisconnected += ClearAll;
        }

        protected override void OnDisable()
        {
            base.OnDisable();

            Exit();
            if (ActiveInterface == null) return;

            ActiveInterface.OnDataReceived -= Parse;
            DeviceSelection.Instance.OnActiveDeviceDisconnected -= ClearAll;
        }

        protected abstract void Enter();
        protected abstract void Exit();
        public abstract void Parse(ReadOnlyMemory<byte> bytes);

        private void FormPage()
        {
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            propertyStack = new StackPanel
            {
                Margin = new Thickness(0, 0, 10, 8),
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetColumn(propertyStack, 0);

            var scrollViewer = new ScrollViewer()
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            string canvasName = PageName + "Canvas";
            canvasName = canvasName.Replace(" ", "_");
            Canvas = new Canvas()
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
                Name = canvasName
            };
            scrollViewer.Content = Canvas;
            Canvas.SetResourceReference(BackgroundProperty, "SystemControlBackgroundLowBrush");
            Grid.SetColumn(scrollViewer, 1);

            RogueKeysGrid = new UniformGrid
            {
                Rows = 128,
                Columns = 4,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            Canvas.SetLeft(RogueKeysGrid, 0);
            Canvas.SetTop(RogueKeysGrid, 340);
            Canvas.Children.Add(RogueKeysGrid);

            root.Children.Add(propertyStack);
            root.Children.Add(scrollViewer);
        }

        protected void SetupKeyboard()
        {
            var keyboardLayout = LayoutConverter.Convert();
            float unit = 50;
            foreach (KeyDef keyDef in keyboardLayout)
            {
                byte keycode = LayoutConverter.keyLabelToCode.TryGetValue(keyDef.Label, out var code) ? code : (byte)0;
                if (keycode != 0 && KeyDisplays.ContainsKey(keycode)) continue;
                AddKeyDisplay(Canvas.Children, keycode, keyDef, unit);
            }
        }

        protected E AddProperty<E>(E element) where E : FrameworkElement
        {
            var margin = element.Margin;
            margin.Bottom += 5;
            element.Margin = margin;

            element.HorizontalAlignment = HorizontalAlignment.Stretch;
            propertyStack.Children.Add(element);
            element.SetResourceReference(ForegroundProperty, "TextControlForeground");
            return element;
        }

        protected Button AddButton(string text, Action handler)
        {
            var button = new Button()
            {
                Content = text,
                FontSize = 12,
            };
            button.Click += (_, _) => { handler(); };
            return AddProperty(button);
        }

        protected ToggleButton AddToggle(string text, Action<bool> handler, bool isOn = false)
        {
            var toggleButton = new ToggleButton()
            {
                Content = text,
                FontSize = 12,
                IsChecked = isOn,
            };
            toggleButton.Click += (_, _) =>
            {
                handler(toggleButton.IsChecked ?? false);
            };
            return AddProperty(toggleButton);
        }

        protected TextBlock Header(string text, double fontSize = 14, Brush color = null)
        {
            return AddProperty(new TextBlock()
            {
                Text = text,
                FontSize = fontSize,
                Margin = new Thickness(0, 12, 0, 0),
                FontWeight = FontWeights.Bold,
                Foreground = color ?? Brushes.Black
            });
        }

        protected TextBlock AddTextProperty(string text, double fontSize = 12, Brush color = null)
        {
            return AddProperty(new TextBlock()
            {
                Text = text,
                FontSize = fontSize,
                Foreground = color ?? Brushes.Black
            });
        }

        protected delegate bool TextBoxChangedDelegate(string text);

        protected TextBox AddTextBox(
            string labelText,
            string text = "",
            double fontSize = 12,
            Orientation orientation = Orientation.Horizontal,
            Brush color = null,
            TextBoxChangedDelegate handler = null
        )
        {
            var stackPanel = new StackPanel()
            {
                Orientation = orientation,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var label = new TextBlock()
            {
                Text = labelText,
                FontSize = fontSize,
                Foreground = color ?? Brushes.Black,
                Margin = new Thickness(0, 0, 5, 0),
            };
            label.SetResourceReference(ForegroundProperty, "TextControlForeground");

            var border = new Border()
            {
                BorderBrush = Brushes.Gray,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(5, 0, 5, 0),
                MinWidth = 50,
            };

            var textBox = new TextBox()
            {
                Text = text,
                FontSize = fontSize,
                Foreground = color ?? Brushes.Black,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                TextAlignment = TextAlignment.Center,
            };
            textBox.SetResourceReference(ForegroundProperty, "TextControlForeground");

            border.Child = textBox;

            stackPanel.Children.Add(label);
            stackPanel.Children.Add(border);

            string latestValidText = text;

            if (handler != null)
            {
                textBox.TextChanged += (_, _) =>
                {
                    bool isValid = handler(textBox.Text);
                    if (isValid) latestValidText = textBox.Text;
                };

                textBox.LostFocus += (_, _) =>
                {
                    textBox.Text = latestValidText;
                    handler(textBox.Text);
                };
            }
            AddProperty(stackPanel);
            return textBox;
        }

        public KeyDisplay AddKeyDisplay(UIElementCollection parent, byte keycode, KeyDef keyDef, float unit = 1)
        {
            return AddKeyDisplay(parent, keycode, keyDef.X, keyDef.Y, keyDef.W, keyDef.H, unit, keyDef.Label);
        }

        public KeyDisplay AddKeyDisplay(UIElementCollection parent, byte keycode, float x, float y, float w = 50, float h = 50, float unit = 1, string label = null)
        {
            KeyDisplay newDisplay = new(keycode, w * unit, h * unit, label);
            Canvas.SetLeft(newDisplay, x * unit);
            Canvas.SetTop(newDisplay, y * unit);

            parent.Add(newDisplay);
            KeyDisplays[keycode] = newDisplay;
            spawnedKeys.Push(newDisplay);

            newDisplay.OnLeftClicked += () => OnKeyDisplayClicked(keycode);

            return newDisplay;
        }

        public KeyDisplay AddKeyDisplay(byte keycode, float unit = 1, float gap = 5, string label = null)
        {
            KeyDisplay newDisplay = AddKeyDisplay(RogueKeysGrid.Children, keycode, x: currentColumn * unit + gap, y: currentRow * unit + gap + 350, label: label);
            currentColumn++;
            if (currentColumn >= 16)
            {
                currentColumn = 0;
                currentRow++;
            }
            return newDisplay;
        }

        protected virtual void OnKeyDisplayClicked(byte keycode) { }

        public void ResetMinMax()
        {
            foreach (var item in KeyDisplays)
            {
                item.Value.ResetMinMax();
            }
        }

        [GET("/ClearAll", true)]
        protected virtual void ClearAll()
        {
            foreach (var item in spawnedKeys)
            {
                Canvas.Children.Remove(item);
            }
            spawnedKeys.Clear();
            KeyDisplays.Clear();

            foreach(var item in RogueKeysGrid.Children)
            {
                if (item is KeyDisplay keyDisplay)
                {
                    RogueKeysGrid.Children.Remove(keyDisplay);
                }
            }

            currentRow = 0;
            currentColumn = 0;
            SetupKeyboard();
        }
    }
}
