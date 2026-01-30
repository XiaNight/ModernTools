using Base.Core;
using Base.Pages;
using Base.Services;
using ModernWpf;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;

namespace Base;

using Components;
using System.Windows.Threading;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private bool isNavExpanded = true;
    private bool isLogVisible = false;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindowLoadingAsync;

        WindowChrome.SetIsHitTestVisibleInChrome(MenuBar, true);
        WindowChrome.SetIsHitTestVisibleInChrome(TitleBarControls, true);
    }

    private void ToggleNav_Click(object sender, RoutedEventArgs e)
    {
        isNavExpanded = !isNavExpanded;

        if (isNavExpanded) NavTabsManager.Expand();
        else NavTabsManager.Collapse();

        // Toggle visibility of text labels

        LogMessage($"Navigation panel {(isNavExpanded ? "expanded" : "contracted")}.");
    }

    private void ToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        var currentTheme = ThemeManager.Current.ApplicationTheme;
        ThemeManager.Current.ApplicationTheme = currentTheme == ApplicationTheme.Light
            ? ApplicationTheme.Dark
            : ApplicationTheme.Light;

        ThemeManager.Current.ActualApplicationThemeChanged += (s, ev) =>
        {
            foreach (WpfBehaviour wpfObject in registeredWpfObjects)
            {
                if(wpfObject.IsEnabled)
                    wpfObject?.ThemeChanged();
            }
        };

        LogMessage($"Theme changed to {ThemeManager.Current.ApplicationTheme}.");
    }

    private void ToggleLog_Click(object sender, RoutedEventArgs e)
    {
        isLogVisible = !isLogVisible;
        LogPanel.Visibility = isLogVisible ? Visibility.Visible : Visibility.Collapsed;

        if (isLogVisible)
        {
            LogMessage("Log window shown.");
        }
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        LogTextBox.Clear();
        LogMessage("Log cleared.");
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void LogMessage(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        LogTextBox.AppendText($"[{timestamp}] {message}\n");
        LogTextBox.ScrollToEnd();
    }

    #region WPF public

    private async void MainWindowLoadingAsync(object sender, RoutedEventArgs e)
    {
        _ = Task.Run(MainWindowLoading);
    }

    private void MainWindowLoading()
    {
        LoadingCover.AutoFinish((t) =>
        {
            LoadingBlur.Radius = Math.Max(0, LoadingBlur.Radius - t * 20);
        });

        Task.Run(() => PreloadWpfBehaviourSingletons(AppDomain.CurrentDomain.GetAssemblies()));
        Task.Run(() => BuildNavigationTabs(AppDomain.CurrentDomain.GetAssemblies()));

        Dispatcher.InvokeAsync(() =>
        {
            _ = DeviceSelection.Instance.Refresh();
            DeviceSelection.Instance.OnActiveInterfaceConnected += ReloadPage;
        });
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // Fired before the window actually closes.
        // You can cancel shutdown by setting e.Cancel = true.
        Debug.Log("MainWindow is closing");
        foreach (WpfBehaviour wpfObject in registeredWpfObjects)
        {
            wpfObject?.OnApplicationQuit(e);
        }
        foreach (WpfBehaviour wpfObject in registeredWpfObjects)
        {
            wpfObject?.Disable();
        }
    }

    private void Window_Closed(object sender, EventArgs e)
    {
        // Fired after the window has closed.
        Debug.Log("MainWindow has closed – app will now exit");
    }

    private async void PreloadWpfBehaviourSingletons(IEnumerable<Assembly> assemblies)
    {
        var openBase = typeof(WpfBehaviourSingleton<>);

        Type[] allTypes = assemblies.SelectMany(SafeGetTypes)
            .Where(t => t != null)
            .Where(t => t.IsClass && !t.IsAbstract)
            .ToArray();

        await Task.Delay(100);

        var jobs = new Dictionary<Type, LoadingCover.LoadingJob>(allTypes.Length);
        foreach (var t in allTypes)
            jobs[t] = LoadingCover.RentJob(1f);

        foreach (Type t in allTypes)
        {
            // Match: class T : WpfBehaviourSingleton<T>
            if (!IsSelfReferencingSingleton(t, openBase))
            {
                jobs[t].Finish();
                continue;
            }
            
            await Dispatcher.InvokeAsync(() =>
            {
                // Force creation: WpfBehaviourSingleton<T>.Instance
                var closedBase = openBase.MakeGenericType(t);
                var prop = closedBase.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                _ = prop?.GetValue(null);
            }, DispatcherPriority.Background);

            jobs[t].Finish();
        }
    }

    private readonly Dictionary<IPageBase, NavigationButton> navPageMap = new();

    private static bool IsSelfReferencingSingleton(Type t, Type openBase)
    {
        for (var cur = t; cur != null && cur != typeof(object); cur = cur.BaseType!)
        {
            if (!cur.IsGenericType) continue;

            var def = cur.GetGenericTypeDefinition();
            if (def != openBase) continue;

            var arg = cur.GetGenericArguments()[0];
            return arg == t;
        }
        return false;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly a)
    {
        Type[] types;
        try { types = a.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(x => x != null).ToArray()!; }
        catch { types = Array.Empty<Type>(); }
        return types;
    }

    private void BuildNavigationTabs(IEnumerable<Assembly> assemblies)
    {
        // find all PageBase and none abstract
        Type[] allTypes = assemblies.SelectMany(SafeGetTypes)
            .Where(t => typeof(PageBase).IsAssignableFrom(t))
            .Where(t => !t.IsAbstract)
            .ToArray();

        var jobs = new Dictionary<Type, LoadingCover.LoadingJob>(allTypes.Length);
        foreach (var t in allTypes)
            jobs[t] = LoadingCover.RentJob(1f);

        foreach (Type t in allTypes)
        {
            Dispatcher.InvokeAsync(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();

                PageBase newPage = (PageBase)Activator.CreateInstance(t);
                RegisterWpfObject(newPage);

                if(newPage.NavOrder >= 0)
                {
                    NavigationButton newTab;
                    if (newPage.NavAlignment == PageBase.NavigationAlignment.Front)
                    {
                        newTab = NavTabsManager.AddTop(newPage.PageName, newPage.Glyph, newPage.SecondaryGlyph, newPage.NavOrder);
                        newTab.SecondaryLabel.Text = newPage.ShortName;
                    }
                    else
                    {
                        newTab = NavTabsManager.AddBottom(newPage.PageName, newPage.Glyph, newPage.SecondaryGlyph, newPage.NavOrder);
                        newTab.SecondaryLabel.Text = newPage.ShortName;
                    }

                    newTab.OnClick += () => SelectPage(t);
                    navPageMap.Add(newPage, newTab);
                }

                sw.Stop();
                LogMessage($"[NavInit] {t.FullName} : {sw.Elapsed.TotalMilliseconds:0.###} ms");
                sw = null;
                jobs[t].Finish();
            }, DispatcherPriority.Background);
        }
    }

    public PageBase SelectPage(Type pageType)
    {
        if (!typeof(PageBase).IsAssignableFrom(pageType) || pageType.IsAbstract)
            return null;

        PageBase page = FindObjectOfType(pageType, true) as PageBase;

        if (currentPage != null) navPageMap[currentPage].SetHighlightedState(false);
        currentPage?.Disable();

        currentPage = page;
        AssignToolsMenu(currentPage);

        currentPage?.Enable();
        navPageMap[currentPage].SetHighlightedState(true);

        ContentFrame.Children.Clear();
        ContentFrame.Children.Add(page);
        return page;
    }
    private void ShowAbout(object sender, RoutedEventArgs e)
    {
        AboutWindow.Show(this);
    }

    #endregion

    #region WpfBehaviour Assigning

    private readonly List<WpfBehaviour> registeredWpfObjects = new();
    private readonly Queue<WpfBehaviour> newWpfObjects = new();
    private bool wpfRegisterDispatched = false;
    private readonly object wpfRegisterLock = new();
    public void RegisterWpfObject(WpfBehaviour wpfObject)
    {
        lock (wpfRegisterLock)
        {
            newWpfObjects.Enqueue(wpfObject);
            if (wpfRegisterDispatched) return;
            wpfRegisterDispatched = true;

            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                lock (wpfRegisterLock)
                {
                    while (newWpfObjects.Count > 0)
                    {
                        WpfBehaviour wpfObject = newWpfObjects.Dequeue();
                        if (wpfObject == null) continue;
                        if (registeredWpfObjects.Contains(wpfObject)) continue;
                        registeredWpfObjects.Add(wpfObject);

                        wpfObject.Awake();
                    }
                    newWpfObjects.Clear();
                    wpfRegisterDispatched = false;
                }
            });
        }
    }

    public WpfBehaviour FindObjectOfType(Type type, bool findInactive = false)
    {
        foreach (var wpfObject in registeredWpfObjects)
        {
            if (type.IsInstanceOfType(wpfObject))
            {
                if (!findInactive && !wpfObject.IsEnabled) continue;
                return wpfObject;
            }
        }
        return null;
    }

    public T FindObjectOfType<T>(bool findInactive = false) where T : WpfBehaviour
    {
        foreach (var wpfObject in registeredWpfObjects)
        {
            if (wpfObject is T tObject)
            {
                if (!findInactive && !tObject.IsEnabled) continue;
                return tObject;
            }
        }
        return null;
    }

    #endregion

    #region Custom Functions

    private IPageBase currentPage = null;
    private List<IPageBase> pages;

    public void ReloadPage()
    {
        currentPage?.Disable();
        //currentPage = pages[MainTabControl.SelectedIndex];
        currentPage?.Enable();
    }

    public void SetTabsEnabled(bool enabled)
    {
        //foreach (TabItem item in MainTabControl.Items)
        //{
        //    if (item.Content is Grid grid)
        //    {
        //        grid.IsEnabled = enabled;
        //    }
        //}
    }

    public void SelectTabIndex(int index)
    {
        //if (index >= 0 && index < MainTabControl.Items.Count)
        //{
        //    MainTabControl.SelectedIndex = index;
        //}
    }

    public static string GetOutputFolder()
    {
        string exeDir = AppContext.BaseDirectory;
        string outputDir = Path.Combine(exeDir, "Output");

        Directory.CreateDirectory(outputDir); // Safe even if it exists

        return outputDir;
    }

    public string GetToolFolder(params string[] subFolders)
    {
        string exeDir = AppContext.BaseDirectory;
        return Path.Combine(exeDir, "Tools", Path.Combine(subFolders));
    }

    public static string GetConfigFolder(params string[] subFolders)
    {
        string exeDir = AppContext.BaseDirectory;
        return Path.Combine(exeDir, "Config", Path.Combine(subFolders));
    }

    public static readonly string appName = Util.GetAssemblyAttribute<AssemblyProductAttribute>(a => a.Product);
    public static readonly string company = Util.GetAssemblyAttribute<AssemblyCompanyAttribute>(a => a.Company);
    public static readonly string version = Util.GetAssemblyAttribute<AssemblyInformationalVersionAttribute>(Application.ResourceAssembly, a => a.InformationalVersion);
    public static readonly string toolBaseVersion = Util.GetAssemblyAttribute<AssemblyInformationalVersionAttribute>(Assembly.GetExecutingAssembly(), a => a.InformationalVersion);
    public static readonly string fileVersion = Util.GetAssemblyAttribute<AssemblyFileVersionAttribute>(a => a.Version);
    public static readonly string assemblyVersion = Util.GetAssemblyAttribute<AssemblyVersionAttribute>(a => a.Version);
    public static readonly string copyright = Util.GetAssemblyAttribute<AssemblyCopyrightAttribute>(a => a.Copyright);
    public static readonly string description = Util.GetAssemblyAttribute<AssemblyDescriptionAttribute>(a => a.Description);
    public static readonly string title = Util.GetAssemblyAttribute<AssemblyTitleAttribute>(a => a.Title);
    public static readonly string trademark = Util.GetAssemblyAttribute<AssemblyTrademarkAttribute>(a => a.Trademark);
    public static readonly string applicationIcon = FindIconPath();

    private static string FindIconPath()
    {
        string exeDir = AppContext.BaseDirectory;

        // Look for *.ico files in the executable directory
        var icoFiles = Directory.GetFiles(exeDir, "*.ico", SearchOption.TopDirectoryOnly);

        // Return the first one found, or null if none
        return icoFiles.FirstOrDefault();
    }

    public void ApplyTheme(bool dark)
    {
        var palette = new ResourceDictionary
        {
            Source = new Uri(dark
                ? "base;component/Themes/Palette.Dark.xaml"
                : "base;component/Themes/Palette.Light.xaml", UriKind.Relative)
        };

        if (palette != null)
        {
            foreach (DictionaryEntry e in palette)
            {
                Resources[e.Key] = e.Value; // overwrite keyed entries
                Application.Current.Resources[e.Key] = e.Value;
            }
        }
    }

    public static void InstantiateWpfBehaviourSingletons()
    {
        TouchAllWpfBehaviourSingletons([Application.ResourceAssembly, Assembly.GetExecutingAssembly()]);
    }

    private static void TouchAllWpfBehaviourSingletons(params Assembly[] assemblies)
    {
        assemblies ??= AppDomain.CurrentDomain.GetAssemblies();

        // Deduplicate assemblies
        assemblies = assemblies
            .Where(a => a != null)
            .GroupBy(a => a.FullName)
            .Select(g => g.First())
            .ToArray();

        foreach (var asm in assemblies)
        {
            Type[] types;
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray()!;
            }

            foreach (var t in types)
            {
                if (t == null || t.IsAbstract || t.IsGenericTypeDefinition)
                    continue;

                if (!typeof(PageBase).IsAssignableFrom(t))
                    continue;

                var instance = Activator.CreateInstance(t) as PageBase;
                instance?.Awake();
            }
        }
    }


    #endregion

    #region Menu Item

    private List<CommandBinding> bindedMenuItemCommands = new();

    public void AssignToolsMenu(object page)
    {
        ClearTools();
        RemoveAllCommandBindings();
        ToolsMenu.IsEnabled = false;
        ArgumentNullException.ThrowIfNull(page);

        var items = AppMenuItemAttribute.GetAppMenuItemRegestry(page);
        rootTools.Clear();
        foreach (var item in items)
        {
            AddTool(item.Path, item.StayOpen, item.Action, item.key, item.modifierKeys);
        }
        ToolsMenu.Items.Clear();
        foreach (var tool in rootTools.Values)
        {
            ToolsMenu.Items.Add(tool);
        }

        ToolsMenu.IsEnabled = ToolsMenu.Items.Count > 0;
    }

    public void ClearTools()
    {
        ToolsMenu.Items.Clear();
        rootTools.Clear();
    }

    public void AddTool(string path, bool stayOpen, Action callback, Key key, ModifierKeys modifierKeys = ModifierKeys.None)
    {
        if (string.IsNullOrWhiteSpace(path) || callback == null) return;

        string[] strings = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        ToolItem lastItem = null;
        Dictionary<string, ToolItem> level = rootTools;

        for (int i = 0; i < strings.Length; i++)
        {
            string s = strings[i];
            bool isEnd = (i == strings.Length - 1);
            if (string.IsNullOrWhiteSpace(s)) return;
            ToolItem newItem;

            if (isEnd)
            {
                if (level.ContainsKey(s)) return; // already exists, Currently no overwrite

                // Create the command and menu item
                RelayCommand relayCommand = new(path, typeof(ToolItem), key, modifierKeys, callback);
                newItem = new(s, stayOpen, relayCommand);

                // Bind the command
                CommandBinding commandbinding = new(relayCommand, relayCommand.OnExec, relayCommand.OnCan);
                AddCommandBinding(commandbinding);
            }
            else
            {
                s += "/"; // intermediate nodes have trailing slash
                if (level.ContainsKey(s))
                {
                    lastItem = level[s];
                    level = lastItem.SubTools;
                    continue;
                }

                newItem = new(s, stayOpen);
            }

            // Assign to parent
            if (lastItem != null) lastItem.AddSubTool(newItem);
            else rootTools.Add(s, newItem);

            lastItem = newItem;
            level = lastItem.SubTools;
        }
    }

    private void AddCommandBinding(CommandBinding binding)
    {
        if (binding == null) return;
        if (bindedMenuItemCommands.Contains(binding)) return;
        bindedMenuItemCommands.Add(binding);
        CommandBindings.Add(binding);
    }

    private void RemoveCommandBinding(CommandBinding binding)
    {
        if (binding == null) return;
        if (!bindedMenuItemCommands.Contains(binding)) return;
        bindedMenuItemCommands.Remove(binding);
        CommandBindings.Remove(binding);
    }

    private void RemoveAllCommandBindings()
    {
        foreach (var binding in bindedMenuItemCommands)
        {
            CommandBindings.Remove(binding);
        }
        bindedMenuItemCommands.Clear();
    }

    private readonly Dictionary<string, ToolItem> rootTools = new();

    public class ToolItem : MenuItem
    {
        public string Path { get; private set; }
        public Action Callback { get; private set; }
        public Dictionary<string, ToolItem> SubTools { get; private set; } = new();
        public bool IsEnd => Callback != null;

        public ToolItem(string path, bool stayOpen, ICommand command = null, string icon = null) : base()
        {
            Path = path;
            Header = path;
            Icon = icon;
            StaysOpenOnClick = stayOpen;

            if (command != null)
                Command = command;
        }

        public bool AddSubTool(ToolItem item)
        {
            if (item == null) return false;
            if (SubTools.ContainsKey(item.Path)) return false;
            SubTools[item.Path] = item;
            Items.Add(item);
            return true;
        }
    }

    public class RelayCommand : RoutedCommand
    {
        private readonly Action execute;
        private readonly Func<bool> canExecute;

        public RelayCommand(string name, Type ownerType, Key key, ModifierKeys modifiers, Action execute, Func<bool> canExecute = null)
            : base(name, ownerType, [new KeyGesture(key, modifiers)])
        {
            this.execute = execute;
            this.canExecute = canExecute;
        }

        public void OnExec(object sender, ExecutedRoutedEventArgs e)
        {
            execute();
        }

        public void OnCan(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = canExecute?.Invoke() ?? true;
        }
    }

    #endregion
}