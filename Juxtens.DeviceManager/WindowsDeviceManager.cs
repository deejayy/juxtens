using System.Runtime.InteropServices;
using System.Text;
using Juxtens.DeviceManager.Interop;
using Juxtens.Logger;

namespace Juxtens.DeviceManager;

public sealed class WindowsDeviceManager : IDeviceManager
{
    private readonly DeviceManagerConfig _config;
    private readonly ILogger _logger;

    public WindowsDeviceManager(DeviceManagerConfig config, ILogger logger)
    {
        _config = config;
        _logger = logger;
    }

    public Result<DeviceInfo, DeviceError> FindDevice(DeviceId id)
    {
        _logger.Info($"Finding device: {id}");
        
        var result = LocateDevNode(id.InstanceId);
        if (result.IsError)
            return Result<DeviceInfo, DeviceError>.Failure(result.Error);

        var hDevInfo = SetupApi.SetupDiGetClassDevsW(
            IntPtr.Zero,
            null,
            IntPtr.Zero,
            SetupApi.DIGCF_PRESENT | SetupApi.DIGCF_ALLCLASSES);

        if (hDevInfo == SetupApi.INVALID_HANDLE_VALUE)
        {
            var error = Marshal.GetLastWin32Error();
            _logger.Error($"SetupDiGetClassDevsW failed: 0x{error:X8}");
            return Result<DeviceInfo, DeviceError>.Failure(
                new DeviceError.WindowsApi((uint)error, "Failed to get device list"));
        }

        try
        {
            uint index = 0;
            while (true)
            {
                var devInfoData = new SetupApi.SP_DEVINFO_DATA { cbSize = SetupApi.SizeOfDevInfoData };
                if (!SetupApi.SetupDiEnumDeviceInfo(hDevInfo, index++, ref devInfoData))
                {
                    var error = (uint)Marshal.GetLastWin32Error();
                    if (error == SetupApi.ERROR_NO_MORE_ITEMS)
                        break;
                    continue;
                }

                var instanceId = GetDeviceInstanceId(hDevInfo, ref devInfoData);
                if (instanceId == null)
                    continue;

                if (string.Equals(instanceId, id.InstanceId, StringComparison.OrdinalIgnoreCase))
                {
                    var friendlyName = GetDeviceProperty(hDevInfo, ref devInfoData, SetupApi.SPDRP_FRIENDLYNAME);
                    var description = GetDeviceProperty(hDevInfo, ref devInfoData, SetupApi.SPDRP_DEVICEDESC);
                    var hardwareIds = GetDeviceMultiStringProperty(hDevInfo, ref devInfoData, SetupApi.SPDRP_HARDWAREID);

                    var deviceInfo = new DeviceInfo(id, friendlyName, description, hardwareIds);
                    _logger.Info($"Found device: {id}");
                    return Result<DeviceInfo, DeviceError>.Success(deviceInfo);
                }
            }

            _logger.Warning($"Device {id} exists in devnode but not in enumeration");
            var stubInfo = new DeviceInfo(id, null, null, null);
            return Result<DeviceInfo, DeviceError>.Success(stubInfo);
        }
        finally
        {
            SetupApi.SetupDiDestroyDeviceInfoList(hDevInfo);
        }
    }

    public Result<IReadOnlyList<DeviceInfo>, DeviceError> FindDevices(Func<DeviceInfo, bool> predicate)
    {
        _logger.Info("Enumerating all devices");
        var devices = new List<DeviceInfo>();
        var hDevInfo = SetupApi.SetupDiGetClassDevsW(
            IntPtr.Zero,
            null,
            IntPtr.Zero,
            SetupApi.DIGCF_PRESENT | SetupApi.DIGCF_ALLCLASSES);

        if (hDevInfo == SetupApi.INVALID_HANDLE_VALUE)
        {
            var error = Marshal.GetLastWin32Error();
            _logger.Error($"SetupDiGetClassDevsW failed: 0x{error:X8}");
            return Result<IReadOnlyList<DeviceInfo>, DeviceError>.Failure(
                new DeviceError.WindowsApi((uint)error, "Failed to get device list"));
        }

        try
        {
            uint index = 0;
            while (true)
            {
                var devInfoData = new SetupApi.SP_DEVINFO_DATA { cbSize = SetupApi.SizeOfDevInfoData };
                if (!SetupApi.SetupDiEnumDeviceInfo(hDevInfo, index++, ref devInfoData))
                {
                    var error = (uint)Marshal.GetLastWin32Error();
                    if (error == SetupApi.ERROR_NO_MORE_ITEMS)
                        break;
                    _logger.Warning($"SetupDiEnumDeviceInfo failed at index {index - 1}: 0x{error:X8}");
                    continue;
                }

                var instanceId = GetDeviceInstanceId(hDevInfo, ref devInfoData);
                if (instanceId == null)
                    continue;

                var friendlyName = GetDeviceProperty(hDevInfo, ref devInfoData, SetupApi.SPDRP_FRIENDLYNAME);
                var description = GetDeviceProperty(hDevInfo, ref devInfoData, SetupApi.SPDRP_DEVICEDESC);
                var hardwareIds = GetDeviceMultiStringProperty(hDevInfo, ref devInfoData, SetupApi.SPDRP_HARDWAREID);

                var deviceInfo = new DeviceInfo(
                    new DeviceId(instanceId),
                    friendlyName,
                    description,
                    hardwareIds);

                if (predicate(deviceInfo))
                    devices.Add(deviceInfo);
            }

            _logger.Info($"Found {devices.Count} matching devices");
            return Result<IReadOnlyList<DeviceInfo>, DeviceError>.Success(devices);
        }
        finally
        {
            SetupApi.SetupDiDestroyDeviceInfoList(hDevInfo);
        }
    }

    public Result<DeviceState, DeviceError> GetState(DeviceId id)
    {
        _logger.Info($"Getting state for device: {id}");
        var devNodeResult = LocateDevNode(id.InstanceId);
        if (devNodeResult.IsError)
        {
            if (devNodeResult.Error is DeviceError.NotFound)
                return Result<DeviceState, DeviceError>.Success(DeviceState.NotFound);
            return Result<DeviceState, DeviceError>.Failure(devNodeResult.Error);
        }

        var cr = CfgMgr32.CM_Get_DevNode_Status(out var status, out var problemNumber, devNodeResult.Value, 0);
        if (cr != CfgMgr32.CR_SUCCESS)
        {
            _logger.Error($"CM_Get_DevNode_Status failed: 0x{cr:X8}");
            return Result<DeviceState, DeviceError>.Failure(
                new DeviceError.WindowsApi(cr, "Failed to get device status"));
        }

        var state = DetermineDeviceState(status, problemNumber);
        _logger.Info($"Device {id} state: {state}");
        return Result<DeviceState, DeviceError>.Success(state);
    }

    public Result<Unit, DeviceError> Enable(DeviceId id)
    {
        _logger.Info($"Enabling device: {id}");
        var devNodeResult = LocateDevNode(id.InstanceId);
        if (devNodeResult.IsError)
            return Result<Unit, DeviceError>.Failure(devNodeResult.Error);

        var cr = CfgMgr32.CM_Enable_DevNode(devNodeResult.Value, 0);
        if (cr != CfgMgr32.CR_SUCCESS)
        {
            _logger.Error($"CM_Enable_DevNode failed: 0x{cr:X8}");
            return Result<Unit, DeviceError>.Failure(
                new DeviceError.WindowsApi(cr, "Failed to enable device"));
        }

        _logger.Info($"Device {id} enabled");
        return Result<Unit, DeviceError>.Success(Unit.Value);
    }

    public Result<Unit, DeviceError> Disable(DeviceId id)
    {
        _logger.Info($"Disabling device: {id}");
        var devNodeResult = LocateDevNode(id.InstanceId);
        if (devNodeResult.IsError)
            return Result<Unit, DeviceError>.Failure(devNodeResult.Error);

        var cr = CfgMgr32.CM_Disable_DevNode(devNodeResult.Value, CfgMgr32.CM_DISABLE_POLITE);
        if (cr != CfgMgr32.CR_SUCCESS)
        {
            _logger.Error($"CM_Disable_DevNode failed: 0x{cr:X8}");
            return Result<Unit, DeviceError>.Failure(
                new DeviceError.WindowsApi(cr, "Failed to disable device"));
        }

        _logger.Info($"Device {id} disabled");
        return Result<Unit, DeviceError>.Success(Unit.Value);
    }

    public Result<Unit, DeviceError> Restart(DeviceId id)
    {
        _logger.Info($"Restarting device (disable/enable cycle): {id}");
        
        var disableResult = Disable(id);
        if (disableResult.IsError)
            return disableResult;

        var waitDisabled = WaitForState(id, DeviceState.Disabled, TimeSpan.FromSeconds(5));
        if (waitDisabled.IsError)
        {
            _logger.Warning($"Device {id} did not reach disabled state, proceeding anyway");
        }

        var enableResult = Enable(id);
        if (enableResult.IsError)
            return enableResult;

        var waitEnabled = WaitForState(id, DeviceState.Enabled, TimeSpan.FromSeconds(10));
        if (waitEnabled.IsError)
        {
            _logger.Warning($"Device {id} did not reach enabled state within timeout");
        }

        _logger.Info($"Device {id} restarted successfully");
        return Result<Unit, DeviceError>.Success(Unit.Value);
    }

    public Result<DeviceState, DeviceError> WaitForState(DeviceId id, DeviceState targetState, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? _config.DefaultTimeout;
        _logger.Info($"Waiting for device {id} to reach state {targetState} (timeout: {effectiveTimeout.TotalSeconds}s)");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (stopwatch.Elapsed < effectiveTimeout)
        {
            var stateResult = GetState(id);
            if (stateResult.IsError)
                return stateResult;

            if (stateResult.Value == targetState)
            {
                _logger.Info($"Device {id} reached target state {targetState} after {stopwatch.Elapsed.TotalSeconds:F1}s");
                return stateResult;
            }

            Thread.Sleep(_config.PollInterval);
        }

        _logger.Warning($"Timeout waiting for device {id} to reach state {targetState}");
        return Result<DeviceState, DeviceError>.Failure(new DeviceError.Timeout(stopwatch.Elapsed));
    }

    public async Task<Result<DeviceState, DeviceError>> WaitForStateAsync(
        DeviceId id, 
        DeviceState targetState, 
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveTimeout = timeout ?? _config.DefaultTimeout;
        _logger.Info($"Waiting asynchronously for device {id} to reach state {targetState} (timeout: {effectiveTimeout.TotalSeconds}s)");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (stopwatch.Elapsed < effectiveTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stateResult = GetState(id);
            if (stateResult.IsError)
                return stateResult;

            if (stateResult.Value == targetState)
            {
                _logger.Info($"Device {id} reached target state {targetState} after {stopwatch.Elapsed.TotalSeconds:F1}s");
                return stateResult;
            }

            await Task.Delay(_config.PollInterval, cancellationToken);
        }

        _logger.Warning($"Timeout waiting for device {id} to reach state {targetState}");
        return Result<DeviceState, DeviceError>.Failure(new DeviceError.Timeout(stopwatch.Elapsed));
    }

    private Result<uint, DeviceError> LocateDevNode(string instanceId)
    {
        var cr = CfgMgr32.CM_Locate_DevNodeW(out var devInst, instanceId, CfgMgr32.CM_LOCATE_DEVNODE_NORMAL);
        return cr switch
        {
            CfgMgr32.CR_SUCCESS => Result<uint, DeviceError>.Success(devInst),
            CfgMgr32.CR_NO_SUCH_DEVNODE => Result<uint, DeviceError>.Failure(
                new DeviceError.NotFound(new DeviceId(instanceId))),
            CfgMgr32.CR_INVALID_DEVICE_ID => Result<uint, DeviceError>.Failure(
                new DeviceError.InvalidId(new DeviceId(instanceId))),
            CfgMgr32.CR_ACCESS_DENIED => Result<uint, DeviceError>.Failure(
                new DeviceError.PermissionDenied()),
            _ => Result<uint, DeviceError>.Failure(
                new DeviceError.WindowsApi(cr, "Failed to locate device node"))
        };
    }

    private static DeviceState DetermineDeviceState(uint status, uint problemNumber)
    {
        if ((status & CfgMgr32.DN_HAS_PROBLEM) != 0)
        {
            return problemNumber switch
            {
                CfgMgr32.CM_PROB_DISABLED => DeviceState.Disabled,
                _ => DeviceState.Problem(problemNumber)
            };
        }
        
        if ((status & CfgMgr32.DN_STARTED) != 0)
            return DeviceState.Enabled;
            
        return DeviceState.Disabled;
    }

    private string? GetDeviceInstanceId(IntPtr hDevInfo, ref SetupApi.SP_DEVINFO_DATA devInfoData)
    {
        const int maxInstanceIdLength = 256;
        var buffer = new char[maxInstanceIdLength];
        if (!SetupApi.SetupDiGetDeviceInstanceIdW(hDevInfo, ref devInfoData, buffer, (uint)buffer.Length, out _))
            return null;
        return new string(buffer).TrimEnd('\0');
    }

    private string? GetDeviceProperty(IntPtr hDevInfo, ref SetupApi.SP_DEVINFO_DATA devInfoData, uint property)
    {
        const int maxPropertySize = 65536;
        
        if (!SetupApi.SetupDiGetDeviceRegistryPropertyW(
            hDevInfo, ref devInfoData, property, out _, null, 0, out var requiredSize))
        {
            if (requiredSize == 0)
                return null;
        }

        if (requiredSize > maxPropertySize)
        {
            _logger.Warning($"Device property size {requiredSize} exceeds maximum {maxPropertySize}, truncating");
            requiredSize = maxPropertySize;
        }

        var buffer = new byte[requiredSize];
        if (!SetupApi.SetupDiGetDeviceRegistryPropertyW(
            hDevInfo, ref devInfoData, property, out _, buffer, requiredSize, out _))
        {
            return null;
        }

        return Encoding.Unicode.GetString(buffer).TrimEnd('\0');
    }

    private string[]? GetDeviceMultiStringProperty(IntPtr hDevInfo, ref SetupApi.SP_DEVINFO_DATA devInfoData, uint property)
    {
        const int maxPropertySize = 65536;
        
        if (!SetupApi.SetupDiGetDeviceRegistryPropertyW(
            hDevInfo, ref devInfoData, property, out _, null, 0, out var requiredSize))
        {
            if (requiredSize == 0)
                return null;
        }

        if (requiredSize > maxPropertySize)
        {
            _logger.Warning($"Device multi-string property size {requiredSize} exceeds maximum {maxPropertySize}, truncating");
            requiredSize = maxPropertySize;
        }

        var buffer = new byte[requiredSize];
        if (!SetupApi.SetupDiGetDeviceRegistryPropertyW(
            hDevInfo, ref devInfoData, property, out _, buffer, requiredSize, out _))
        {
            return null;
        }

        var text = Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        return text.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
    }
}
