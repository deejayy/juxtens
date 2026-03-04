namespace Juxtens.GStreamer;

public sealed class ReceiverConfig
{
    public ushort Port { get; }
    public bool Fullscreen { get; }

    public ReceiverConfig(ushort port, bool fullscreen = false)
    {
        Port = port;
        Fullscreen = fullscreen;
    }
}
