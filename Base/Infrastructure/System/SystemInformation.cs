using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Management;
using System.Runtime.CompilerServices;

namespace Base.Infrastructure.System;

public abstract class BindableBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (!string.IsNullOrWhiteSpace(propertyName))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class SystemInformation : BindableBase
{
    private StaticSystemInformation staticInfo = new();
    private DynamicSystemInformation dynamicInfo = new();

    public StaticSystemInformation Static
    {
        get => staticInfo;
        set => SetProperty(ref staticInfo, value);
    }

    public DynamicSystemInformation Dynamic
    {
        get => dynamicInfo;
        set => SetProperty(ref dynamicInfo, value);
    }
}

public sealed class StaticSystemInformation : BindableBase
{
    private string machineName = string.Empty;
    private string userName = string.Empty;
    private string systemDirectory = string.Empty;
    private string systemDrive = string.Empty;
    private string dotNetVersion = string.Empty;
    private string windowsProductName = string.Empty;
    private string windowsDisplayVersion = string.Empty;
    private string windowsReleaseId = string.Empty;
    private string windowsBuildNumber = string.Empty;
    private string windowsUbr = string.Empty;
    private string windowsVersionString = string.Empty;
    private string windowsCaption = string.Empty;
    private string windowsVersion = string.Empty;
    private string windowsArchitecture = string.Empty;
    private string windowsInstallDateRaw = string.Empty;
    private string manufacturer = string.Empty;
    private string model = string.Empty;
    private string systemFamily = string.Empty;
    private string systemType = string.Empty;
    private string domain = string.Empty;
    private string workgroup = string.Empty;
    private bool partOfDomain;
    private string primaryOwnerName = string.Empty;
    private string totalPhysicalMemoryRaw = string.Empty;
    private StaticDriveInfo systemDriveInfo = new();

    public string MachineName
    {
        get => machineName;
        set => SetProperty(ref machineName, value);
    }

    public string UserName
    {
        get => userName;
        set => SetProperty(ref userName, value);
    }

    public string SystemDirectory
    {
        get => systemDirectory;
        set => SetProperty(ref systemDirectory, value);
    }

    public string SystemDrive
    {
        get => systemDrive;
        set => SetProperty(ref systemDrive, value);
    }

    public string DotNetVersion
    {
        get => dotNetVersion;
        set => SetProperty(ref dotNetVersion, value);
    }

    public string WindowsProductName
    {
        get => windowsProductName;
        set => SetProperty(ref windowsProductName, value);
    }

    public string WindowsDisplayVersion
    {
        get => windowsDisplayVersion;
        set => SetProperty(ref windowsDisplayVersion, value);
    }

    public string WindowsReleaseId
    {
        get => windowsReleaseId;
        set => SetProperty(ref windowsReleaseId, value);
    }

    public string WindowsBuildNumber
    {
        get => windowsBuildNumber;
        set => SetProperty(ref windowsBuildNumber, value);
    }

    public string WindowsUbr
    {
        get => windowsUbr;
        set => SetProperty(ref windowsUbr, value);
    }

    public string WindowsVersionString
    {
        get => windowsVersionString;
        set => SetProperty(ref windowsVersionString, value);
    }

    public string WindowsCaption
    {
        get => windowsCaption;
        set => SetProperty(ref windowsCaption, value);
    }

    public string WindowsVersion
    {
        get => windowsVersion;
        set => SetProperty(ref windowsVersion, value);
    }

    public string WindowsArchitecture
    {
        get => windowsArchitecture;
        set => SetProperty(ref windowsArchitecture, value);
    }

    public string WindowsInstallDateRaw
    {
        get => windowsInstallDateRaw;
        set => SetProperty(ref windowsInstallDateRaw, value);
    }

    public string Manufacturer
    {
        get => manufacturer;
        set => SetProperty(ref manufacturer, value);
    }

    public string Model
    {
        get => model;
        set => SetProperty(ref model, value);
    }

    public string SystemFamily
    {
        get => systemFamily;
        set => SetProperty(ref systemFamily, value);
    }

    public string SystemType
    {
        get => systemType;
        set => SetProperty(ref systemType, value);
    }

    public string Domain
    {
        get => domain;
        set => SetProperty(ref domain, value);
    }

    public string Workgroup
    {
        get => workgroup;
        set => SetProperty(ref workgroup, value);
    }

    public bool PartOfDomain
    {
        get => partOfDomain;
        set => SetProperty(ref partOfDomain, value);
    }

    public string PrimaryOwnerName
    {
        get => primaryOwnerName;
        set => SetProperty(ref primaryOwnerName, value);
    }

    public string TotalPhysicalMemoryRaw
    {
        get => totalPhysicalMemoryRaw;
        set => SetProperty(ref totalPhysicalMemoryRaw, value);
    }

    public ObservableCollection<CpuStaticInfo> Processors { get; } = new();
    public ObservableCollection<GpuStaticInfo> VideoControllers { get; } = new();

    public StaticDriveInfo SystemDriveInfo
    {
        get => systemDriveInfo;
        set => SetProperty(ref systemDriveInfo, value);
    }

    public string CpuName =>
        Processors.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Name))?.Name ?? string.Empty;

    public string GpuName =>
        string.Join(" | ", VideoControllers
            .Select(x => x.Name)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase));

    public StaticSystemInformation()
    {
        Processors.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CpuName));
        VideoControllers.CollectionChanged += (_, _) => OnPropertyChanged(nameof(GpuName));
    }
}

public sealed class DynamicSystemInformation : BindableBase
{
    private DateTime collectedAtLocal;
    private DateTime collectedAtUtc;
    private string windowsLastBootUpTimeRaw = string.Empty;
    private OsMemoryInfo memory = new();
    private DynamicDriveInfo systemDriveInfo = new();

    public DateTime CollectedAtLocal
    {
        get => collectedAtLocal;
        set => SetProperty(ref collectedAtLocal, value);
    }

    public DateTime CollectedAtUtc
    {
        get => collectedAtUtc;
        set => SetProperty(ref collectedAtUtc, value);
    }

    public string WindowsLastBootUpTimeRaw
    {
        get => windowsLastBootUpTimeRaw;
        set => SetProperty(ref windowsLastBootUpTimeRaw, value);
    }

    public OsMemoryInfo Memory
    {
        get => memory;
        set => SetProperty(ref memory, value);
    }

    public DynamicDriveInfo SystemDriveInfo
    {
        get => systemDriveInfo;
        set => SetProperty(ref systemDriveInfo, value);
    }

    public ObservableCollection<CpuDynamicInfo> Processors { get; } = new();
    public ObservableCollection<GpuDynamicInfo> VideoControllers { get; } = new();

}

public sealed class CpuStaticInfo : BindableBase
{
    private string name = string.Empty;
    private string manufacturer = string.Empty;
    private string processorId = string.Empty;
    private string description = string.Empty;
    private string numberOfCoresRaw = string.Empty;
    private string numberOfLogicalProcessorsRaw = string.Empty;
    private string maxClockSpeedMhzRaw = string.Empty;
    private string socketDesignation = string.Empty;
    private string architectureRaw = string.Empty;

    public string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    public string Manufacturer
    {
        get => manufacturer;
        set => SetProperty(ref manufacturer, value);
    }

    public string ProcessorId
    {
        get => processorId;
        set => SetProperty(ref processorId, value);
    }

    public string Description
    {
        get => description;
        set => SetProperty(ref description, value);
    }

    public string NumberOfCoresRaw
    {
        get => numberOfCoresRaw;
        set => SetProperty(ref numberOfCoresRaw, value);
    }

    public string NumberOfLogicalProcessorsRaw
    {
        get => numberOfLogicalProcessorsRaw;
        set => SetProperty(ref numberOfLogicalProcessorsRaw, value);
    }

    public string MaxClockSpeedMhzRaw
    {
        get => maxClockSpeedMhzRaw;
        set => SetProperty(ref maxClockSpeedMhzRaw, value);
    }

    public string SocketDesignation
    {
        get => socketDesignation;
        set => SetProperty(ref socketDesignation, value);
    }

    public string ArchitectureRaw
    {
        get => architectureRaw;
        set => SetProperty(ref architectureRaw, value);
    }
}

public sealed class CpuDynamicInfo : BindableBase
{
    private string currentClockSpeedMhzRaw = string.Empty;

    public string CurrentClockSpeedMhzRaw
    {
        get => currentClockSpeedMhzRaw;
        set => SetProperty(ref currentClockSpeedMhzRaw, value);
    }
}

public sealed class GpuStaticInfo : BindableBase
{
    private string name = string.Empty;
    private string adapterCompatibility = string.Empty;
    private string driverVersion = string.Empty;
    private string videoProcessor = string.Empty;
    private string adapterRamRaw = string.Empty;
    private string pnpDeviceId = string.Empty;

    public string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    public string AdapterCompatibility
    {
        get => adapterCompatibility;
        set => SetProperty(ref adapterCompatibility, value);
    }

    public string DriverVersion
    {
        get => driverVersion;
        set => SetProperty(ref driverVersion, value);
    }

    public string VideoProcessor
    {
        get => videoProcessor;
        set => SetProperty(ref videoProcessor, value);
    }

    public string AdapterRamRaw
    {
        get => adapterRamRaw;
        set => SetProperty(ref adapterRamRaw, value);
    }

    public string PnpDeviceId
    {
        get => pnpDeviceId;
        set => SetProperty(ref pnpDeviceId, value);
    }
}

public sealed class GpuDynamicInfo : BindableBase
{
    private string currentHorizontalResolutionRaw = string.Empty;
    private string currentVerticalResolutionRaw = string.Empty;

    public string CurrentHorizontalResolutionRaw
    {
        get => currentHorizontalResolutionRaw;
        set => SetProperty(ref currentHorizontalResolutionRaw, value);
    }

    public string CurrentVerticalResolutionRaw
    {
        get => currentVerticalResolutionRaw;
        set => SetProperty(ref currentVerticalResolutionRaw, value);
    }
}

public sealed class OsMemoryInfo : BindableBase
{
    private string totalVisibleMemoryKbRaw = string.Empty;
    private string freePhysicalMemoryKbRaw = string.Empty;
    private string totalVirtualMemoryKbRaw = string.Empty;
    private string freeVirtualMemoryKbRaw = string.Empty;

    public string TotalVisibleMemoryKbRaw
    {
        get => totalVisibleMemoryKbRaw;
        set => SetProperty(ref totalVisibleMemoryKbRaw, value);
    }

    public string FreePhysicalMemoryKbRaw
    {
        get => freePhysicalMemoryKbRaw;
        set => SetProperty(ref freePhysicalMemoryKbRaw, value);
    }

    public string TotalVirtualMemoryKbRaw
    {
        get => totalVirtualMemoryKbRaw;
        set => SetProperty(ref totalVirtualMemoryKbRaw, value);
    }

    public string FreeVirtualMemoryKbRaw
    {
        get => freeVirtualMemoryKbRaw;
        set => SetProperty(ref freeVirtualMemoryKbRaw, value);
    }
}

public sealed class StaticDriveInfo : BindableBase
{
    private string name = string.Empty;
    private string driveType = string.Empty;
    private string driveFormat = string.Empty;
    private string rootDirectory = string.Empty;
    private string volumeLabel = string.Empty;

    public string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    public string DriveType
    {
        get => driveType;
        set => SetProperty(ref driveType, value);
    }

    public string DriveFormat
    {
        get => driveFormat;
        set => SetProperty(ref driveFormat, value);
    }

    public string RootDirectory
    {
        get => rootDirectory;
        set => SetProperty(ref rootDirectory, value);
    }

    public string VolumeLabel
    {
        get => volumeLabel;
        set => SetProperty(ref volumeLabel, value);
    }
}

public sealed class DynamicDriveInfo : BindableBase
{
    private bool isReady;
    private string totalSizeRaw = string.Empty;
    private string availableFreeSpaceRaw = string.Empty;
    private string totalFreeSpaceRaw = string.Empty;

    public bool IsReady
    {
        get => isReady;
        set => SetProperty(ref isReady, value);
    }

    public string TotalSizeRaw
    {
        get => totalSizeRaw;
        set => SetProperty(ref totalSizeRaw, value);
    }

    public string AvailableFreeSpaceRaw
    {
        get => availableFreeSpaceRaw;
        set => SetProperty(ref availableFreeSpaceRaw, value);
    }

    public string TotalFreeSpaceRaw
    {
        get => totalFreeSpaceRaw;
        set => SetProperty(ref totalFreeSpaceRaw, value);
    }
}

public static class WindowsInformationProvider
{
    private const string ComputerSystemQuery =
        "SELECT Manufacturer, Model, SystemFamily, SystemType, Domain, Workgroup, PartOfDomain, PrimaryOwnerName, TotalPhysicalMemory FROM Win32_ComputerSystem";

    private const string OperatingSystemQuery =
        "SELECT Caption, Version, OSArchitecture, InstallDate, LastBootUpTime, TotalVisibleMemorySize, FreePhysicalMemory, TotalVirtualMemorySize, FreeVirtualMemory FROM Win32_OperatingSystem";

    private const string ProcessorQuery =
        "SELECT Name, Manufacturer, ProcessorId, Description, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, CurrentClockSpeed, SocketDesignation, Architecture FROM Win32_Processor";

    private const string VideoControllerQuery =
        "SELECT Name, AdapterCompatibility, DriverVersion, VideoProcessor, AdapterRAM, PNPDeviceID, CurrentHorizontalResolution, CurrentVerticalResolution FROM Win32_VideoController";

    public static SystemInformation GetWindowsInformation()
    {
        var systemDirectory = Environment.SystemDirectory;
        var systemDrive = Path.GetPathRoot(systemDirectory) ?? "C:\\";

        var info = new SystemInformation
        {
            Static =
        {
            MachineName = Environment.MachineName,
            UserName = Environment.UserName,
            SystemDirectory = systemDirectory,
            SystemDrive = systemDrive,
            DotNetVersion = Environment.Version.ToString(),
            WindowsVersionString = Environment.OSVersion.VersionString
        },
            Dynamic =
        {
            CollectedAtLocal = DateTime.Now,
            CollectedAtUtc = DateTime.UtcNow
        }
        };

        PopulateComputerSystem(info.Static);
        PopulateOperatingSystem(info.Static, info.Dynamic);
        PopulateProcessors(info.Static, info.Dynamic);
        PopulateVideoControllers(info.Static, info.Dynamic);
        PopulateWindowsRegistry(info.Static);
        PopulateSystemDrive(info.Static, info.Dynamic, systemDrive);

        return info;
    }

    public static StaticSystemInformation GetStaticWindowsInformation()
    {
        var systemDirectory = Environment.SystemDirectory;
        var systemDrive = Path.GetPathRoot(systemDirectory) ?? "C:\\";

        var info = new StaticSystemInformation
        {
            MachineName = Environment.MachineName,
            UserName = Environment.UserName,
            SystemDirectory = systemDirectory,
            SystemDrive = systemDrive,
            DotNetVersion = Environment.Version.ToString(),
            WindowsVersionString = Environment.OSVersion.VersionString
        };

        PopulateComputerSystem(info);
        PopulateOperatingSystemStatic(info);
        PopulateProcessorsStatic(info);
        PopulateVideoControllersStatic(info);
        PopulateWindowsRegistry(info);
        PopulateSystemDriveStatic(info, systemDrive);

        return info;
    }

    public static DynamicSystemInformation GetDynamicWindowsInformation()
    {
        var systemDirectory = Environment.SystemDirectory;
        var systemDrive = Path.GetPathRoot(systemDirectory) ?? "C:\\";

        var info = new DynamicSystemInformation
        {
            CollectedAtLocal = DateTime.Now,
            CollectedAtUtc = DateTime.UtcNow
        };

        PopulateOperatingSystemDynamic(info);
        PopulateProcessorsDynamic(info);
        PopulateVideoControllersDynamic(info);
        PopulateSystemDriveDynamic(info, systemDrive);

        return info;
    }

    public static void RefreshDynamicWindowsInformation(DynamicSystemInformation info)
    {
        ArgumentNullException.ThrowIfNull(info);

        var systemDirectory = Environment.SystemDirectory;
        var systemDrive = Path.GetPathRoot(systemDirectory) ?? "C:\\";

        info.CollectedAtLocal = DateTime.Now;
        info.CollectedAtUtc = DateTime.UtcNow;

        info.Processors.Clear();
        info.VideoControllers.Clear();

        PopulateOperatingSystemDynamic(info);
        PopulateProcessorsDynamic(info);
        PopulateVideoControllersDynamic(info);
        PopulateSystemDriveDynamic(info, systemDrive);
    }

    private static void PopulateComputerSystem(StaticSystemInformation info)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(ComputerSystemQuery);
            using var results = searcher.Get();

            foreach (ManagementObject obj in results.Cast<ManagementObject>())
            {
                info.Manufacturer = ReadString(obj, "Manufacturer");
                info.Model = ReadString(obj, "Model");
                info.SystemFamily = ReadString(obj, "SystemFamily");
                info.SystemType = ReadString(obj, "SystemType");
                info.Domain = ReadString(obj, "Domain");
                info.Workgroup = ReadString(obj, "Workgroup");
                info.PartOfDomain = ReadBool(obj, "PartOfDomain");
                info.PrimaryOwnerName = ReadString(obj, "PrimaryOwnerName");
                info.TotalPhysicalMemoryRaw = ReadString(obj, "TotalPhysicalMemory");
                break;
            }
        }
        catch
        {
        }
    }

    private static void PopulateOperatingSystem(StaticSystemInformation staticInfo, DynamicSystemInformation dynamicInfo)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(OperatingSystemQuery);
            using var results = searcher.Get();

            foreach (ManagementObject obj in results.Cast<ManagementObject>())
            {
                staticInfo.WindowsCaption = ReadString(obj, "Caption");
                staticInfo.WindowsVersion = ReadString(obj, "Version");
                staticInfo.WindowsArchitecture = ReadString(obj, "OSArchitecture");
                staticInfo.WindowsInstallDateRaw = ReadString(obj, "InstallDate");

                dynamicInfo.WindowsLastBootUpTimeRaw = ReadString(obj, "LastBootUpTime");
                dynamicInfo.Memory.TotalVisibleMemoryKbRaw = ReadString(obj, "TotalVisibleMemorySize");
                dynamicInfo.Memory.FreePhysicalMemoryKbRaw = ReadString(obj, "FreePhysicalMemory");
                dynamicInfo.Memory.TotalVirtualMemoryKbRaw = ReadString(obj, "TotalVirtualMemorySize");
                dynamicInfo.Memory.FreeVirtualMemoryKbRaw = ReadString(obj, "FreeVirtualMemory");

                break;
            }
        }
        catch
        {
        }
    }

    private static void PopulateOperatingSystemStatic(StaticSystemInformation info)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(OperatingSystemQuery);
            using var results = searcher.Get();

            foreach (ManagementObject obj in results.Cast<ManagementObject>())
            {
                info.WindowsCaption = ReadString(obj, "Caption");
                info.WindowsVersion = ReadString(obj, "Version");
                info.WindowsArchitecture = ReadString(obj, "OSArchitecture");
                info.WindowsInstallDateRaw = ReadString(obj, "InstallDate");
                break;
            }
        }
        catch
        {
        }
    }

    private static void PopulateOperatingSystemDynamic(DynamicSystemInformation info)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(OperatingSystemQuery);
            using var results = searcher.Get();

            foreach (ManagementObject obj in results.Cast<ManagementObject>())
            {
                info.WindowsLastBootUpTimeRaw = ReadString(obj, "LastBootUpTime");
                info.Memory.TotalVisibleMemoryKbRaw = ReadString(obj, "TotalVisibleMemorySize");
                info.Memory.FreePhysicalMemoryKbRaw = ReadString(obj, "FreePhysicalMemory");
                info.Memory.TotalVirtualMemoryKbRaw = ReadString(obj, "TotalVirtualMemorySize");
                info.Memory.FreeVirtualMemoryKbRaw = ReadString(obj, "FreeVirtualMemory");
                break;
            }
        }
        catch
        {
        }
    }

    private static void PopulateProcessors(StaticSystemInformation staticInfo, DynamicSystemInformation dynamicInfo)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(ProcessorQuery);
            using var results = searcher.Get();

            foreach (ManagementObject obj in results.Cast<ManagementObject>())
            {
                staticInfo.Processors.Add(new CpuStaticInfo
                {
                    Name = ReadString(obj, "Name"),
                    Manufacturer = ReadString(obj, "Manufacturer"),
                    ProcessorId = ReadString(obj, "ProcessorId"),
                    Description = ReadString(obj, "Description"),
                    NumberOfCoresRaw = ReadString(obj, "NumberOfCores"),
                    NumberOfLogicalProcessorsRaw = ReadString(obj, "NumberOfLogicalProcessors"),
                    MaxClockSpeedMhzRaw = ReadString(obj, "MaxClockSpeed"),
                    SocketDesignation = ReadString(obj, "SocketDesignation"),
                    ArchitectureRaw = ReadString(obj, "Architecture")
                });

                dynamicInfo.Processors.Add(new CpuDynamicInfo
                {
                    CurrentClockSpeedMhzRaw = ReadString(obj, "CurrentClockSpeed")
                });
            }
        }
        catch
        {
        }
    }

    private static void PopulateProcessorsStatic(StaticSystemInformation info)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(ProcessorQuery);
            using var results = searcher.Get();

            foreach (ManagementObject obj in results.Cast<ManagementObject>())
            {
                info.Processors.Add(new CpuStaticInfo
                {
                    Name = ReadString(obj, "Name"),
                    Manufacturer = ReadString(obj, "Manufacturer"),
                    ProcessorId = ReadString(obj, "ProcessorId"),
                    Description = ReadString(obj, "Description"),
                    NumberOfCoresRaw = ReadString(obj, "NumberOfCores"),
                    NumberOfLogicalProcessorsRaw = ReadString(obj, "NumberOfLogicalProcessors"),
                    MaxClockSpeedMhzRaw = ReadString(obj, "MaxClockSpeed"),
                    SocketDesignation = ReadString(obj, "SocketDesignation"),
                    ArchitectureRaw = ReadString(obj, "Architecture")
                });
            }
        }
        catch
        {
        }
    }

    private static void PopulateProcessorsDynamic(DynamicSystemInformation info)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(ProcessorQuery);
            using var results = searcher.Get();

            foreach (ManagementObject obj in results.Cast<ManagementObject>())
            {
                info.Processors.Add(new CpuDynamicInfo
                {
                    CurrentClockSpeedMhzRaw = ReadString(obj, "CurrentClockSpeed")
                });
            }
        }
        catch
        {
        }
    }

    private static void PopulateVideoControllers(StaticSystemInformation staticInfo, DynamicSystemInformation dynamicInfo)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(VideoControllerQuery);
            using var results = searcher.Get();

            foreach (ManagementObject obj in results.Cast<ManagementObject>())
            {
                staticInfo.VideoControllers.Add(new GpuStaticInfo
                {
                    Name = ReadString(obj, "Name"),
                    AdapterCompatibility = ReadString(obj, "AdapterCompatibility"),
                    DriverVersion = ReadString(obj, "DriverVersion"),
                    VideoProcessor = ReadString(obj, "VideoProcessor"),
                    AdapterRamRaw = ReadString(obj, "AdapterRAM"),
                    PnpDeviceId = ReadString(obj, "PNPDeviceID")
                });

                dynamicInfo.VideoControllers.Add(new GpuDynamicInfo
                {
                    CurrentHorizontalResolutionRaw = ReadString(obj, "CurrentHorizontalResolution"),
                    CurrentVerticalResolutionRaw = ReadString(obj, "CurrentVerticalResolution")
                });
            }
        }
        catch
        {
        }
    }

    private static void PopulateVideoControllersStatic(StaticSystemInformation info)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(VideoControllerQuery);
            using var results = searcher.Get();

            foreach (ManagementObject obj in results.Cast<ManagementObject>())
            {
                info.VideoControllers.Add(new GpuStaticInfo
                {
                    Name = ReadString(obj, "Name"),
                    AdapterCompatibility = ReadString(obj, "AdapterCompatibility"),
                    DriverVersion = ReadString(obj, "DriverVersion"),
                    VideoProcessor = ReadString(obj, "VideoProcessor"),
                    AdapterRamRaw = ReadString(obj, "AdapterRAM"),
                    PnpDeviceId = ReadString(obj, "PNPDeviceID")
                });
            }
        }
        catch
        {
        }
    }

    private static void PopulateVideoControllersDynamic(DynamicSystemInformation info)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(VideoControllerQuery);
            using var results = searcher.Get();

            foreach (ManagementObject obj in results.Cast<ManagementObject>())
            {
                info.VideoControllers.Add(new GpuDynamicInfo
                {
                    CurrentHorizontalResolutionRaw = ReadString(obj, "CurrentHorizontalResolution"),
                    CurrentVerticalResolutionRaw = ReadString(obj, "CurrentVerticalResolution")
                });
            }
        }
        catch
        {
        }
    }

    private static void PopulateWindowsRegistry(StaticSystemInformation info)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key == null)
                return;

            info.WindowsProductName = ReadRegistryString(key, "ProductName");
            info.WindowsDisplayVersion = ReadRegistryString(key, "DisplayVersion");
            info.WindowsReleaseId = ReadRegistryString(key, "ReleaseId");
            info.WindowsBuildNumber = ReadRegistryString(key, "CurrentBuildNumber");
            info.WindowsUbr = ReadRegistryString(key, "UBR");
        }
        catch
        {
        }
    }

    private static void PopulateSystemDrive(StaticSystemInformation staticInfo, DynamicSystemInformation dynamicInfo, string systemDrive)
    {
        try
        {
            var drive = new DriveInfo(systemDrive);

            staticInfo.SystemDriveInfo = new StaticDriveInfo
            {
                Name = drive.Name,
                DriveType = drive.DriveType.ToString(),
                RootDirectory = drive.RootDirectory.FullName
            };

            dynamicInfo.SystemDriveInfo = new DynamicDriveInfo
            {
                IsReady = drive.IsReady
            };

            if (!drive.IsReady)
                return;

            staticInfo.SystemDriveInfo.DriveFormat = SafeRead(() => drive.DriveFormat);
            staticInfo.SystemDriveInfo.VolumeLabel = SafeRead(() => drive.VolumeLabel);

            dynamicInfo.SystemDriveInfo.TotalSizeRaw = SafeRead(() => drive.TotalSize.ToString(CultureInfo.InvariantCulture));
            dynamicInfo.SystemDriveInfo.AvailableFreeSpaceRaw = SafeRead(() => drive.AvailableFreeSpace.ToString(CultureInfo.InvariantCulture));
            dynamicInfo.SystemDriveInfo.TotalFreeSpaceRaw = SafeRead(() => drive.TotalFreeSpace.ToString(CultureInfo.InvariantCulture));
        }
        catch
        {
        }
    }

    private static void PopulateSystemDriveStatic(StaticSystemInformation info, string systemDrive)
    {
        try
        {
            var drive = new DriveInfo(systemDrive);

            info.SystemDriveInfo = new StaticDriveInfo
            {
                Name = drive.Name,
                DriveType = drive.DriveType.ToString(),
                RootDirectory = drive.RootDirectory.FullName
            };

            if (!drive.IsReady)
                return;

            info.SystemDriveInfo.DriveFormat = SafeRead(() => drive.DriveFormat);
            info.SystemDriveInfo.VolumeLabel = SafeRead(() => drive.VolumeLabel);
        }
        catch
        {
        }
    }

    private static void PopulateSystemDriveDynamic(DynamicSystemInformation info, string systemDrive)
    {
        try
        {
            var drive = new DriveInfo(systemDrive);

            info.SystemDriveInfo = new DynamicDriveInfo
            {
                IsReady = drive.IsReady
            };

            if (!drive.IsReady)
                return;

            info.SystemDriveInfo.TotalSizeRaw = SafeRead(() => drive.TotalSize.ToString(CultureInfo.InvariantCulture));
            info.SystemDriveInfo.AvailableFreeSpaceRaw = SafeRead(() => drive.AvailableFreeSpace.ToString(CultureInfo.InvariantCulture));
            info.SystemDriveInfo.TotalFreeSpaceRaw = SafeRead(() => drive.TotalFreeSpace.ToString(CultureInfo.InvariantCulture));
        }
        catch
        {
        }
    }

    private static string ReadString(ManagementBaseObject obj, string propertyName)
    {
        try
        {
            return obj[propertyName]?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool ReadBool(ManagementBaseObject obj, string propertyName)
    {
        try
        {
            var value = obj[propertyName];
            return value != null && Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return false;
        }
    }

    private static string ReadRegistryString(RegistryKey key, string name)
    {
        try
        {
            return key.GetValue(name)?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string SafeRead(Func<string> getter)
    {
        try
        {
            return getter() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
