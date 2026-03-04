namespace Juxtens.DeviceManager;

public sealed class DeviceInfo
{
    public DeviceId Id { get; }
    public string? FriendlyName { get; }
    public string? Description { get; }
    public string[]? HardwareIds { get; }

    public DeviceInfo(DeviceId id, string? friendlyName, string? description, string[]? hardwareIds)
    {
        Id = id;
        FriendlyName = friendlyName;
        Description = description;
        HardwareIds = hardwareIds;
    }
}
