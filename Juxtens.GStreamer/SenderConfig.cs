namespace Juxtens.GStreamer;

public sealed class SenderConfig
{
    public string Host { get; }
    public ushort Port { get; }
    public uint MonitorIndex { get; }

    public SenderConfig(string host, ushort port, uint monitorIndex)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Host cannot be empty", nameof(host));

        Host = host;
        Port = port;
        MonitorIndex = monitorIndex;
    }
}
