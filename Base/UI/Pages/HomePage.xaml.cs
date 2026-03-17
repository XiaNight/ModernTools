using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Management;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Base.Pages
{
    /// <summary>
    /// Interaction logic for HomePage.xaml
    /// </summary>
    public partial class HomePage : PageBase, INotifyPropertyChanged
    {
        public override string PageName => "Home";
        public override int NavOrder => -1;

        private readonly DispatcherTimer _clockTimer;

        private string _currentTimeText = DateTime.Now.ToString("dddd, MMM dd yyyy  HH:mm:ss", CultureInfo.InvariantCulture);
        private string _currentMachineText = $"{Environment.UserName}@{Environment.MachineName}";
        private string _appVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "dev";
        private MachineInfo _machineInfo = new();
        private ObservableCollection<RecentPageItem> _recentPages = new();

        public event PropertyChangedEventHandler PropertyChanged;

        public string CurrentTimeText
        {
            get => _currentTimeText;
            set => SetField(ref _currentTimeText, value);
        }

        public string CurrentMachineText
        {
            get => _currentMachineText;
            set => SetField(ref _currentMachineText, value);
        }

        public string AppVersion
        {
            get => _appVersion;
            set => SetField(ref _appVersion, value);
        }

        public MachineInfo MachineInfo
        {
            get => _machineInfo;
            set => SetField(ref _machineInfo, value);
        }

        public ObservableCollection<RecentPageItem> RecentPages
        {
            get => _recentPages;
            set => SetField(ref _recentPages, value);
        }

        public HomePage()
        {
            InitializeComponent();
            DataContext = this;

            SeedRecentPages();
            RefreshMachineInfo();

            _clockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _clockTimer.Tick += (_, _) =>
            {
                CurrentTimeText = DateTime.Now.ToString("dddd, MMM dd yyyy  HH:mm:ss", CultureInfo.InvariantCulture);
                MachineInfo.Uptime = GetUptimeText();
            };
            _clockTimer.Start();
        }

        private void RefreshSpecsButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshMachineInfo();
        }

        private void RefreshMachineInfo()
        {
            MachineInfo = MachineInfoProvider.GetMachineInfo();
            MachineInfo.Uptime = GetUptimeText();
        }

        private void SeedRecentPages()
        {
            RecentPages = new ObservableCollection<RecentPageItem>
            {
                new() { Title = "Keyboard", Subtitle = "Profile editor and macro map", LastOpened = "Today 10:21" },
                new() { Title = "Gamepad", Subtitle = "Input test and deadzone tuning", LastOpened = "Today 09:48" },
                new() { Title = "Log", Subtitle = "Recent traces and operation history", LastOpened = "Yesterday 18:05" },
                new() { Title = "Audio", Subtitle = "Endpoint checks and output test", LastOpened = "Yesterday 16:33" }
            };
        }

        private static string GetUptimeText()
        {
            try
            {
                var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
                if (uptime.TotalDays >= 1)
                    return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
                if (uptime.TotalHours >= 1)
                    return $"{uptime.Hours}h {uptime.Minutes}m";
                return $"{uptime.Minutes}m";
            }
            catch
            {
                return "Unknown";
            }
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class RecentPageItem
    {
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string LastOpened { get; set; } = string.Empty;
    }

    public sealed class MachineInfo : INotifyPropertyChanged
    {
        private string _cpuName = "Loading...";
        private string _gpuName = "Loading...";
        private string _ramDisplay = "Loading...";
        private double _ramUsagePercent;
        private string _diskDisplay = "Loading...";
        private double _diskUsedPercent;
        private string _windowsVersion = "Loading...";
        private string _machineName = Environment.MachineName;
        private string _userName = Environment.UserName;
        private string _systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        private string _dotNetVersion = Environment.Version.ToString();
        private string _lastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        private string _uptime = "Unknown";

        public event PropertyChangedEventHandler? PropertyChanged;

        public string CpuName
        {
            get => _cpuName;
            set => SetField(ref _cpuName, value);
        }

        public string GpuName
        {
            get => _gpuName;
            set => SetField(ref _gpuName, value);
        }

        public string RamDisplay
        {
            get => _ramDisplay;
            set
            {
                if (SetField(ref _ramDisplay, value))
                {
                    OnPropertyChanged(nameof(RamUsagePercentText));
                    OnPropertyChanged(nameof(RamUsageBarText));
                }
            }
        }

        public double RamUsagePercent
        {
            get => _ramUsagePercent;
            set
            {
                if (SetField(ref _ramUsagePercent, value))
                {
                    OnPropertyChanged(nameof(RamUsagePercentText));
                    OnPropertyChanged(nameof(RamUsageBarText));
                }
            }
        }

        public string DiskDisplay
        {
            get => _diskDisplay;
            set
            {
                if (SetField(ref _diskDisplay, value))
                {
                    OnPropertyChanged(nameof(DiskUsedPercentText));
                    OnPropertyChanged(nameof(DiskBarText));
                }
            }
        }

        public double DiskUsedPercent
        {
            get => _diskUsedPercent;
            set
            {
                if (SetField(ref _diskUsedPercent, value))
                {
                    OnPropertyChanged(nameof(DiskUsedPercentText));
                    OnPropertyChanged(nameof(DiskBarText));
                }
            }
        }

        public string WindowsVersion
        {
            get => _windowsVersion;
            set => SetField(ref _windowsVersion, value);
        }

        public string MachineName
        {
            get => _machineName;
            set => SetField(ref _machineName, value);
        }

        public string UserName
        {
            get => _userName;
            set => SetField(ref _userName, value);
        }

        public string SystemDrive
        {
            get => _systemDrive;
            set => SetField(ref _systemDrive, value);
        }

        public string DotNetVersion
        {
            get => _dotNetVersion;
            set => SetField(ref _dotNetVersion, value);
        }

        public string LastUpdated
        {
            get => _lastUpdated;
            set => SetField(ref _lastUpdated, value);
        }

        public string Uptime
        {
            get => _uptime;
            set => SetField(ref _uptime, value);
        }

        public string RamUsagePercentText => $"{Math.Round(RamUsagePercent)}%";
        public string DiskUsedPercentText => $"{Math.Round(DiskUsedPercent)}%";
        public string RamUsageBarText => $"Memory used: {Math.Round(RamUsagePercent)}%";
        public string DiskBarText => $"System drive used: {Math.Round(DiskUsedPercent)}%";

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
                return false;

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public static class MachineInfoProvider
    {
        public static MachineInfo GetMachineInfo()
        {
            var info = new MachineInfo
            {
                MachineName = Environment.MachineName,
                UserName = Environment.UserName,
                SystemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\",
                DotNetVersion = $".NET {Environment.Version}",
                LastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            };

            info.CpuName = GetCpuName();
            info.GpuName = GetGpuName();
            PopulateRam(info);
            PopulateDisk(info);
            info.WindowsVersion = GetWindowsVersionText();

            return info;
        }

        private static string GetCpuName()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                foreach (var obj in searcher.Get().OfType<ManagementObject>())
                {
                    var name = obj["Name"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                        return NormalizeWhitespace(name);
                }
            }
            catch
            {
            }

            return "Unknown CPU";
        }

        private static string GetGpuName()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                var names = searcher.Get()
                    .OfType<ManagementObject>()
                    .Select(x => x["Name"]?.ToString()?.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(NormalizeWhitespace)
                    .Distinct()
                    .ToList();

                if (names.Count > 0)
                    return string.Join(" | ", names);
            }
            catch
            {
            }

            return "Unknown GPU";
        }

        private static void PopulateRam(MachineInfo info)
        {
            try
            {
                ulong totalBytes = 0;

                using (var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                {
                    foreach (var obj in searcher.Get().OfType<ManagementObject>())
                    {
                        totalBytes = Convert.ToUInt64(obj["TotalPhysicalMemory"]);
                        break;
                    }
                }

                if (totalBytes == 0)
                {
                    info.RamDisplay = "Unknown";
                    info.RamUsagePercent = 0;
                    return;
                }

                ulong freeKb = 0;
                using (var searcher = new ManagementObjectSearcher("SELECT FreePhysicalMemory FROM Win32_OperatingSystem"))
                {
                    foreach (var obj in searcher.Get().OfType<ManagementObject>())
                    {
                        freeKb = Convert.ToUInt64(obj["FreePhysicalMemory"]);
                        break;
                    }
                }

                var freeBytes = freeKb * 1024.0;
                var usedBytes = Math.Max(0, totalBytes - freeBytes);
                var usedPercent = totalBytes > 0 ? (usedBytes / totalBytes) * 100.0 : 0.0;

                info.RamUsagePercent = Math.Round(usedPercent, 1);
                info.RamDisplay = $"{FormatBytes((long)usedBytes)} / {FormatBytes((long)totalBytes)}";
            }
            catch
            {
                info.RamDisplay = "Unavailable";
                info.RamUsagePercent = 0;
            }
        }

        private static void PopulateDisk(MachineInfo info)
        {
            try
            {
                var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
                var drive = new DriveInfo(root);

                if (!drive.IsReady)
                {
                    info.DiskDisplay = $"{root} not ready";
                    info.DiskUsedPercent = 0;
                    return;
                }

                long total = drive.TotalSize;
                long free = drive.AvailableFreeSpace;
                long used = total - free;
                var usedPercent = total > 0 ? (double)used / total * 100.0 : 0.0;

                info.DiskUsedPercent = Math.Round(usedPercent, 1);
                info.DiskDisplay = $"{drive.Name} {FormatBytes(used)} / {FormatBytes(total)}";
            }
            catch
            {
                info.DiskDisplay = "Unavailable";
                info.DiskUsedPercent = 0;
            }
        }

        private static string GetWindowsVersionText()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                if (key == null)
                    return Environment.OSVersion.VersionString;

                var productName = key.GetValue("ProductName")?.ToString() ?? "Windows";
                var displayVersion = key.GetValue("DisplayVersion")?.ToString();
                var releaseId = key.GetValue("ReleaseId")?.ToString();
                var build = key.GetValue("CurrentBuildNumber")?.ToString();
                var ubr = key.GetValue("UBR")?.ToString();

                var marketingVersion = !string.IsNullOrWhiteSpace(displayVersion) ? displayVersion : releaseId;
                var buildText = !string.IsNullOrWhiteSpace(build)
                    ? (!string.IsNullOrWhiteSpace(ubr) ? $"{build}.{ubr}" : build)
                    : Environment.OSVersion.Version.Build.ToString(CultureInfo.InvariantCulture);

                if (!string.IsNullOrWhiteSpace(marketingVersion))
                    return $"{productName} {marketingVersion} (Build {buildText})";

                return $"{productName} (Build {buildText})";
            }
            catch
            {
                return Environment.OSVersion.VersionString;
            }
        }

        private static string NormalizeWhitespace(string value)
        {
            return string.Join(" ", value.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB", "PB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < suffixes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            return $"{len:0.#} {suffixes[order]}";
        }
    }
}