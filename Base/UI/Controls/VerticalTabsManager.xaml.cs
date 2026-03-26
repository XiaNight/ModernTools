using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace Base.Components
{
    /// <summary>
    /// Interaction logic for TabsManager.xaml
    /// </summary>
    public partial class VerticalTabsManager : UserControl
    {

        public static readonly DependencyProperty IsOpenProperty =
            DependencyProperty.Register(
                nameof(IsOpen),
                typeof(bool),
                typeof(VerticalTabsManager),
                new PropertyMetadata(true, OnIsOpenChanged));
        public bool IsOpen
        {
            get => (bool)GetValue(IsOpenProperty);
            set => SetValue(IsOpenProperty, value);
        }


        public ObservableCollection<INavigationItem> TopButtons { get; } = new();
        public ObservableCollection<INavigationItem> BottomButtons { get; } = new();

        public event Action<INavigationItem> OnTabChanged;

        public void Open() => IsOpen = true;

        public void Close() => IsOpen = false;

        public void ToggleOpen() => IsOpen = !IsOpen;

        public VerticalTabsManager()
        {
            InitializeComponent();

            Loaded += (_, _) =>
            {
                BeginAnimation(WidthProperty, null);
                Width = IsOpen ? OpenWidth : ClosedWidth;
            };
        }

        public void ExitCompactMode()
        {
            IsOpen = true;
            foreach (INavigationItem button in TopButtons) button.ExitCompactMode();
            foreach (INavigationItem button in BottomButtons) button.ExitCompactMode();
        }

        public void EnterCompactMode()
        {
            IsOpen = false;
            foreach (INavigationItem button in TopButtons) button.EnterCompactMode();
            foreach (INavigationItem button in BottomButtons) button.EnterCompactMode();
        }

        private void NavButtonClicked(INavigationItem button)
        {
            OnTabChanged?.Invoke(button);
        }

        public delegate INavigationItem AddButtonDelegate(string text, string[] path, string glyph = "\uE7EF", string secondaryGlyph = "", string secondaryText = "", int order = int.MaxValue);

        public INavigationItem AddTop(string text, string[] path, string glyph = "\uE7EF", string secondaryGlyph = "", string secondaryText = "", int order = int.MaxValue)
        {
            return Add(text, path, glyph, secondaryGlyph, secondaryText, order, TopButtons);
        }

        public INavigationItem AddBottom(string text, string[] path, string glyph = "\uE7EF", string secondaryGlyph = "", string secondaryText = "", int order = int.MaxValue)
        {
            return Add(text, path, glyph, secondaryGlyph, secondaryText, order, BottomButtons);
        }

        private INavigationItem Add(string text, string[] path, string glyph, string secondaryGlyph, string secondaryText, int order, ObservableCollection<INavigationItem> collection)
        {
            if (path != null && path.Length > 0 && !string.IsNullOrEmpty(path[0]))
            {
                NavigationExpander expander = GetOrAdd(path, collection);
                if (expander != null)
                {
                    collection = expander.Items;
                }
            }
            INavigationItem newButton = new NavigationButton
            {
                Text = text,
                Glyph = glyph,
                SecondaryGlyph = secondaryGlyph,
                ShortText = secondaryText,
                OrderIndex = order
            };
            newButton.OnClick += () => NavButtonClicked(newButton);
            collection.Add(newButton);

            // Sort buttons by OrderIndex
            collection.ToArray().OrderBy(b => b.OrderIndex).ToList().ForEach(b =>
            {
                collection.Remove(b);
                collection.Add(b);
            });
            return newButton;
        }

        private NavigationExpander GetOrAdd(string[] path, ObservableCollection<INavigationItem> collection)
        {
            if (path == null || path.Length == 0 || string.IsNullOrEmpty(path[0]))
                return null;
            foreach (var item in collection)
            {
                if (item is not NavigationExpander expander) continue;
                if (string.Compare(expander.Text, path[0]) == 0)
                {
                    if (path.Length > 1)
                        return GetOrAdd(path[1..], expander.Items);
                    else
                        return expander;
                }
            }
            var newExpander = new NavigationExpander
            {
                Text = path[0]
            };
            collection.Add(newExpander);
            // Sort buttons by OrderIndex
            collection.ToArray().OrderBy(b => b.OrderIndex).ToList().ForEach(b =>
            {
                collection.Remove(b);
                collection.Add(b);
            });
            if (path.Length > 1)
                return GetOrAdd(path[1..], newExpander.Items);
            else
                return newExpander;
        }

        public bool FindItem(string[] path, string name, out INavigationItem item)
        {
            item = null;
            if (path == null || path.Length == 0)
                return false;

            foreach (var button in TopButtons)
            {
                if (FindItemInItem(button, path, name, out item))
                    return true;
            }

            foreach (var button in BottomButtons)
            {
                if (FindItemInItem(button, path, name, out item))
                    return true;
            }

            return false;
        }

        private static bool FindItemInItem(INavigationItem button, string[] path, string name, out INavigationItem item)
        {
            item = null;
            if (string.Compare(button.Text, path[0]) != 0)
                return false;
            if (path.Length == 1)
            {
                item = button;
                return true;
            }
            if (button is not NavigationExpander expander)
                return false;
            foreach (var child in expander.Items)
            {
                if (FindItemInItem(child, path[1..], name, out item))
                    return true;
            }
            return false;
        }

        #region Animation

        public static readonly DependencyProperty OpenWidthProperty =
            DependencyProperty.Register(
                nameof(OpenWidth),
                typeof(double),
                typeof(VerticalTabsManager),
                new PropertyMetadata(270d));

        public static readonly DependencyProperty ClosedWidthProperty =
            DependencyProperty.Register(
                nameof(ClosedWidth),
                typeof(double),
                typeof(VerticalTabsManager),
                new PropertyMetadata(48d));

        public static readonly DependencyProperty AnimationDurationProperty =
            DependencyProperty.Register(
                nameof(AnimationDuration),
                typeof(Duration),
                typeof(VerticalTabsManager),
                new PropertyMetadata(new Duration(TimeSpan.FromMilliseconds(220))));

        public double OpenWidth
        {
            get => (double)GetValue(OpenWidthProperty);
            set => SetValue(OpenWidthProperty, value);
        }

        public double ClosedWidth
        {
            get => (double)GetValue(ClosedWidthProperty);
            set => SetValue(ClosedWidthProperty, value);
        }

        public Duration AnimationDuration
        {
            get => (Duration)GetValue(AnimationDurationProperty);
            set => SetValue(AnimationDurationProperty, value);
        }

        private static void OnIsOpenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not VerticalTabsManager control)
                return;

            bool isOpen = (bool)e.NewValue;
            control.AnimateWidth(isOpen ? control.OpenWidth : control.ClosedWidth);

            if (isOpen)
                control.ExitCompactMode();
            else
                control.EnterCompactMode();
        }

        private void AnimateWidth(double to)
        {
            var animation = new DoubleAnimation
            {
                To = to,
                Duration = AnimationDuration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            BeginAnimation(WidthProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }

        #endregion
    }
}
