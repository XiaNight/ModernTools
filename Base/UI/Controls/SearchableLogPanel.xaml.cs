using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Base.Components
{
    public partial class SearchableLogPanel : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private readonly Paragraph para = new Paragraph { Margin = new Thickness(0) };

        // Regex matching
        private readonly List<(int Index, int Length)> matchSpans = new();
        private bool isMatchDirty = false;
        private int currentMatchIndex = -1;

        private readonly List<TextRange> highlightedRanges = new();
        private string cachedFullText = "";
        private int logVersion = 0;
        private int paintedVersion = -1;

        private ScrollViewer scrollViewer;

        private readonly DispatcherTimer debounceTimer;
        private readonly DispatcherTimer repaintTimer;

        private string searchText = "";
        private bool isSearchTextDirty = false;
        public string SearchText
        {
            get => searchText;
            set
            {
                if (searchText == value) return;
                searchText = value ?? "";
                isSearchTextDirty = true;
                OnPropertyChanged(nameof(SearchText));
                ScheduleRebuild();
            }
        }

        private string matchLabel = "0/0";
        public string MatchLabel
        {
            get => matchLabel;
            private set
            {
                if (matchLabel == value) return;
                matchLabel = value;
                OnPropertyChanged(nameof(MatchLabel));
            }
        }

        public Brush MatchHighlightBrush { get; set; } =
            new SolidColorBrush(Color.FromArgb(0xA0, 0xFF, 0xD1, 0x00));

        public Brush CurrentMatchHighlightBrush { get; set; } =
            new SolidColorBrush(Color.FromArgb(0xC0, 0x00, 0x78, 0xD4));

        public int MaxLines { get; set; } = 1000;

        public SearchableLogPanel()
        {
            InitializeComponent();

            LogBox.Document = new FlowDocument(para)
            {
                PagePadding = new Thickness(0),
                FontFamily = LogBox.FontFamily,
                FontSize = LogBox.FontSize,
                Background = Brushes.Transparent
            };

            debounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };

            debounceTimer.Tick += (_, __) =>
            {
                debounceTimer.Stop();
                RebuildMatches();
            };

            repaintTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };

            repaintTimer.Tick += (_, __) =>
            {
                repaintTimer.Stop();
                PaintVisible();
            };

            Loaded += (_, __) =>
            {
                HookScrollViewer();
            };

            UpdateMatchLabel();
        }

        public void AppendLog(string line, bool addNewline = true, bool autoScrollIfAtBottom = true)
        {
            if (line == null) return;

            bool atBottom = IsAtBottom();

            string text = addNewline ? line + Environment.NewLine : line;

            para.Inlines.Add(new Run(text));

            logVersion++;
            isMatchDirty = true;

            TrimToMaxLines();

            RebuildMatches();

            if (autoScrollIfAtBottom && atBottom)
                ScrollToEnd();

            ScheduleRepaint();
        }

        public void Clear()
        {
            para.Inlines.Clear();
            matchSpans.Clear();
            highlightedRanges.Clear();

            cachedFullText = "";

            logVersion++;
            isMatchDirty = false;
            paintedVersion = -1;

            isSearchTextDirty = false;
            currentMatchIndex = -1;

            UpdateMatchLabel();
        }

        public string GetAllText()
        {
            return new TextRange(
                LogBox.Document.ContentStart,
                LogBox.Document.ContentEnd
            ).Text;
        }

        private void ScheduleRebuild()
        {
            debounceTimer.Stop();
            debounceTimer.Start();
        }

        private void RebuildMatches()
        {
            Stopwatch sw = Stopwatch.StartNew();
            matchSpans.Clear();

            if (isSearchTextDirty) currentMatchIndex = -1;

            string q = (SearchText ?? "").Trim();

            if (string.IsNullOrEmpty(q))
            {
                UpdateMatchLabel();
                RemoveAllHighlights();
                return;
            }

            cachedFullText = GetAllText();

            var regex = new Regex(
                Regex.Escape(q),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
            );

            MatchCollection matches = regex.Matches(cachedFullText);


            foreach (Match m in matches)
            {
                if (!m.Success) continue;

                matchSpans.Add((m.Index, m.Length));
            }

            if (isSearchTextDirty && matchSpans.Count > 0)
                currentMatchIndex = 0;
            else if (currentMatchIndex >= matchSpans.Count)
                currentMatchIndex = matchSpans.Count - 1;

            isMatchDirty = false;
            isSearchTextDirty = false;

            UpdateMatchLabel();
            ScheduleRepaint(true);
            Debug.WriteLine($"RebuildMatches ms: {sw.ElapsedMilliseconds}");
        }

        private void ScheduleRepaint(bool now = false)
        {
            if (now)
            {
                repaintTimer.Stop();
                if (isMatchDirty)
                {
                    RebuildMatches();
                }
                PaintVisible();
            }
            else
            {
                if (repaintTimer.IsEnabled) return;
                repaintTimer.Stop();
                repaintTimer.Start();
            }
        }

        private void PaintVisible()
        {
            if (matchSpans.Count == 0) return;
            if (isMatchDirty) return;

            if (paintedVersion != logVersion)
            {
                paintedVersion = logVersion;
            }

            Stopwatch sw = Stopwatch.StartNew();
            TextPointer start;
            TextPointer end;
            try
            {
                start = LogBox.GetPositionFromPoint(new Point(0, 0), true);
                end = LogBox.GetPositionFromPoint(new Point(LogBox.ActualWidth, LogBox.ActualHeight), true);
            }
            catch
            {
                return;
            }

            start ??= LogBox.Document.ContentStart;
            end ??= LogBox.Document.ContentEnd;

            int padding = 100; // Extra padding to catch partially visible matches
            start = GetTextPointerAtCharOffsetBackward(start, padding) ?? LogBox.Document.ContentStart;
            end = GetTextPointerAtCharOffset(end, padding) ?? LogBox.Document.ContentEnd;

            RemoveAllHighlights();

            if (NextMatchPointer(start) is BoundInfo boundInfo)
            {
                PaintHighlight(boundInfo.matchStart,
                    boundInfo.matchEnd,
                    boundInfo.matchIndex == currentMatchIndex
                );

                while (NextMatchPointer(boundInfo) is BoundInfo next)
                {
                    if (next.matchStart.CompareTo(end) > 0) break;

                    PaintHighlight(next.matchStart,
                        next.matchEnd,
                        next.matchIndex == currentMatchIndex
                    );

                    boundInfo = next;
                }
            }
            Debug.WriteLine($"PaintVisible ms: {sw.ElapsedMilliseconds}");
        }
        private void RemoveAllHighlights()
        {
            foreach (var range in highlightedRanges)
            {
                range.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
            }
            highlightedRanges.Clear();
        }

        private void RemoveHighlight(TextRange range)
        {
            range.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
            highlightedRanges.Remove(range);
        }

        private void PaintHighlight(TextPointer from, TextPointer to, bool isCurrent)
        {
            TextRange range = new(from, to);
            range.ApplyPropertyValue(
                TextElement.BackgroundProperty,
                isCurrent
                    ? CurrentMatchHighlightBrush
                    : MatchHighlightBrush
            );
            highlightedRanges.Add(range);
        }

        private TextRange CreateRange(int index, int length)
        {
            var start = GetPointerAt(index);
            var end = GetPointerAt(index + length);

            if (start == null || end == null)
                return null;

            return new TextRange(start, end);
        }

        private TextPointer GetPointerAt(int offset)
        {
            int remaining = offset;

            TextPointer p = LogBox.Document.ContentStart;

            while (p != null)
            {
                if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    string run = p.GetTextInRun(LogicalDirection.Forward);

                    if (run.Length >= remaining)
                        return p.GetPositionAtOffset(remaining);

                    remaining -= run.Length;

                    p = p.GetPositionAtOffset(run.Length);
                }
                else
                {
                    p = p.GetNextContextPosition(LogicalDirection.Forward);
                }

                if (p != null && p.CompareTo(p.DocumentEnd) >= 0)
                    return p.DocumentEnd;
            }

            return null;
        }

        private int LowerBound(int index)
        {
            int lo = 0;
            int hi = matchSpans.Count;

            while (lo < hi)
            {
                int mid = (lo + hi) / 2;

                if (matchSpans[mid].Index < index)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            return Math.Max(0, lo - 1);
        }

        private record BoundInfo(int matchIndex, TextPointer matchStart, TextPointer matchEnd);

        private BoundInfo NextMatchPointer(BoundInfo boundInfo)
        {
            int index = boundInfo.matchIndex;
            if (index >= matchSpans.Count)
                return null;
            TextPointer p = boundInfo.matchStart;
            while (p != null)
            {
                if (p.CompareTo(boundInfo.matchStart) > 0)
                    break;
                index++;
                if (index >= matchSpans.Count)
                    return null;
                int toNext = matchSpans[index].Index - matchSpans[index - 1].Index;
                p = GetTextPointerAtCharOffset(p, toNext);
            }
            if (index >= matchSpans.Count)
                return null;
            return new(index, p, GetTextPointerAtCharOffset(p, matchSpans[index].Length));
        }

        private BoundInfo NextMatchPointer(TextPointer pointer)
        {
            int index = 0;
            TextPointer p = LogBox.Document.ContentStart;

            p = GetTextPointerAtCharOffset(p, matchSpans[index].Index);
            while (p != null)
            {
                if (p.CompareTo(pointer) >= 0)
                    break;
                index++;
                if (index >= matchSpans.Count)
                    return null;
                int toNext = matchSpans[index].Index - matchSpans[index - 1].Index;
                p = GetTextPointerAtCharOffset(p, toNext);
            }
            return new(index, p, GetTextPointerAtCharOffset(p, matchSpans[index].Length));
        }

        private void Next()
        {
            if (matchSpans.Count == 0)
                return;

            currentMatchIndex = (currentMatchIndex + 1) % matchSpans.Count;

            UpdateMatchLabel();
            ScrollToCurrent();
            ScheduleRepaint(true);
        }

        private void Prev()
        {
            if (matchSpans.Count == 0)
                return;

            currentMatchIndex =
                (currentMatchIndex - 1 + matchSpans.Count) % matchSpans.Count;

            UpdateMatchLabel();
            ScrollToCurrent();
            ScheduleRepaint(true);
        }

        private void ScrollToCurrent()
        {
            var cur = matchSpans[currentMatchIndex];

            var docStart = LogBox.Document.ContentStart;
            var start = GetTextPointerAtCharOffset(docStart, cur.Index);
            //var end = GetTextPointerAtCharOffset(docStart, cur.Index + cur.Length);

            //LogBox.Selection.Select(start, end);
            //LogBox.Focus();
            LogBox.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var rect = start.GetCharacterRect(LogicalDirection.Forward);
                    LogBox.ScrollToVerticalOffset(Math.Max(0, LogBox.VerticalOffset + rect.Top - LogBox.ActualHeight + 40));
                    //cur.Start.Paragraph?.BringIntoView();
                }
                catch { /* ignore */ }
            }, DispatcherPriority.Background);
        }

        private void UpdateMatchLabel()
        {
            if (matchSpans.Count == 0)
                MatchLabel = "0/0";
            else
                MatchLabel = $"{currentMatchIndex + 1}/{matchSpans.Count}";
        }

        private void Prev_Click(object sender, RoutedEventArgs e) => Prev();
        private void Next_Click(object sender, RoutedEventArgs e) => Next();
        private void Clear_Click(object sender, RoutedEventArgs e) => Clear();

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                    Prev();
                else
                    Next();

                e.Handled = true;
            }
        }

        private void ScrollToEnd()
        {
            LogBox.Dispatcher.InvokeAsync(
                () => LogBox.ScrollToEnd(),
                DispatcherPriority.Background
            );
        }

        private bool IsAtBottom()
        {
            var sv = scrollViewer;

            if (sv == null)
                return true;

            return sv.VerticalOffset >= sv.ScrollableHeight - 1;
        }

        private void TrimToMaxLines()
        {
            if (MaxLines <= 0)
                return;

            string text = GetAllText();
            int lines = 1;
            foreach (char c in text)
                if (c == '\n')
                    lines++;

            if (lines <= MaxLines)
                return;

            int remove = lines - MaxLines;
            var start = LogBox.Document.ContentStart;
            var end = GetPointerAtLine(start, remove);

            if (end != null)
                new TextRange(start, end).Text = "";
        }

        private static TextPointer GetTextPointerAtCharOffset(TextPointer from, int charOffset)
        {
            int remaining = charOffset;
            TextPointer p = from;

            while (p != null)
            {
                if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    string textRun = p.GetTextInRun(LogicalDirection.Forward);
                    if (textRun.Length >= remaining)
                        return p.GetPositionAtOffset(remaining, LogicalDirection.Forward);

                    remaining -= textRun.Length;
                    p = p.GetPositionAtOffset(textRun.Length, LogicalDirection.Forward);
                }
                else
                {
                    p = p.GetNextContextPosition(LogicalDirection.Forward);
                }

                if (p != null && p.CompareTo(p.DocumentEnd) >= 0)
                    return p.DocumentEnd;
            }

            return null;
        }
        private static TextPointer GetTextPointerAtCharOffsetBackward(TextPointer start, int charOffset)
        {
            int remaining = charOffset;
            TextPointer p = start;

            while (p != null)
            {
                if (p.GetPointerContext(LogicalDirection.Backward) == TextPointerContext.Text)
                {
                    string textRun = p.GetTextInRun(LogicalDirection.Backward);

                    if (textRun.Length >= remaining)
                        return p.GetPositionAtOffset(-remaining, LogicalDirection.Backward);

                    remaining -= textRun.Length;
                    p = p.GetPositionAtOffset(-textRun.Length, LogicalDirection.Backward);
                }
                else
                {
                    p = p.GetNextContextPosition(LogicalDirection.Backward);
                }

                if (p != null && p.CompareTo(p.DocumentStart) <= 0)
                    return p.DocumentStart;
            }

            return null;
        }

        private static TextPointer GetPointerAtLine(TextPointer start, int lines)
        {
            int remaining = lines;

            TextPointer p = start;

            while (p != null)
            {
                if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    string run = p.GetTextInRun(LogicalDirection.Forward);

                    for (int i = 0; i < run.Length; i++)
                    {
                        if (run[i] == '\n')
                        {
                            remaining--;

                            if (remaining <= 0)
                                return p.GetPositionAtOffset(i + 1);
                        }
                    }

                    p = p.GetPositionAtOffset(run.Length);
                }
                else
                {
                    p = p.GetNextContextPosition(LogicalDirection.Forward);
                }
            }

            return null;
        }

        #region Initialization Helpers

        private void HookScrollViewer()
        {
            scrollViewer = FindScrollViewer(LogBox);

            if (scrollViewer != null)
                scrollViewer.ScrollChanged += (_, __) => ScheduleRepaint();
        }

        private static ScrollViewer? FindScrollViewer(DependencyObject root)
        {
            if (root is ScrollViewer sv)
                return sv;

            int count = VisualTreeHelper.GetChildrenCount(root);

            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);

                var found = FindScrollViewer(child);

                if (found != null)
                    return found;
            }

            return null;
        }

        private void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(name)
            );
        }

        #endregion
    }
}