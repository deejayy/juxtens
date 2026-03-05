using Juxtens.DeviceManager;
using Juxtens.DeviceManager.Predicates;
using Juxtens.Logger;

namespace Juxtens.VDDControl;

public sealed class VDDController : IVDDController
{
    private readonly IDeviceManager _deviceManager;
    private readonly VDDConfig _config;
    private readonly ILogger _logger;
    private readonly DeviceId? _knownDeviceId;

    public VDDController(
        IDeviceManager deviceManager, 
        ILogger logger, 
        string? configPath = null,
        DeviceId? knownDeviceId = null)
    {
        _deviceManager = deviceManager;
        _logger = logger;
        _config = new VDDConfig(logger, configPath);
        _knownDeviceId = knownDeviceId;
    }

    public void Dispose()
    {
        _config.Dispose();
    }

    public bool IsDriverInstalled()
    {
        _logger.Info("Checking if VDD driver is installed");
        var findResult = _deviceManager.FindDevices(DevicePredicates.VirtualDisplay());
        
        return findResult.Match(
            devices =>
            {
                var installed = devices.Count > 0;
                _logger.Info($"VDD driver installed: {installed}");
                return installed;
            },
            error =>
            {
                _logger.Warning($"Failed to check VDD installation: {error.Message}");
                return false;
            });
    }

    public Result<uint, VDDError> GetCurrentCount()
    {
        return _config.GetMonitorCount();
    }

    public Result<uint, VDDError> GetEffectiveCount()
    {
        var deviceResult = LocateVDDDevice();
        if (deviceResult.IsError)
            return Result<uint, VDDError>.Failure(deviceResult.Error);

        var deviceId = deviceResult.Value;
        var stateResult = _deviceManager.GetState(deviceId);
        
        return stateResult.Match(
            state =>
            {
                if (state.Kind == DeviceStateKind.Disabled || state.Kind == DeviceStateKind.NotFound)
                {
                    return Result<uint, VDDError>.Success(0);
                }
                
                var configResult = _config.GetMonitorCount();
                return configResult.Match(
                    configCount =>
                    {
                        if (configCount == 0)
                        {
                            _logger.Warning("VDD driver enabled with config count=0, driver creates 1 display (driver bug)");
                            return Result<uint, VDDError>.Success(1);
                        }
                        return Result<uint, VDDError>.Success(configCount);
                    },
                    error => Result<uint, VDDError>.Failure(error));
            },
            error => Result<uint, VDDError>.Failure(
                new VDDError.DeviceOperationFailed("GetState", error.Message)));
    }

    public Result<Unit, VDDError> SetVirtualDisplayCount(uint count)
    {
        const uint MaxCount = 10;
        
        if (count > MaxCount)
        {
            _logger.Error($"Invalid count: {count} exceeds maximum {MaxCount}");
            return Result<Unit, VDDError>.Failure(new VDDError.InvalidCount((int)count));
        }

        _logger.Info($"SetVirtualDisplayCount called with count={count}");

        var deviceResult = LocateVDDDevice();
        if (deviceResult.IsError)
            return Result<Unit, VDDError>.Failure(deviceResult.Error);

        var deviceId = deviceResult.Value;

        if (count == 0)
        {
            _logger.Info("Count=0 requested, disabling device without writing config");
            return DisableDevice(deviceId);
        }
        
        var configResult = _config.SetMonitorCount(count);
        if (configResult.IsError)
            return Result<Unit, VDDError>.Failure(configResult.Error);

        return EnableOrRestartDevice(deviceId);
    }

    private Result<DeviceId, VDDError> LocateVDDDevice()
    {
        if (_knownDeviceId.HasValue)
        {
            _logger.Info($"Using known device ID: {_knownDeviceId.Value}");
            
            var stateResult = _deviceManager.GetState(_knownDeviceId.Value);
            return stateResult.Match(
                state =>
                {
                    if (state.Kind == DeviceStateKind.NotFound)
                    {
                        _logger.Error($"Known device ID {_knownDeviceId.Value} not found, falling back to search");
                    }
                    else
                    {
                        return Result<DeviceId, VDDError>.Success(_knownDeviceId.Value);
                    }
                    
                    return LocateViaSearch();
                },
                error =>
                {
                    _logger.Warning($"Failed to verify known device ID: {error.Message}, falling back to search");
                    return LocateViaSearch();
                });
        }

        return LocateViaSearch();
    }

    private Result<DeviceId, VDDError> LocateViaSearch()
    {
        _logger.Info("Locating VDD device via predicate");
        var findResult = _deviceManager.FindDevices(DevicePredicates.VirtualDisplay());
        
        return findResult.Match(
            devices =>
            {
                if (devices.Count == 0)
                {
                    _logger.Error("No virtual display devices found");
                    return Result<DeviceId, VDDError>.Failure(new VDDError.DeviceNotFound());
                }

                var device = devices[0];
                _logger.Info($"Found VDD device: {device.Id}");
                return Result<DeviceId, VDDError>.Success(device.Id);
            },
            error =>
            {
                _logger.Error($"Device enumeration failed: {error.Message}");
                return Result<DeviceId, VDDError>.Failure(
                    new VDDError.DeviceOperationFailed("FindDevices", error.Message));
            });
    }

    private Result<Unit, VDDError> DisableDevice(DeviceId deviceId)
    {
        _logger.Info($"Ensuring device {deviceId} is disabled");

        var stateResult = _deviceManager.GetState(deviceId);
        
        return stateResult.Match(
            state =>
            {
                if (state.Kind == DeviceStateKind.NotFound)
                {
                    _logger.Error($"Device {deviceId} not found");
                    return Result<Unit, VDDError>.Failure(new VDDError.DeviceNotFound());
                }

                if (state.Kind == DeviceStateKind.Disabled)
                {
                    _logger.Info($"Device {deviceId} already disabled");
                    return Result<Unit, VDDError>.Success(Unit.Value);
                }

                _logger.Info($"Disabling device {deviceId} (current state: {state})");
                var disableResult = _deviceManager.Disable(deviceId);
                
                return disableResult.Match(
                    _ => Result<Unit, VDDError>.Success(Unit.Value),
                    error => Result<Unit, VDDError>.Failure(
                        new VDDError.DeviceOperationFailed("Disable", error.Message)));
            },
            error => Result<Unit, VDDError>.Failure(
                new VDDError.DeviceOperationFailed("GetState", error.Message)));
    }

    private Result<Unit, VDDError> EnableOrRestartDevice(DeviceId deviceId)
    {
        _logger.Info($"Ensuring device {deviceId} is enabled");

        var stateResult = _deviceManager.GetState(deviceId);
        
        return stateResult.Match(
            state =>
            {
                switch (state.Kind)
                {
                    case DeviceStateKind.NotFound:
                        _logger.Error($"Device {deviceId} not found");
                        return Result<Unit, VDDError>.Failure(new VDDError.DeviceNotFound());

                    case DeviceStateKind.Disabled:
                        _logger.Info($"Enabling device {deviceId}");
                        var enableResult = _deviceManager.Enable(deviceId);
                        return enableResult.Match(
                            _ =>
                            {
                                var waitResult = _deviceManager.WaitForState(
                                    deviceId, 
                                    DeviceState.Enabled, 
                                    TimeSpan.FromSeconds(10));
                                
                                if (waitResult.IsError)
                                    _logger.Warning($"Device may not be fully enabled: {waitResult.Error.Message}");
                                
                                return Result<Unit, VDDError>.Success(Unit.Value);
                            },
                            error => Result<Unit, VDDError>.Failure(
                                new VDDError.DeviceOperationFailed("Enable", error.Message)));

                    case DeviceStateKind.Enabled:
                        _logger.Info($"Restarting device {deviceId} to apply new config");
                        var restartResult = _deviceManager.Restart(deviceId);
                        return restartResult.Match(
                            _ =>
                            {
                                var waitResult = _deviceManager.WaitForState(
                                    deviceId, 
                                    DeviceState.Enabled, 
                                    TimeSpan.FromSeconds(10));
                                
                                if (waitResult.IsError)
                                    _logger.Warning($"Device may not be fully enabled after restart: {waitResult.Error.Message}");
                                
                                return Result<Unit, VDDError>.Success(Unit.Value);
                            },
                            error => Result<Unit, VDDError>.Failure(
                                new VDDError.DeviceOperationFailed("Restart", error.Message)));

                    case DeviceStateKind.Problem:
                        _logger.Info($"Device {deviceId} has problem (code: {state.ProblemCode}), attempting restart");
                        var problemRestartResult = _deviceManager.Restart(deviceId);
                        return problemRestartResult.Match(
                            _ =>
                            {
                                var waitResult = _deviceManager.WaitForState(
                                    deviceId, 
                                    DeviceState.Enabled, 
                                    TimeSpan.FromSeconds(10));
                                
                                if (waitResult.IsError)
                                    _logger.Warning($"Device may not be fully enabled after restart: {waitResult.Error.Message}");
                                
                                return Result<Unit, VDDError>.Success(Unit.Value);
                            },
                            error => Result<Unit, VDDError>.Failure(
                                new VDDError.DeviceOperationFailed("Restart", error.Message)));

                    default:
                        _logger.Error($"Device {deviceId} in unknown state: {state}, cannot enable");
                        return Result<Unit, VDDError>.Failure(
                            new VDDError.DeviceOperationFailed("EnableOrRestart", $"Unknown device state: {state}"));
                }
            },
            error => Result<Unit, VDDError>.Failure(
                new VDDError.DeviceOperationFailed("GetState", error.Message)));
    }
}
