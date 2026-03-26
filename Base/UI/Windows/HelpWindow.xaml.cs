using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Base.Core;

namespace Base.Components
{
    public partial class HelpWindow : Window
    {
        public ObservableCollection<HelpItem> Items { get; } = new();

        private ICollectionView _view;

        public HelpWindow()
        {
            InitializeComponent();
            HelpList.ItemsSource = Items;
            _view = CollectionViewSource.GetDefaultView(Items);
            FilterBox.TextChanged += (_, __) => ApplyFilter();
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.F && (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
                {
                    FilterBox.Focus();
                    FilterBox.SelectAll();
                    e.Handled = true;
                }
            };
        }

        public void LoadFromRoot(FrameworkElement root)
        {
            Items.Clear();
            foreach (var item in VisualScan.FindWithToolTips(root))
                Items.Add(item);
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var q = FilterBox.Text?.Trim() ?? string.Empty;
            _view.Filter = string.IsNullOrEmpty(q)
                ? null
                : new Predicate<object>(o =>
                {
                    var hi = (HelpItem)o;
                    return (hi.ControlPath?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                        || (hi.Tooltip?.IndexOf(q, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
                });
            _view.Refresh();
        }

        private void List_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (HelpList.SelectedItem is not HelpItem hi || hi.Element == null) return;

            hi.Element.BringIntoView();
            AdornerPulse.Pulse(hi.Element, TimeSpan.FromSeconds(1.25));
            if (hi.Element is Control c) c.Focus();
        }

        public static void Show(Window owner, FrameworkElement page, string description = "")
        {
            var w = new HelpWindow { Owner = owner };
            w.DescriptionText.Text = description;
            w.DescriptionText.Visibility = string.IsNullOrEmpty(description) ? Visibility.Collapsed : Visibility.Visible;
            w.LoadFromRoot(page);
            w.ShowDialog();
        }

        public sealed class HelpItem
        {
            public FrameworkElement? Element { get; init; }
            public string ControlPath { get; init; } = "";
            public string Tooltip { get; init; } = "";
        }

        internal static class VisualScan
        {
            public static IEnumerable<HelpItem> FindWithToolTips(FrameworkElement root)
            {
                if (root == null) yield break;
                foreach (var fe in EnumerateVisuals(root))
                {
                    if (fe.ToolTip is null) continue;
                    var text = ExtractToolTipText(fe.ToolTip);
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    yield return new HelpItem
                    {
                        Element = fe,
                        ControlPath = BuildPath(fe),
                        Tooltip = text
                    };
                }
            }

            private static IEnumerable<FrameworkElement> EnumerateVisuals(DependencyObject root)
            {
                var stack = new Stack<DependencyObject>();
                stack.Push(root);

                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    if (cur is FrameworkElement fe) yield return fe;

                    var count = VisualTreeHelper.GetChildrenCount(cur);
                    for (int i = 0; i < count; i++)
                        stack.Push(VisualTreeHelper.GetChild(cur, i));
                }
            }

            private static string ExtractToolTipText(object toolTip)
            {
                return toolTip switch
                {
                    string s => s,
                    TextBlock tb => tb.Text,
                    ToolTip tt => ExtractToolTipText(tt.Content!),
                    FrameworkElement fe => fe switch
                    {
                        ContentControl cc => cc.Content?.ToString() ?? fe.ToString(),
                        _ => fe.ToString()
                    },
                    _ => toolTip?.ToString() ?? string.Empty
                };
            }

            private static string BuildPath(FrameworkElement fe)
            {
                // "Window/Grid[0]/StackPanel[1]/Button(Name)"
                var chain = new List<string>();
                DependencyObject? cur = fe;
                while (cur != null)
                {
                    string part;
                    if (cur is FrameworkElement e)
                    {
                        var idx = GetIndexInParent(cur);
                        var name = string.IsNullOrWhiteSpace(e.Name) ? null : e.Name;
                        part = name != null ? $"{e.GetType().Name}({name})" : $"{e.GetType().Name}[{idx}]";
                    }
                    else
                    {
                        part = cur.GetType().Name;
                    }

                    chain.Add(part);
                    cur = VisualTreeHelper.GetParent(cur);
                }
                chain.Reverse();
                return string.Join("/", chain);
            }

            private static int GetIndexInParent(DependencyObject child)
            {
                var parent = VisualTreeHelper.GetParent(child);
                if (parent == null) return 0;
                int count = VisualTreeHelper.GetChildrenCount(parent);
                for (int i = 0, pos = 0; i < count; i++)
                {
                    var c = VisualTreeHelper.GetChild(parent, i);
                    if (c.GetType() == child.GetType())
                    {
                        if (ReferenceEquals(c, child)) return pos;
                        pos++;
                    }
                }
                return 0;
            }
        }

        internal sealed class PulseAdorner : Adorner
        {
            private readonly Pen _pen;
            public PulseAdorner(UIElement adorned) : base(adorned)
            {
                _pen = new Pen(Brushes.OrangeRed, 3) { DashStyle = DashStyles.Dash };
                IsHitTestVisible = false;
            }
            protected override void OnRender(DrawingContext dc)
            {
                var r = new Rect(AdornedElement.RenderSize);
                r.Inflate(3, 3);
                dc.DrawRectangle(null, _pen, r);
            }
        }

        internal static class AdornerPulse
        {
            public static void Pulse(FrameworkElement element, TimeSpan duration)
            {
                var layer = AdornerLayer.GetAdornerLayer(element);
                if (layer == null) return;

                var ad = new PulseAdorner(element);
                layer.Add(ad);

                var timer = new DispatcherTimer { Interval = duration };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    layer.Remove(ad);
                };
                timer.Start();
            }
        }
    }
}
