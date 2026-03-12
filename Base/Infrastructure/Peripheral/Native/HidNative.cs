// Base.Services.HIDService.Native/HidNative.cs
// net8.0-windows10.0.26100.0
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace Base.Services.Peripheral.Native
{
    internal static class HidNative
    {
        // ========= enums =========

        internal enum HIDP_REPORT_TYPE : short
        {
            HidP_Input = 0,
            HidP_Output = 1,
            HidP_Feature = 2
        }

        // ========= structs (hidpi.h compatible layouts) =========

        [StructLayout(LayoutKind.Sequential)]
        internal struct HIDP_RANGE
        {
            public ushort UsageMax;
            public ushort UsageMin;
            public ushort StringMax;
            public ushort StringMin;
            public ushort DesignatorMax;
            public ushort DesignatorMin;
            public ushort DataIndexMax;
            public ushort DataIndexMin;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HIDP_NOT_RANGE
        {
            public ushort Usage;
            public ushort Reserved1;
            public ushort StringIndex;
            public ushort Reserved2;
            public ushort DesignatorIndex;
            public ushort Reserved3;
            public ushort DataIndex;
            public ushort Reserved4;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HIDP_BUTTON_CAPS
        {
            public ushort UsagePage;
            public byte ReportID;
            public byte IsAlias;

            public ushort BitField;
            public ushort LinkCollection;
            public ushort LinkUsage;
            public ushort LinkUsagePage;

            public byte IsRange;
            public byte IsStringRange;
            public byte IsDesignatorRange;
            public byte IsAbsolute;

            public ushort ReportCount;
            public uint Reserved;

            // Ten ULONG in header; keep size parity on both x86/x64
            public uint Reserved2_0; public uint Reserved2_1; public uint Reserved2_2; public uint Reserved2_3; public uint Reserved2_4;
            public uint Reserved2_5; public uint Reserved2_6; public uint Reserved2_7; public uint Reserved2_8; public uint Reserved2_9;

            public HIDP_RANGE Range;
            public HIDP_NOT_RANGE NotRange;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HIDP_VALUE_CAPS
        {
            public ushort UsagePage;
            public byte ReportID;
            public byte IsAlias;

            public ushort BitField;
            public ushort LinkCollection;
            public ushort LinkUsage;
            public ushort LinkUsagePage;

            public byte IsRange;
            public byte IsStringRange;
            public byte IsDesignatorRange;
            public byte IsAbsolute;

            public byte HasNull;
            public byte Reserved;

            public ushort BitSize;
            public ushort ReportCount;

            public ushort Reserved2_0;
            public ushort Reserved2_1;
            public ushort Reserved2_2;

            public uint UnitsExp;
            public uint Units;

            public int LogicalMin;
            public int LogicalMax;

            public int PhysicalMin;
            public int PhysicalMax;

            public HIDP_RANGE Range;
            public HIDP_NOT_RANGE NotRange;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public nint Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SP_DEVICE_INTERFACE_DATA
        {
            public uint cbSize;
            public Guid InterfaceClassGuid;
            public uint Flags;
            public nint Reserved;
        }

        // NOTE: Use CharSet.Auto with ByValTStr to keep structure size stable
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        internal struct SP_DEVICE_INTERFACE_DETAIL_DATA
        {
            public int cbSize;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string DevicePath;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HIDD_ATTRIBUTES
        {
            public int Size;
            public short VendorID;
            public short ProductID;
            public short VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct COMMTIMEOUTS
        {
            public uint ReadIntervalTimeout;
            public uint ReadTotalTimeoutMultiplier;
            public uint ReadTotalTimeoutConstant;
            public uint WriteTotalTimeoutMultiplier;
            public uint WriteTotalTimeoutConstant;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct HIDP_LINK_COLLECTION_NODE
        {
            public ushort LinkUsage;          // 0x05 = Game Pad, 0x04 = Joystick (on page 0x01)
            public ushort LinkUsagePage;      // 0x01 = Generic Desktop
            public ushort Parent;             // 0 => top-level collection (TLC)
            public ushort NumberOfChildren;
            public ushort NextSibling;
            public ushort FirstChild;
            public uint CollectionType;     // HIDP_COLLECTION_TYPE
            public byte IsAlias;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
            public ushort[] Reserved;
        }

        // ========= hid.dll =========

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern void HidD_GetHidGuid(ref Guid hidGuid);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern bool HidD_GetPreparsedData(IntPtr hObject, ref IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern bool HidD_FreePreparsedData(ref IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern bool HidD_GetPreparsedData(SafeFileHandle hObject, ref nint PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern int HidP_GetCaps(nint pPHIDP_PREPARSED_DATA, ref HIDP_CAPS myPHIDP_CAPS);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern bool HidD_GetAttributes(SafeFileHandle hObject, ref HIDD_ATTRIBUTES Attributes);

        [DllImport("hid.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        internal static extern bool HidD_GetFeature(nint hDevice, nint hReportBuffer, uint ReportBufferLength);

        [DllImport("hid.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        internal static extern bool HidD_SetFeature(nint hDevice, nint ReportBuffer, uint ReportBufferLength);

        [DllImport("hid.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        internal static extern bool HidD_GetProductString(SafeFileHandle hDevice, IntPtr Buffer, uint BufferLength);

        [DllImport("hid.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        internal static extern bool HidD_GetSerialNumberString(SafeFileHandle hDevice, IntPtr Buffer, uint BufferLength);

        [DllImport("hid.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        internal static extern bool HidD_GetManufacturerString(SafeFileHandle hDevice, IntPtr Buffer, uint BufferLength);

        [DllImport("hid.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        internal static extern bool HidD_SetOutputReport(SafeFileHandle hDevice, IntPtr Buffer, uint ReportBufferLength);

        [DllImport("hid.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall)]
        internal static extern bool HidD_GetInputReport(SafeFileHandle hDevice, IntPtr Buffer, uint ReportBufferLength);

        [DllImport("hid.dll")]
        internal static extern int HidP_GetLinkCollectionNodes(
            [Out] HIDP_LINK_COLLECTION_NODE[] linkCollectionNodes,
            ref uint linkCollectionNodesLength,
            IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern int HidP_GetButtonCaps(
            HIDP_REPORT_TYPE reportType,
            [Out] HIDP_BUTTON_CAPS[] buttonCaps,
            ref ushort buttonCapsLength,
            IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern int HidP_GetValueCaps(
            HIDP_REPORT_TYPE reportType,
            [Out] HIDP_VALUE_CAPS[] valueCaps,
            ref ushort valueCapsLength,
            IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern int HidP_MaxUsageListLength(
            HIDP_REPORT_TYPE reportType,
            ushort usagePage,
            IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern int HidP_GetUsages(
            HIDP_REPORT_TYPE reportType,
            ushort usagePage,
            ushort linkCollection,
            [Out] ushort[] usageList,
            ref uint usageLength,
            IntPtr preparsedData,
            [In] byte[] report,
            uint reportLength);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern int HidP_GetUsageValue(
            HIDP_REPORT_TYPE reportType,
            ushort usagePage,
            ushort linkCollection,
            ushort usage,
            out int usageValue,
            IntPtr preparsedData,
            [In] byte[] report,
            uint reportLength);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern int HidP_GetScaledUsageValue(
            HIDP_REPORT_TYPE reportType,
            ushort usagePage,
            ushort linkCollection,
            ushort usage,
            out int scaledValue,
            IntPtr preparsedData,
            [In] byte[] report,
            uint reportLength);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern int HidP_GetSpecificValueCaps(
            HIDP_REPORT_TYPE reportType,
            ushort usagePage,
            ushort linkCollection,
            ushort usage,
            [Out] HIDP_VALUE_CAPS[] valueCaps,
            ref ushort valueCapsLength,
            IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern int HidP_GetSpecificButtonCaps(
            HIDP_REPORT_TYPE reportType,
            ushort usagePage,
            ushort linkCollection,
            ushort usage,
            [Out] HIDP_BUTTON_CAPS[] buttonCaps,
            ref ushort buttonCapsLength,
            IntPtr preparsedData);

        // ========= setupapi.dll =========

        private const string SETUPAPI = "setupapi.dll";

        [DllImport(SETUPAPI, SetLastError = true, CharSet = CharSet.Auto)]
        internal static extern IntPtr SetupDiGetClassDevs(
            ref Guid ClassGuid,
            IntPtr Enumerator,
            IntPtr hwndParent,
            uint Flags);

        [DllImport(SETUPAPI, SetLastError = true)]
        internal static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr DeviceInfoSet,
            IntPtr DeviceInfoData,
            ref Guid InterfaceClassGuid,
            uint MemberIndex,
            ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

        [DllImport(SETUPAPI, SetLastError = true, CharSet = CharSet.Auto)]
        internal static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr DeviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
            ref SP_DEVICE_INTERFACE_DETAIL_DATA DeviceInterfaceDetailData,
            int DeviceInterfaceDetailDataSize,
            out int RequiredSize,
            ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport(SETUPAPI, SetLastError = true)]
        internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        // ========= kernel32.dll =========

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        internal static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);
    }
}
