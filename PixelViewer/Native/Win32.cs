using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Carina.PixelViewer.Native;

/// <summary>
/// Native functions and types for Windows.
/// </summary>
static class Win32
{
    // Constants.
    const string Gdi32Lib = "Gdi32";
    const string Kernel32Lib = "Kernel32";
    const string MscmsLib = "Mscms";
    const string User32Lib = "User32";


    public const int CCHDEVICENAME = 32;


    public enum CLASS : uint
    {
        MONITOR = 0x6D6E7472,
        PRINTER = 0x70727472,
        SCANNER = 0x73636E72,
    }


    public enum COLORPROFILESUBTYPE
    {
       PERCEPTUAL,
       RELATIVE_COLORIMETRIC,
       SATURATION,
       ABSOLUTE_COLORIMETRIC,
       NONE,
       RGB_WORKING_SPACE,
       CUSTOM_WORKING_SPACE,
       STANDARD_DISPLAY_COLOR_MODE,
       EXTENDED_DISPLAY_COLOR_MODE
    }


    public enum COLORPROFILETYPE
    {
        ICC,
        DMP,
        CAMP,
        GMMP
    }


    [StructLayout(LayoutKind.Sequential)]
    public struct DISPLAY_DEVICE
    {
        public uint cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceString;
        public DISPLAY_DEVICE_FLAGS StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string DeviceKey;
    }


    [Flags]
    public enum DISPLAY_DEVICE_FLAGS : uint
    {
        ACTIVE = 0x1,
        ATTACHED_TO_DESKTOP = 0x1,
        ATTACHED = 0x2,
        PRIMARY_DEVICE = 0x4,
    }


    public enum EDD : uint
    {
        GET_DEVICE_INTERFACE_NAME = 0x1,
    }


    public enum ERROR : uint
    {
        SUCCESS = 0,
        FILE_NOT_FOUND = 2,
    }


    public enum FILE_MODE : uint
    {
        CREATE_NEW = 1,
        CREATE_ALWAYS = 2,
        OPEN_EXISTING = 3,
        OPEN_ALWAYS = 4,
        TRUNCATE_EXISTING = 5,
    }


    public enum FILE_SHARE : uint
    {
        NONE = 0,
        READ = 1,
        WRITE = 2,
    }


    [Flags]
    public enum MONITOR : uint
    {
        DEFAULTTONULL = 0x0,
        DEFAULTTOPRIMARY = 0x1,
        DEFAULTTONEAREST = 0x2,
    }


    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFOEX
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public MONITORINFOF dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
        public string szDevice;
    }


    [Flags]
    public enum MONITORINFOF : uint
    {
        PRIMARY = 0x1,
    }


    [StructLayout(LayoutKind.Sequential)]
    public struct PROFILE
    {
        public PROFILE_TYPE dwType;
        public IntPtr pProfileData;
        public uint cbDataSize;
    }


    public enum PROFILE_ACCESS : uint
    {
        READ = 1,
        READWRITE = 2,
    }


    public enum PROFILE_TYPE : uint
    {
        FILENAME = 1,
        MEMBUFFER = 2,
    }


    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }


    public enum WCS_PROFILE_MANAGEMENT_SCOPE
    {
        SYSTEM_WIDE,
        CURRENT_USER
    }


    [DllImport(MscmsLib, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CloseColorProfile(IntPtr hProfile);


    [DllImport(User32Lib, SetLastError = true)]
    public static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, EDD dwFlags);


    [DllImport(MscmsLib, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool GetColorDirectory(string? pMachineName, StringBuilder pBuffer, ref uint pdwSize);


    [DllImport(User32Lib, SetLastError = true)]
    public static extern IntPtr GetDC(IntPtr hWnd);


    [DllImport(Gdi32Lib, SetLastError = true)]
    public static extern bool GetICMProfile(IntPtr hdc, ref uint pBufSize, StringBuilder pszFilename);


    [DllImport(Kernel32Lib)]
    public static extern ERROR GetLastError();


    [DllImport(User32Lib, SetLastError = true)]
    public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);


    [DllImport(User32Lib, SetLastError = true)]
    public static extern IntPtr GetWindowDC(IntPtr hWnd);


    [DllImport(Kernel32Lib, SetLastError = true)]
    public static extern IntPtr LocalFree(IntPtr hMem);


    [DllImport(User32Lib, SetLastError = true)]
    public static extern IntPtr MonitorFromRect(ref RECT lprc, MONITOR dwFlags);


    [DllImport(MscmsLib, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr OpenColorProfile(ref PROFILE pProfile, PROFILE_ACCESS dwDesiredAccess, FILE_SHARE dwShareMode, FILE_MODE dwCreationMode);


    [DllImport(User32Lib, SetLastError = true)]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);


    [DllImport(MscmsLib, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool WcsGetDefaultColorProfile(WCS_PROFILE_MANAGEMENT_SCOPE scope, string? pDeviceName, COLORPROFILETYPE cptColorProfileType, COLORPROFILESUBTYPE cpstColorProfileSubType, uint dwProfileID, uint cbProfileName, StringBuilder pProfileName);


    [DllImport(MscmsLib, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool WcsGetDefaultColorProfileSize(WCS_PROFILE_MANAGEMENT_SCOPE scope, string? pDeviceName, COLORPROFILETYPE cptColorProfileType, COLORPROFILESUBTYPE cpstColorProfileSubType, uint dwProfileID, out uint pcbProfileName);


    [DllImport(MscmsLib, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool WcsGetUsePerUserProfiles(string? pDeviceName, CLASS dwDeviceClass, out bool pUsePerUserProfiles);
}
