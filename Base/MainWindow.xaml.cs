using Base.Core;
using Base.Pages;
using Base.Services;
using ModernWpf;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;

namespace Base;

using Components;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Interop;
using System.Windows.Threading;
using static Base.Components.VerticalTabsManager;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    private bool isNavExpanded = true;
    private bool isLogVisible = false;
    public event Action<PageBase, PageBase> OnPageChanged;

    private LayoutMode _currentLayoutMode = LayoutMode.Normal;
    public LayoutMode CurrentLayoutMode
    {
        get => _currentLayoutMode;
        private set
        {
            if (_currentLayoutMode == value) return;
            _currentLayoutMode = value;
            OnPropertyChanged();
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindowLoadingAsync;
        SizeChanged += OnSizeChanged;

        WindowChrome.SetIsHitTestVisibleInChrome(MenuBar, true);
        WindowChrome.SetIsHitTestVisibleInChrome(TitleBarControls, true);

        Debug.OnLog += LogMessage;
    }

    #region WndProc

    public event Action<IntPtr, int, IntPtr, IntPtr, bool> WindowMessageReceived;
    public nint Handle => new WindowInteropHelper(this).Handle;
    private void RegisterWndProc()
    {
        var hwndSource = HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        if (hwndSource != null)
            hwndSource.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        WindowMessageReceived?.Invoke(hwnd, msg, wParam, lParam, handled);
        return IntPtr.Zero;
    }

    #endregion

    #region WPF public

    private void ToggleNav_Click(object sender, RoutedEventArgs e)
    {
        isNavExpanded = !isNavExpanded;

        if (isNavExpanded) NavTabsManager.ExitCompactMode();
        else NavTabsManager.EnterCompactMode();
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
                if (wpfObject.IsEnabled)
                    wpfObject?.ThemeChanged();
            }
        };

        LocalAppDataStore.Instance.Set("Theme", ThemeManager.Current.ApplicationTheme);

        LogMessage($"Theme changed to {ThemeManager.Current.ApplicationTheme}.");
    }

    private void ToggleLog_Click(object sender, RoutedEventArgs e)
    {
        isLogVisible = !isLogVisible;
        LogPanel.Visibility = isLogVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        LogTextBox.Clear();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void MainWindowLoadingAsync(object sender, RoutedEventArgs e)
    {
        UpdateLayoutMode(ActualWidth);
        MainWindowLoading();
        RegisterWndProc();
    }

    private async void MainWindowLoading()
    {
        LoadPluginDLLs(GetPluginsFolder());

#if DEBUG
        LoadDLLsInFolder(AppContext.BaseDirectory);
#endif

        await PreloadWpfBehaviourSingletons(AppDomain.CurrentDomain.GetAssemblies());
        await BuildNavigationTabs(AppDomain.CurrentDomain.GetAssemblies());

        SelectTabIndex(0);
        DeviceSelection.Instance.OnActiveDeviceConnected += ReloadPage;

        LoadingCover.AutoFinish((t) =>
        {
            LoadingBlur.Radius = Math.Max(0, LoadingBlur.Radius - t * 20);
        });
    }

    /// <summary>
    /// Load plugins from the "Plugins" subfolder. This allows users to add new features without modifying the main executable.
    /// DLLs is placed under their own subfolder in "Plugins" to avoid name conflicts and allow multiple versions.
    /// </summary>
    /// <param name="pluginsFolder"></param>
    private void LoadPluginDLLs(string pluginsFolder)
    {
        if (!Directory.Exists(pluginsFolder))
        {
            LogMessage($"[PluginLoad] Plugins folder not found: {pluginsFolder}");
            return;
        }
        foreach (string subDir in Directory.GetDirectories(pluginsFolder))
        {
            LoadDLLsInFolder(subDir);
        }
    }

    private void LoadDLLsInFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            LogMessage($"[PluginLoad] Folder not found: {folder}");
            return;
        }
        foreach (string dllPath in Directory.GetFiles(folder, "*.dll"))
        {
            LoadPluginDLL(dllPath);
        }
    }

    private void LoadPluginDLL(string dllPath)
    {
        try
        {
            var asmName = AssemblyName.GetAssemblyName(dllPath);
            if (asmName == null) return;
            var loadedAsm = Assembly.LoadFrom(dllPath);
            LogMessage($"[PluginLoad] Loaded plugin: {asmName.FullName}");
        }
        catch (Exception ex)
        {
            LogMessage($"[PluginLoad] Failed to load plugin from {dllPath}: {ex.Message}");
        }
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

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
            UpdateLayoutMode(e.NewSize.Width);
    }

    #endregion

    #region Page

    private readonly Dictionary<IPageBase, INavigationItem> navPageMap = new();
    private PageBase currentPage = null;

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

    private async Task BuildNavigationTabs(IEnumerable<Assembly> assemblies)
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
            await Dispatcher.InvokeAsync(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();

                PageBase newPage = null;
                try
                {
                    newPage = (PageBase)Activator.CreateInstance(t);
                }
                catch (FileNotFoundException fileEx)
                {
                    LogMessage($"[NavInit] Failed to load {t.FullName}: {fileEx.Message}");
                    jobs[t].Finish();
                }
                catch (Exception ex)
                {
                    LogMessage($"[NavInit] Failed to load {t.FullName}: {ex.Message}");
                }
                if (newPage == null)
                {
                    jobs[t].Finish();
                    return;
                }

                RegisterWpfObject(newPage);

                if (newPage.NavOrder >= 0)
                {
                    string[] path = PathAttribute.GetPath(newPage, nameof(newPage.PageName));
                    INavigationItem newTab;

                    AddButtonDelegate add = newPage.NavAlignment switch
                    {
                        PageBase.NavigationAlignment.Front => NavTabsManager.AddTop,
                        PageBase.NavigationAlignment.Back => NavTabsManager.AddBottom,
                        _ => NavTabsManager.AddTop
                    };

                    newTab = add(
                        text: newPage.PageName,
                        path: path,
                        glyph: newPage.Glyph,
                        secondaryGlyph: newPage.SecondaryGlyph,
                        secondaryText: newPage.ShortName,
                        order: newPage.NavOrder);

                    newTab.OnClick += () => SelectPage(newPage);
                    navPageMap.Add(newPage, newTab);
                }

                sw.Stop();
                LogMessage($"[NavInit] {t.FullName} : {sw.Elapsed.TotalMilliseconds:0.###} ms");
                sw = null;
                jobs[t].Finish();
            }, DispatcherPriority.Loaded);
        }
    }

    public void SelectPage<T>() where T : PageBase
    {
        T page = FindObjectOfType<T>();
    }

    public void SelectPage(PageBase page)
    {
        if (page == null) return;
        if (page == currentPage) return;

        if (currentPage != null) navPageMap[currentPage].SetHighlightedState(false);
        currentPage?.Disable();

        PageBase lastPage = currentPage;

        currentPage = page;
        AssignToolsMenu(currentPage);

        OnPageChanged?.Invoke(lastPage, page);

        currentPage?.Enable();
        navPageMap[currentPage].SetHighlightedState(true);

        SetDeviceSelectionVisibility(currentPage.ShowDeviceSelection);

        ContentFrame.Children.Clear();
        ContentFrame.Children.Add(page);
    }

    public void ReloadPage()
    {
        currentPage?.Disable();
        currentPage?.Enable();
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

    /// <summary>
    /// Searches the registered WPF objects and returns the first instance of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The type of object to search for. Must derive from <see cref="WpfBehaviour"/>.</typeparam>
    /// <param name="findInactive">
    /// If true, includes disabled objects in the search; otherwise, only enabled objects are considered.
    /// </param>
    /// <returns>
    /// The first matching object of type <typeparamref name="T"/> if found; otherwise, null.
    /// </returns>
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

    private async Task PreloadWpfBehaviourSingletons(IEnumerable<Assembly> assemblies)
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

    #endregion

    #region Custom Functions

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

    private const string bugReportUrl = "https://forms.office.com/Pages/ResponsePage.aspx?id=xFkfMGnCZkqKjPXaqyEfozJm8MaUvBNDsBYYmv4ZE1tUMlQ0RVcxWVo2Q1RTSFRIUzVOVlMzVVc2US4u";
    private const string featureRequestUrl = "https://forms.office.com/Pages/ResponsePage.aspx?id=xFkfMGnCZkqKjPXaqyEfozJm8MaUvBNDsBYYmv4ZE1tUMUhIWFJMOFdFWjRSWDNXT0RFRjNGOThXVi4u";

    public void SetDeviceSelectionVisibility(bool state)
    {
        TitleBarControls.Visibility = state ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SelectTabIndex(int index)
    {
        Stack<IEnumerable<INavigationItem>> stack = new();
        stack.Push(NavTabsManager.TopButtons);
        stack.Push(NavTabsManager.BottomButtons);
        int walk = 0;

        while (stack.Count > 0)
        {
            foreach (INavigationItem item in stack.Pop())
            {
                if (item is NavigationButton button)
                {
                    if (walk == index)
                    {
                        button.Click();
                        return;
                    }
                    walk++;
                }
                else if (item is NavigationExpander expander)
                {
                    stack.Push(expander.Items);
                }
            }
        }
    }
    public void SelectTabByName(string name)
    {
        Stack<IEnumerable<INavigationItem>> stack = new();
        stack.Push(NavTabsManager.TopButtons);
        stack.Push(NavTabsManager.BottomButtons);
        while (stack.Count > 0)
        {
            foreach (INavigationItem item in stack.Pop())
            {
                if (item is NavigationButton button)
                {
                    if (button.Text == name)
                    {
                        button.Click();
                        return;
                    }
                }
                else if (item is NavigationExpander expander)
                {
                    stack.Push(expander.Items);
                }
            }
        }
    }

    public static string GetOutputFolder(params string[] subFolders)
        => GetOutputFolder(true, subFolders);
    public static string GetOutputFolder(bool create = true, params string[] subFolders)
        => GetFolder("Output", create, subFolders);
    public static string GetToolFolder(params string[] subFolders)
        => GetToolFolder(true, subFolders);
    public static string GetToolFolder(bool create = true, params string[] subFolders)
        => GetFolder("Tools", create, subFolders);
    public static string GetConfigFolder(params string[] subFolders)
        => GetConfigFolder(true, subFolders);
    public static string GetConfigFolder(bool create = true, params string[] subFolders)
        => GetFolder("Config", create, subFolders);
    public static string GetPluginsFolder(params string[] subFolders)
        => GetPluginsFolder(true, subFolders);
    public static string GetPluginsFolder(bool create = true, params string[] subFolders)
        => GetFolder("Plugins", create, subFolders);

    public static string GetFolder(string folderName, bool create = true, params string[] subFolders)
    {
        string exeDir = AppContext.BaseDirectory;
        string dir = Path.Combine(exeDir, folderName, Path.Combine(subFolders));
        if (create) Directory.CreateDirectory(dir); // Safe even if it exists
        return dir;
    }

    public static string GetExePath()
    {
        return Environment.ProcessPath!;
    }

    public void RequestWindowFocus()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        Focus();
    }

    public static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show("The link is not configured.", "Oops", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to open the browser.\n\n{ex.Message}",
                "Oops",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
    public void SetFWVersion(byte major, byte inter, byte minor)
    {
        SetFWVersion($"V{major:X2}_{inter:X2}_{minor:X2}");
    }

    public void SetFWVersion(string version)
    {
        string text = string.IsNullOrEmpty(version) ? "FW: ----" : $"FW: {version}";
        if (Application.Current?.Dispatcher?.CheckAccess() ?? false)
        {
            MainFooter.DeviceVersion.Text = text;
        }
        else
        {
            Application.Current?.Dispatcher?.Invoke(() => MainFooter.DeviceVersion.Text = text);
        }
    }

    public void SetBatteryStatus(bool isCharging, byte[] level)
    {
        if (Application.Current?.Dispatcher?.CheckAccess() ?? false)
        {
            MainFooter.BatteryIndicator.SetBatteryLevel(level);
            MainFooter.BatteryIndicator.SetBatteryStatus(isCharging);
        }
        else
        {
            Application.Current?.Dispatcher?.Invoke(() => { SetBatteryStatus(isCharging, level); });
        }
    }

    private void ShowAbout(object sender, RoutedEventArgs e)
    {
        AboutWindow.Show(this);
    }

    private void RedirectToFeedbackURL(object sender, RoutedEventArgs e)
    {
        OpenUrl(bugReportUrl);
    }

    private void RedirectToFeatureRequestURL(object sender, RoutedEventArgs e)
    {
        OpenUrl(featureRequestUrl);
    }

    private void UpdateLayoutMode(double width)
    {
        CurrentLayoutMode =
            width < 900 ? LayoutMode.Compact :
            width < 1100 ? LayoutMode.Normal :
            LayoutMode.Wide;
    }

    private void LogMessage(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        LogTextBox.AppendText($"[{timestamp}] {message}\n");
        LogTextBox.ScrollToEnd();
    }

    #endregion

    #region Menu Item

    private readonly List<CommandBinding> bindedMenuItemCommands = new();

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

        Style toolStyle = (Style)Application.Current.FindResource(typeof(MenuItem));
        foreach (var tool in rootTools.Values)
        {
            tool.Style = toolStyle;
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

    public event PropertyChangedEventHandler PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}