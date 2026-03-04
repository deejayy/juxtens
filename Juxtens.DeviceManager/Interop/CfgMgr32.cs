using System.Runtime.InteropServices;

namespace Juxtens.DeviceManager.Interop;

internal static class CfgMgr32
{
    private const string DllName = "cfgmgr32.dll";

    [DllImport(DllName, CharSet = CharSet.Unicode)]
    internal static extern uint CM_Locate_DevNodeW(
        out uint devInst,
        string? deviceId,
        uint flags);

    [DllImport(DllName)]
    internal static extern uint CM_Get_DevNode_Status(
        out uint status,
        out uint problemNumber,
        uint devInst,
        uint flags);

    [DllImport(DllName)]
    internal static extern uint CM_Enable_DevNode(
        uint devInst,
        uint flags);

    [DllImport(DllName)]
    internal static extern uint CM_Disable_DevNode(
        uint devInst,
        uint flags);

    [DllImport(DllName)]
    internal static extern uint CM_Reenumerate_DevNode(
        uint devInst,
        uint flags);

    [DllImport(DllName)]
    internal static extern uint CM_Query_And_Remove_SubTreeW(
        uint devInst,
        out int vetoType,
        IntPtr vetoName,
        uint vetoNameLength,
        uint flags);

    internal const uint CR_SUCCESS = 0x00000000;
    internal const uint CR_NO_SUCH_DEVNODE = 0x0000000D;
    internal const uint CR_ACCESS_DENIED = 0x00000033;
    internal const uint CR_INVALID_DEVICE_ID = 0x00000003;

    internal const uint CM_LOCATE_DEVNODE_NORMAL = 0x00000000;
    internal const uint CM_DISABLE_POLITE = 0x00000000;
    internal const uint CM_DISABLE_ABSOLUTE = 0x00000001;
    internal const uint CM_REENUMERATE_NORMAL = 0x00000000;

    internal const uint DN_STARTED = 0x00000008;
    internal const uint DN_HAS_PROBLEM = 0x00000400;

    internal const uint CM_PROB_DISABLED = 0x00000016;
    internal const uint CM_PROB_FAILED_START = 0x0000000A;
    internal const uint CM_PROB_NOT_CONFIGURED = 0x00000001;
}
