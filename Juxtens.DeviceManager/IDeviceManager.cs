namespace Juxtens.DeviceManager;

public interface IDeviceManager
{
    /// <summary>
    /// Finds a device by its instance ID and retrieves full device information.
    /// Enumerates all devices to match the ID and populate properties.
    /// </summary>
    Result<DeviceInfo, DeviceError> FindDevice(DeviceId id);
    
    /// <summary>
    /// Finds all devices matching the given predicate.
    /// Enumerates all present devices and applies the filter.
    /// </summary>
    Result<IReadOnlyList<DeviceInfo>, DeviceError> FindDevices(Func<DeviceInfo, bool> predicate);
    
    /// <summary>
    /// Gets the current state of a device.
    /// </summary>
    Result<DeviceState, DeviceError> GetState(DeviceId id);
    
    /// <summary>
    /// Enables a device. Requires administrator privileges.
    /// </summary>
    Result<Unit, DeviceError> Enable(DeviceId id);
    
    /// <summary>
    /// Disables a device politely. Requires administrator privileges.
    /// </summary>
    Result<Unit, DeviceError> Disable(DeviceId id);
    
    /// <summary>
    /// Restarts a device by performing a disable/enable cycle with state verification.
    /// Requires administrator privileges.
    /// </summary>
    Result<Unit, DeviceError> Restart(DeviceId id);
    
    /// <summary>
    /// Waits synchronously for a device to reach the target state, polling at configured interval.
    /// </summary>
    Result<DeviceState, DeviceError> WaitForState(DeviceId id, DeviceState targetState, TimeSpan? timeout = null);
    
    /// <summary>
    /// Waits asynchronously for a device to reach the target state, polling at configured interval.
    /// </summary>
    Task<Result<DeviceState, DeviceError>> WaitForStateAsync(DeviceId id, DeviceState targetState, TimeSpan? timeout = null, CancellationToken cancellationToken = default);
}
