namespace Juxtens.DeviceManager;

public readonly struct DeviceId : IEquatable<DeviceId>
{
    public string InstanceId { get; }

    public DeviceId(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        InstanceId = instanceId;
    }

    public bool Equals(DeviceId other) => 
        string.Equals(InstanceId, other.InstanceId, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is DeviceId other && Equals(other);

    public override int GetHashCode() => 
        StringComparer.OrdinalIgnoreCase.GetHashCode(InstanceId);

    public override string ToString() => InstanceId;

    public static bool operator ==(DeviceId left, DeviceId right) => left.Equals(right);
    public static bool operator !=(DeviceId left, DeviceId right) => !left.Equals(right);
}
