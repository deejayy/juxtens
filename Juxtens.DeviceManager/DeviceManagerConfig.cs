namespace Juxtens.DeviceManager;

public sealed class DeviceManagerConfig
{
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    public static DeviceManagerConfig Default => new();
}
