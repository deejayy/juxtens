using Juxtens.DeviceManager;

namespace Juxtens.VDDControl;

public interface IVDDController : IDisposable
{
    /// <summary>
    /// Checks if the VDD driver is installed on the system.
    /// Returns true if at least one VDD device is found.
    /// </summary>
    bool IsDriverInstalled();

    /// <summary>
    /// Gets the current virtual display count from VDD configuration.
    /// This is the persisted count in the XML file, regardless of device state.
    /// Note: Config may contain 0 due to 3rd-party modification, which is invalid.
    /// </summary>
    Result<uint, VDDError> GetCurrentCount();
    
    /// <summary>
    /// Gets the effective virtual display count based on device state and driver behavior.
    /// Returns 0 if device is disabled.
    /// Returns 1 if device is enabled but config is 0 (driver bug: VDD creates 1 display when count=0).
    /// Otherwise returns the configured count.
    /// This reflects the actual number of virtual displays available to the system.
    /// </summary>
    Result<uint, VDDError> GetEffectiveCount();
    
    /// <summary>
    /// Sets the virtual display count and applies the change by managing device state.
    /// If count is 0, disables the VDD device (does not write 0 to config, as driver ignores it).
    /// If count > 0 and device is disabled, writes config and enables device.
    /// If count > 0 and device is enabled, writes config and restarts device to apply.
    /// Maximum count is 10. Minimum is 1 (0 is invalid due to driver bug).
    /// </summary>
    Result<Unit, VDDError> SetVirtualDisplayCount(uint count);
}
