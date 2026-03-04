using System.Text.Json;
using Fleck;
using Juxtens.Logger;

namespace Juxtens.Daemon;

public sealed class WebSocketServer : IDisposable
{
    private readonly DaemonOrchestrator _orchestrator;
    private readonly ILogger _logger;
    private readonly Fleck.WebSocketServer _server;
    private IWebSocketConnection? _client;
    private readonly object _clientLock = new();
    private System.Threading.Timer? _heartbeatTimer;
    private DateTime _lastPongReceived;
    private readonly TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(2);
    private readonly TimeSpan _heartbeatTimeout = TimeSpan.FromSeconds(5);
    private readonly List<IWebSocketConnection> _allConnections = new();
    private bool _disposed;

    public event Action<string>? MessageLogged;
    public event Action? ClientConnected;
    public event Action? ClientDisconnected;

    public WebSocketServer(DaemonOrchestrator orchestrator, ILogger logger, string bindAddress = "0.0.0.0:5021")
    {
        _orchestrator = orchestrator;
        _logger = logger;
        _server = new Fleck.WebSocketServer($"ws://{bindAddress}");
        
        FleckLog.LogAction = (level, message, ex) =>
        {
            var logMsg = ex != null ? $"[Fleck {level}] {message}: {ex}" : $"[Fleck {level}] {message}";
            _logger.Info(logMsg);
        };
    }

    public void Start()
    {
        _server.Start(socket =>
        {
            lock (_allConnections)
            {
                _allConnections.Add(socket);
            }
            
            socket.OnOpen = () => OnClientConnected(socket);
            socket.OnClose = () => OnClientDisconnected(socket);
            socket.OnMessage = message => OnMessageReceived(socket, message);
        });

        _logger.Info($"WebSocket server listening on {_server.Location}");
        LogMessage($"WebSocket server listening on {_server.Location}");
    }

    private void OnClientConnected(IWebSocketConnection socket)
    {
        lock (_clientLock)
        {
            if (_client != null)
            {
                _logger.Info($"Rejecting connection from {socket.ConnectionInfo.ClientIpAddress} (already connected)");
                socket.Close();
                return;
            }

            _client = socket;
            _lastPongReceived = DateTime.UtcNow;
        }

        _logger.Info($"Client connected: {socket.ConnectionInfo.ClientIpAddress}");
        LogMessage($"Client connected: {socket.ConnectionInfo.ClientIpAddress}");
        ClientConnected?.Invoke();

        _heartbeatTimer = new System.Threading.Timer(CheckHeartbeat, null, _heartbeatInterval, _heartbeatInterval);
    }

    private void OnClientDisconnected(IWebSocketConnection socket)
    {
        lock (_clientLock)
        {
            if (_client != socket) return;
            _client = null;
        }

        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;

        _logger.Info($"Client disconnected: {socket.ConnectionInfo.ClientIpAddress}");
        LogMessage($"Client disconnected: {socket.ConnectionInfo.ClientIpAddress}");
        ClientDisconnected?.Invoke();

        Task.Run(async () =>
        {
            try
            {
                await _orchestrator.CleanupAllAsync();
                _logger.Info("Cleanup completed after client disconnect");
            }
            catch (Exception ex)
            {
                _logger.Error($"Error during cleanup: {ex}");
            }
        });
    }

    private void OnMessageReceived(IWebSocketConnection socket, string message)
    {
        lock (_clientLock)
        {
            if (_client != socket) return;
        }

        _logger.Info($"Received: {message}");
        LogMessage($"RX: {message}");

        Task.Run(async () =>
        {
            try
            {
                await HandleMessageAsync(message);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error handling message: {ex}");
                await SendErrorAsync($"Error: {ex.Message}");
            }
        });
    }

    private async Task HandleMessageAsync(string message)
    {
        var json = JsonDocument.Parse(message);
        var type = json.RootElement.GetProperty("type").GetString();

        switch (type)
        {
            case "AddStream":
                var addResult = await _orchestrator.AddStreamAsync();
                if (addResult.Success && addResult.Stream != null)
                {
                    await SendStreamStartedAsync(addResult.Stream.Port, addResult.Stream.VdIndex, addResult.Stream.MonitorIndex);
                }
                else
                {
                    await SendErrorAsync(addResult.Error!);
                }
                break;

            case "RemoveStream":
                var port = (ushort)json.RootElement.GetProperty("port").GetInt32();
                var removeResult = await _orchestrator.RemoveStreamAsync(port);
                if (removeResult.Success)
                {
                    await SendStreamStoppedAsync(port);
                }
                else
                {
                    await SendErrorAsync(removeResult.Error!);
                }
                break;

            case "Ping":
                _lastPongReceived = DateTime.UtcNow;
                await SendPongAsync();
                break;

            default:
                _logger.Info($"Unknown message type: {type}");
                break;
        }
    }

    private void CheckHeartbeat(object? state)
    {
        var elapsed = DateTime.UtcNow - _lastPongReceived;
        if (elapsed > _heartbeatTimeout)
        {
            _logger.Info($"Heartbeat timeout ({elapsed.TotalSeconds:F1}s), disconnecting client");
            
            IWebSocketConnection? client;
            lock (_clientLock)
            {
                client = _client;
            }
            
            client?.Close();
        }
    }

    private async Task SendStreamStartedAsync(ushort port, uint vdIndex, uint monitorIndex)
    {
        var msg = new StreamStartedEvent
        {
            Type = "StreamStarted",
            Port = port,
            VdIndex = vdIndex,
            MonitorIndex = monitorIndex
        };
        await SendAsync(msg);
    }

    private async Task SendStreamStoppedAsync(ushort port)
    {
        var msg = new StreamStoppedEvent
        {
            Type = "StreamStopped",
            Port = port
        };
        await SendAsync(msg);
    }

    private async Task SendErrorAsync(string error)
    {
        var msg = new ErrorEvent
        {
            Type = "Error",
            ErrorMessage = error
        };
        await SendAsync(msg);
    }

    private async Task SendPongAsync()
    {
        var msg = new PongResponse { Type = "Pong" };
        await SendAsync(msg);
    }

    private async Task SendAsync(object message)
    {
        var json = JsonSerializer.Serialize(message);
        
        IWebSocketConnection? client;
        lock (_clientLock)
        {
            client = _client;
        }

        if (client?.IsAvailable == true)
        {
            await client.Send(json);
            _logger.Info($"Sent: {json}");
            LogMessage($"TX: {json}");
        }
    }

    private void LogMessage(string message)
    {
        MessageLogged?.Invoke(message);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _heartbeatTimer?.Dispose();
        
        List<IWebSocketConnection> connections;
        lock (_allConnections)
        {
            connections = new List<IWebSocketConnection>(_allConnections);
            _allConnections.Clear();
        }
        
        foreach (var conn in connections)
        {
            try
            {
                conn.Close();
            }
            catch { }
        }
        
        _server?.Dispose();
    }
}
