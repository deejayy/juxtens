using System.Runtime.InteropServices;

namespace Juxtens.DeviceManager.Interop;

internal static class SetupApi
{
    private const string DllName = "setupapi.dll";

    [DllImport(DllName, CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SetupDiGetClassDevsW(
        IntPtr classGuid,
        string? enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport(DllName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet,
        uint memberIndex,
        ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport(DllName, CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceInstanceIdW(
        IntPtr deviceInfoSet,
        ref SP_DEVINFO_DATA deviceInfoData,
        char[] deviceInstanceId,
        uint deviceInstanceIdSize,
        out uint requiredSize);

    [DllImport(DllName, CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceRegistryPropertyW(
        IntPtr deviceInfoSet,
        ref SP_DEVINFO_DATA deviceInfoData,
        uint property,
        out uint propertyRegDataType,
        byte[]? propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize);

    [DllImport(DllName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SP_DEVINFO_DATA
    {
        internal uint cbSize;
        internal Guid ClassGuid;
        internal uint DevInst;
        internal IntPtr Reserved;
    }

    internal static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    internal const uint DIGCF_PRESENT = 0x00000002;
    internal const uint DIGCF_ALLCLASSES = 0x00000004;

    internal const uint SPDRP_FRIENDLYNAME = 0x0000000C;
    internal const uint SPDRP_DEVICEDESC = 0x00000000;
    internal const uint SPDRP_HARDWAREID = 0x00000001;

    internal const uint ERROR_NO_MORE_ITEMS = 259;
    internal const uint ERROR_INSUFFICIENT_BUFFER = 122;

    internal static uint SizeOfDevInfoData => (uint)Marshal.SizeOf<SP_DEVINFO_DATA>();
}
