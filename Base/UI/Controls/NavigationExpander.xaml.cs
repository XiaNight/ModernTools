using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Base.Components
{
    /// <summary>
    /// Interaction logic for NavigationExpander.xaml
    /// </summary>
    [ContentProperty(nameof(Items))]
    public partial class NavigationExpander : UserControl, INavigationItem
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(nameof(Text), typeof(string), typeof(NavigationExpander),
                new PropertyMetadata("Home"));

        public static readonly DependencyProperty ShortTextProperty =
            DependencyProperty.Register(nameof(ShortText), typeof(string), typeof(NavigationExpander),
                new PropertyMetadata("Home"));

        public static readonly DependencyProperty GlyphProperty =
            DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(NavigationExpander),
                new PropertyMetadata("\uE879"));

        public static readonly DependencyProperty SecondaryGlyphProperty =
            DependencyProperty.Register(nameof(SecondaryGlyph), typeof(string), typeof(NavigationExpander),
                new PropertyMetadata(""));

        public static readonly DependencyProperty IsCompactProperty =
            DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(NavigationExpander),
                new PropertyMetadata(false));

        public static readonly DependencyProperty ItemHeightProperty =
            DependencyProperty.Register(nameof(ItemHeight), typeof(int), typeof(NavigationExpander),
                new PropertyMetadata(48));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public string ShortText
        {
            get => (string)GetValue(ShortTextProperty);
            set => SetValue(ShortTextProperty, value);
        }

        public string Glyph
        {
            get => (string)GetValue(GlyphProperty);
            set => SetValue(GlyphProperty, value);
        }

        public string SecondaryGlyph
        {
            get => (string)GetValue(SecondaryGlyphProperty);
            set => SetValue(SecondaryGlyphProperty, value);
        }

        public bool IsCompact
        {
            get => (bool)GetValue(IsCompactProperty);
            set => SetValue(IsCompactProperty, value);
        }

        public int ItemHeight
        {
            get => (int)GetValue(ItemHeightProperty);
            set => SetValue(ItemHeightProperty, value);
        }

        public int OrderIndex { get; set; } = 0;
        public bool IsChild { get; set; } = false;
        public int Size => (NavToggleButton.IsChecked ?? true) ? ChildrenSize() + 1 : 1;

        public ObservableCollection<INavigationItem> Items { get; } = new();

        public event Action OnClick;

        public NavigationExpander()
        {
            InitializeComponent();

            NavToggleButton.Checked += (_, _) =>
            {
                Expand();
                OnClick?.Invoke();
            };

            NavToggleButton.Unchecked += (_, _) => Collapse();
            Items.CollectionChanged += LoadChild;

            Loaded += (_, _) =>
            {
                ExpandContainer.Height = NavToggleButton.IsChecked == true
                    ? MeasureExpandContentHeight() : 0;
            };
        }

        public void Collapse()
        {
            AnimateExpandHeight(0);
        }

        public void Expand()
        {
            AnimateExpandHeight(MeasureExpandContentHeight());
        }


        public void EnterCompactMode()
        {
            IsCompact = true;
            foreach (var item in Items)
            {
                item.EnterCompactMode();
            }
        }
        public void ExitCompactMode()
        {
            IsCompact = false;
            foreach (var item in Items)
            {
                item.ExitCompactMode();
            }
        }

        public void SetHighlightedState(bool state)
        {

        }

        private double MeasureExpandContentHeight()
        {
            return ChildrenSize() * ItemHeight;
        }

        private int ChildrenSize() => Items.Sum(i => i.Size);

        public void UpdateLayoutAnimate()
        {
            AnimateExpandHeight(MeasureExpandContentHeight());
        }

        private void LoadChild(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (INavigationItem item in e.NewItems)
                {
                    item.IsChild = true;
                    item.ItemHeight = ItemHeight;
                }
            }

            if (e.OldItems != null)
            {
                foreach (INavigationItem item in e.OldItems)
                {
                    item.IsChild = false;
                }
            }
        }

        private void AnimateExpandHeight(double to)
        {
            double current = ExpandContainer.Height;
            if (GetParentNavigationItem(out INavigationItem parentItem))
            {
                parentItem.UpdateLayoutAnimate();
            }

            var animation = new DoubleAnimation
            {
                From = current,
                To = to,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            ExpandContainer.BeginAnimation(HeightProperty, animation);
        }

        private bool GetParentNavigationItem(out INavigationItem parentItem)
        {
            parentItem = null;

            DependencyObject current = VisualTreeHelper.GetParent(this);
            while (current != null)
            {
                if (current is INavigationItem navItem)
                {
                    parentItem = navItem;
                    return true;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return false;
        }
    }
}
