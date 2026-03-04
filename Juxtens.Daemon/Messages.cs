using System.Text.Json.Serialization;

namespace Juxtens.Daemon;

public abstract record Message
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }
}

public record ClientMessage : Message;
public record DaemonMessage : Message;

public record AddStreamCommand : ClientMessage
{
    public AddStreamCommand() => Type = "AddStream";
}

public record RemoveStreamCommand : ClientMessage
{
    public RemoveStreamCommand() => Type = "RemoveStream";
    
    [JsonPropertyName("port")]
    public required ushort Port { get; init; }
}

public record PingCommand : ClientMessage
{
    public PingCommand() => Type = "Ping";
}

public record StreamStartedEvent : DaemonMessage
{
    public StreamStartedEvent() => Type = "StreamStarted";
    
    [JsonPropertyName("port")]
    public required ushort Port { get; init; }
    
    [JsonPropertyName("vdIndex")]
    public required uint VdIndex { get; init; }
    
    [JsonPropertyName("monitorIndex")]
    public required uint MonitorIndex { get; init; }
}

public record StreamStoppedEvent : DaemonMessage
{
    public StreamStoppedEvent() => Type = "StreamStopped";
    
    [JsonPropertyName("port")]
    public required ushort Port { get; init; }
}

public record ErrorEvent : DaemonMessage
{
    public ErrorEvent() => Type = "Error";
    
    [JsonPropertyName("message")]
    public required string ErrorMessage { get; init; }
}

public record DaemonExitEvent : DaemonMessage
{
    public DaemonExitEvent() => Type = "DaemonExit";
}

public record PongResponse : DaemonMessage
{
    public PongResponse() => Type = "Pong";
}
