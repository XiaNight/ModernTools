using Base.Pages;
using Base.Services;
using Base.Services.APIService;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Airoha;
public partial class AirohaStopper : PageBase
{
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(System.IntPtr hObject);

    public override string PageName => "Airoha Stopper";

    public AirohaStopper()
    {
        InitializeComponent();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        // Populate the automation tree when the page is enabled
        RefreshAutomationTree();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshAutomationTree();
    }

    private void RefreshAutomationTree()
    {
        AutomationTreeView.Items.Clear();
        StatusText.Text = "Searching for process...";

        try
        {
            var procs = Process.GetProcessesByName("Airoha.Tool.Kit");
            if (procs == null || procs.Length == 0)
            {
                StatusText.Text = "Process 'Airoha.Tool.Kit' not found.";
                return;
            }

            var proc = procs.First();
            Base.Services.Debug.Log("Found process: " + proc.ProcessName);
            StatusText.Text = $"Found process: {proc.ProcessName} (PID {proc.Id})";

            var root = AutomationElement.FromHandle(proc.MainWindowHandle);
            if (root == null)
            {
                StatusText.Text = "Could not get AutomationElement for main window.";
                return;
            }

            var rootItem = BuildTreeItem(root, new List<int>());
            AutomationTreeView.Items.Add(rootItem);
            rootItem.IsExpanded = true;

            StatusText.Text = "Loaded automation tree.";
        }
        catch (System.Exception ex)
        {
            Base.Services.Debug.Log("Error while refreshing automation tree: ", ex.Message, ex.StackTrace);
            StatusText.Text = "Error: " + ex.Message;
        }
    }

    [POST]
    public static bool InvokeButtonByPath(string pathString)
    {
        try
        {
            if (!TryParsePathString(pathString, out int? pid, out int[] path))
                return false;

            Process proc = null;
            if (pid.HasValue)
            {
                try { proc = Process.GetProcessById(pid.Value); }
                catch { return false; }
            }
            else
            {
                // fallback: try find process by known name
                var procs = Process.GetProcessesByName("Airoha.Tool.Kit");
                if (procs == null || procs.Length == 0) return false;
                proc = procs.First();
            }

            var root = AutomationElement.FromHandle(proc.MainWindowHandle);
            if (root == null) return false;

            var target = LocateElementByPath(root, path);
            if (target == null) return false;

            if (target.TryGetCurrentPattern(InvokePattern.Pattern, out var patternObj) && patternObj is InvokePattern ip)
            {
                ip.Invoke();
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    // BuildTreeItem now carries an index-path from the root. The path is a list of child indices
    // representing how to locate the element later.
    private TreeViewItem BuildTreeItem(AutomationElement element, List<int> path)
    {
        string header;
        bool supportsInvoke = false;

        try
        {
            var controlType = element.Current.ControlType?.ProgrammaticName ?? "[UnknownControl]";
            var name = string.IsNullOrEmpty(element.Current.Name) ? "(no name)" : element.Current.Name;
            var aid = string.IsNullOrEmpty(element.Current.AutomationId) ? "(no id)" : element.Current.AutomationId;
            var cls = string.IsNullOrEmpty(element.Current.ClassName) ? "(no class)" : element.Current.ClassName;
            header = $"{controlType} | Name='{name}' | AutomationId='{aid}' | Class='{cls}'";

            // cheap check for invoke support
            try { supportsInvoke = element.TryGetCurrentPattern(InvokePattern.Pattern, out _); } catch { supportsInvoke = false; }
        }
        catch (ElementNotAvailableException)
        {
            header = "[ElementNotAvailable]";
        }
        catch (System.Exception ex)
        {
            header = "[Error reading element] " + ex.Message;
        }

        // Create header UI with optional buttons
        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
        headerPanel.Children.Add(new TextBlock { Text = header, VerticalAlignment = VerticalAlignment.Center });

        // Spacer
        headerPanel.Children.Add(new FrameworkElement { Width = 8 });

        // If element looks like a button or supports InvokePattern, show an Invoke control
        if (supportsInvoke || (element.Current.ControlType == ControlType.Button))
        {
            var invokeBtn = new Button
            {
                Content = "Invoke",
                Margin = new Thickness(6, 0, 0, 0),
                Padding = new Thickness(6, 2, 6, 2),
                Tag = path.ToArray()
            };
            invokeBtn.Click += InvokeTargetButton_Click;
            headerPanel.Children.Add(invokeBtn);
        }

        // Details button for every element
        var detailsBtn = new Button
        {
            Content = "Details",
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(6, 2, 6, 2),
            Tag = path.ToArray()
        };
        detailsBtn.Click += DetailsButton_Click;
        headerPanel.Children.Add(detailsBtn);

        var item = new TreeViewItem { Header = headerPanel, Tag = path.ToArray() };

        try
        {
            var walker = TreeWalker.ControlViewWalker;
            var child = walker.GetFirstChild(element);
            int childIndex = 0;
            while (child != null)
            {
                try
                {
                    // create new path for child (copy + index)
                    var childPath = new List<int>(path) { childIndex };
                    var childItem = BuildTreeItem(child, childPath);
                    item.Items.Add(childItem);
                }
                catch (ElementNotAvailableException)
                {
                    // skip
                }

                child = walker.GetNextSibling(child);
                childIndex++;
            }
        }
        catch (ElementNotAvailableException)
        {
            // element disappeared while walking
        }
        catch (System.Exception ex)
        {
            Base.Services.Debug.Log("Error while walking automation tree: ", ex.Message, ex.StackTrace);
        }

        return item;
    }

    // Locate an element by the index-path saved earlier. Returns null if not found.
    private static AutomationElement? LocateElementByPath(AutomationElement root, int[] path)
    {
        try
        {
            var walker = TreeWalker.ControlViewWalker;
            AutomationElement current = root;

            foreach (var idx in path)
            {
                var child = walker.GetFirstChild(current);
                int i = 0;
                while (child != null && i < idx)
                {
                    child = walker.GetNextSibling(child);
                    i++;
                }

                if (child == null) return null;
                current = child;
            }

            return current;
        }
        catch
        {
            return null;
        }
    }

    private void InvokeTargetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not int[] path) return;

        try
        {
            var procs = Process.GetProcessesByName("Airoha.Tool.Kit");
            if (procs == null || procs.Length == 0)
            {
                MessageBox.Show("Target process not found.", "Invoke", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var proc = procs.First();
            var root = AutomationElement.FromHandle(proc.MainWindowHandle);
            if (root == null)
            {
                MessageBox.Show("Could not get target main window.", "Invoke", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var target = LocateElementByPath(root, path);
            if (target == null)
            {
                MessageBox.Show("Element not found in target app (outdated path). Aborting.", "Invoke", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (target.TryGetCurrentPattern(InvokePattern.Pattern, out var patternObj) && patternObj is InvokePattern ip)
            {
                ip.Invoke();
            }
            else
            {
                MessageBox.Show("Element does not support InvokePattern. Aborting.", "Invoke", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (ElementNotAvailableException)
        {
            MessageBox.Show("Element became unavailable.", "Invoke", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (System.Exception ex)
        {
            Base.Services.Debug.Log("Invoke failed: ", ex.Message, ex.StackTrace);
            MessageBox.Show("Invoke failed: " + ex.Message, "Invoke", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DetailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not int[] path) return;

        try
        {
            var procs = Process.GetProcessesByName("Airoha.Tool.Kit");
            if (procs == null || procs.Length == 0)
            {
                MessageBox.Show("Target process not found.", "Details", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var proc = procs.First();
            var root = AutomationElement.FromHandle(proc.MainWindowHandle);
            if (root == null)
            {
                MessageBox.Show("Could not get target main window.", "Details", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var target = LocateElementByPath(root, path);
            if (target == null)
            {
                MessageBox.Show("Element not found in target app (outdated path).", "Details", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Build details string
            string details;
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Name: " + target.Current.Name);
                sb.AppendLine("AutomationId: " + target.Current.AutomationId);
                sb.AppendLine("Class: " + target.Current.ClassName);
                sb.AppendLine("ControlType: " + (target.Current.ControlType?.ProgrammaticName ?? "(null)"));
                sb.AppendLine("IsEnabled: " + target.Current.IsEnabled);
                sb.AppendLine("IsKeyboardFocusable: " + target.Current.IsKeyboardFocusable);
                try { sb.AppendLine("BoundingRectangle: " + target.Current.BoundingRectangle.ToString()); } catch { }

                try
                {
                    var patterns = target.GetSupportedPatterns();
                    if (patterns != null && patterns.Length > 0)
                    {
                        sb.AppendLine("Supported Patterns:");
                        foreach (var p in patterns) sb.AppendLine(" - " + p.ProgrammaticName);
                    }
                }
                catch { }

                // quick child count
                try
                {
                    var walker = TreeWalker.ControlViewWalker;
                    int count = 0;
                    var child = walker.GetFirstChild(target);
                    while (child != null)
                    {
                        count++;
                        child = walker.GetNextSibling(child);
                    }
                    sb.AppendLine("Child count: " + count);
                }
                catch { }

                details = sb.ToString();
            }
            catch (ElementNotAvailableException)
            {
                details = "Element became unavailable.";
            }

            // Prepare a stable path string (uia://process/pid/path/0,1,2)
            string pathString = BuildPathString(proc, path);

            // Build the details window with copy and optional image view
            var panel = new StackPanel { Margin = new Thickness(8) };
            var detailsBox = new TextBox
            {
                Text = details + "\nPath: " + pathString,
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.Wrap
            };
            detailsBox.Height = 300;
            panel.Children.Add(detailsBox);

            var buttonsPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

            var copyBtn = new Button { Content = "Copy Path", Margin = new Thickness(6, 4, 0, 0), Padding = new Thickness(8, 4, 8, 4) };
            copyBtn.Click += (_, __) =>
            {
                try { Clipboard.SetText(pathString); } catch { }
            };
            buttonsPanel.Children.Add(copyBtn);

            bool isImage = false;
            try
            {
                isImage = target.Current.ControlType == ControlType.Image || (target.Current.ClassName?.ToLowerInvariant().Contains("image") == true);
            }
            catch { }

            if (isImage)
            {
                var imgBtn = new Button { Content = "Show Image", Margin = new Thickness(6, 4, 0, 0), Padding = new Thickness(8, 4, 8, 4) };
                imgBtn.Click += (_, __) => ShowImageFromElement(target);
                buttonsPanel.Children.Add(imgBtn);
            }

            var closeBtn = new Button { Content = "Close", Margin = new Thickness(6, 4, 0, 0), Padding = new Thickness(8, 4, 8, 4) };
            closeBtn.Click += (_, __) => ((Window)closeBtn.Tag).Close();
            buttonsPanel.Children.Add(closeBtn);

            panel.Children.Add(buttonsPanel);

            var win = new Window
            {
                Title = "Element Details",
                Content = panel,
                Width = 700,
                Height = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Application.Current?.MainWindow
            };

            // wire close button tag
            closeBtn.Tag = win;

            win.ShowDialog();
        }
        catch (System.Exception ex)
        {
            Base.Services.Debug.Log("Details failed: ", ex.Message, ex.StackTrace);
            MessageBox.Show("Details failed: " + ex.Message, "Details", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private string BuildPathString(Process proc, int[] path)
    {
        // Encode process info + index path. This format can be extended if needed.
        var pathPart = string.Join(',', path);
        var procName = proc.ProcessName.Replace(' ', '_');
        return $"uia://{procName}/{proc.Id}/path/{pathPart}";
    }

    private void ShowImageFromElement(AutomationElement target)
    {
        try
        {
            System.Windows.Rect rect;
            try { rect = target.Current.BoundingRectangle; }
            catch { rect = System.Windows.Rect.Empty; }

            if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
            {
                MessageBox.Show("Cannot determine element bounding rectangle.", "Show Image", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Capture screen area
            int x = (int)rect.X;
            int y = (int)rect.Y;
            int w = Math.Max(1, (int)rect.Width);
            int h = Math.Max(1, (int)rect.Height);

            using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h), System.Drawing.CopyPixelOperation.SourceCopy);
            }

            var hBitmap = bmp.GetHbitmap();
            try
            {
                var source = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, System.IntPtr.Zero, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

                var img = new System.Windows.Controls.Image { Source = source, Stretch = System.Windows.Media.Stretch.Uniform };
                var win = new Window
                {
                    Title = "Element Image",
                    Content = new ScrollViewer { Content = img },
                    Width = Math.Min(1000, w + 40),
                    Height = Math.Min(800, h + 40),
                    Owner = Application.Current?.MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                win.Show();
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
        catch (System.Exception ex)
        {
            Base.Services.Debug.Log("ShowImage failed: ", ex.Message, ex.StackTrace);
            MessageBox.Show("Show Image failed: " + ex.Message, "Show Image", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Helper to parse path strings
    private static bool TryParsePathString(string input, out int? pid, out int[] path)
    {
        pid = null;
        path = null;
        if (string.IsNullOrWhiteSpace(input)) return false;

        try
        {
            if (input.StartsWith("uia://"))
            {
                var rest = input.Substring("uia://".Length);
                var parts = rest.Split('/');
                // expect: {procName}/{pid}/path/{0,1,2}
                if (parts.Length >= 4 && parts[2] == "path")
                {
                    if (!int.TryParse(parts[1], out var pidVal)) return false;
                    pid = pidVal;
                    path = parts[3].Split(',', System.StringSplitOptions.RemoveEmptyEntries).Select(s => int.Parse(s)).ToArray();
                    return true;
                }
                return false;
            }

            // fallback: comma-separated indices
            var items = input.Split(',', System.StringSplitOptions.RemoveEmptyEntries);
            path = items.Select(s => int.Parse(s.Trim())).ToArray();
            return true;
        }
        catch
        {
            pid = null;
            path = null;
            return false;
        }
    }
}
