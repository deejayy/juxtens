using Juxtens.GStreamer;

namespace Juxtens.Server;

public sealed class StreamPair
{
    public ushort Port { get; }
    public uint VdIndex { get; set; }
    public uint MonitorIndex { get; set; }
    public StreamHandle Sender { get; set; }
    public StreamHandle Receiver { get; set; }
    public StreamState State { get; set; }

    public StreamPair(ushort port, uint vdIndex, uint monitorIndex, StreamHandle sender, StreamHandle receiver)
    {
        Port = port;
        VdIndex = vdIndex;
        MonitorIndex = monitorIndex;
        Sender = sender;
        Receiver = receiver;
        State = StreamState.Running;
    }
}

public enum StreamState
{
    Running,
    SenderRestarting,
    Stopping,
    Stopped
}
