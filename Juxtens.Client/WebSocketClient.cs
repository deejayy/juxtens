using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Juxtens.Logger;

namespace Juxtens.Client;

public sealed class WebSocketClient : IDisposable
{
    private readonly ILogger _logger;
    private ClientWebSocket? _ws;
    private readonly TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(2);
    private readonly TimeSpan _heartbeatTimeout = TimeSpan.FromSeconds(5);
    private System.Threading.Timer? _heartbeatTimer;
    private DateTime _lastPongReceived;
    private int _missedHeartbeats;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private string _remoteAddress = string.Empty;

    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<ushort, uint, uint>? StreamStarted;
    public event Action<ushort>? StreamStopped;
    public event Action<string>? ErrorReceived;
    public event Action<string>? MessageLogged;
    public event Action<int>? HeartbeatStatusChanged;

    public bool IsConnected => _ws?.State == WebSocketState.Open;
    public string RemoteAddress => _remoteAddress;

    public WebSocketClient(ILogger logger)
    {
        _logger = logger;
    }

    public async Task ConnectAsync(string address)
    {
        if (_ws != null)
        {
            throw new InvalidOperationException("Already connected");
        }

        _ws = new ClientWebSocket();
        _cts = new CancellationTokenSource();
        _lastPongReceived = DateTime.UtcNow;
        _missedHeartbeats = 0;
        _remoteAddress = address;

        var uri = new Uri($"ws://{address}");
        _logger.Info($"Connecting to {uri}...");
        LogMessage($"Connecting to {uri}...");

        try
        {
            await _ws.ConnectAsync(uri, _cts.Token);

            _logger.Info("Connected");
            LogMessage("Connected");
            Connected?.Invoke();

            _receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
            _heartbeatTimer = new System.Threading.Timer(SendPing, null, _heartbeatInterval, _heartbeatInterval);
        }
        catch
        {
            _ws.Dispose();
            _ws = null;
            _cts.Dispose();
            _cts = null;
            _remoteAddress = string.Empty;
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        var ws = _ws;
        if (ws == null) return;

        _logger.Info("Disconnecting...");
        LogMessage("Disconnecting...");

        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;

        _cts?.Cancel();

        if (ws.State == WebSocketState.Open)
        {
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnect", CancellationToken.None);
        }

        if (_receiveTask != null)
        {
            await _receiveTask;
        }

        ws.Dispose();
        _ws = null;
        _cts?.Dispose();
        _cts = null;
        _remoteAddress = string.Empty;

        _logger.Info("Disconnected");
        LogMessage("Disconnected");
        Disconnected?.Invoke();
    }

    public async Task SendAddStreamAsync()
    {
        var msg = new { type = "AddStream" };
        await SendAsync(msg);
    }

    public async Task SendRemoveStreamAsync(ushort port)
    {
        var msg = new { type = "RemoveStream", port };
        await SendAsync(msg);
    }

    private void SendPing(object? state)
    {
        if (_ws?.State != WebSocketState.Open) return;

        var elapsed = DateTime.UtcNow - _lastPongReceived;
        if (elapsed > _heartbeatTimeout)
        {
            _logger.Warning($"Heartbeat timeout ({elapsed.TotalSeconds:F1}s), disconnecting");
            LogMessage($"Heartbeat timeout, disconnecting...");
            Task.Run(async () => await DisconnectAsync());
            return;
        }

        _missedHeartbeats = (int)(elapsed.TotalSeconds / _heartbeatInterval.TotalSeconds);
        HeartbeatStatusChanged?.Invoke(_missedHeartbeats);

        var msg = new { type = "Ping" };
        Task.Run(async () => await SendPingMessageAsync(msg));
    }

    private async Task SendPingMessageAsync(object message)
    {
        if (_ws?.State != WebSocketState.Open) return;

        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private async Task SendAsync(object message)
    {
        if (_ws?.State != WebSocketState.Open) return;

        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);

        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        _logger.Info($"Sent: {json}");
        LogMessage($"TX: {json}");
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];

        try
        {
            while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
            {
                var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.Info("Server closed connection");
                    LogMessage("Server closed connection");
                    break;
                }

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                _logger.Info($"Received: {json}");
                LogMessage($"RX: {json}");

                await HandleMessageAsync(json);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.Error("Receive error", ex);
            LogMessage($"Receive error: {ex.Message}");
        }
        finally
        {
            if (_ws != null)
            {
                _ = Task.Run(async () => await DisconnectAsync());
            }
        }
    }

    private async Task HandleMessageAsync(string json)
    {
        await Task.Yield();

        var doc = JsonDocument.Parse(json);
        var type = doc.RootElement.GetProperty("type").GetString();

        switch (type)
        {
            case "StreamStarted":
                var port = (ushort)doc.RootElement.GetProperty("port").GetInt32();
                var vdIndex = (uint)doc.RootElement.GetProperty("vdIndex").GetInt32();
                var monitorIndex = (uint)doc.RootElement.GetProperty("monitorIndex").GetInt32();
                StreamStarted?.Invoke(port, vdIndex, monitorIndex);
                break;

            case "StreamStopped":
                var stoppedPort = (ushort)doc.RootElement.GetProperty("port").GetInt32();
                StreamStopped?.Invoke(stoppedPort);
                break;

            case "Error":
                var errorMsg = doc.RootElement.GetProperty("message").GetString() ?? "Unknown error";
                ErrorReceived?.Invoke(errorMsg);
                break;

            case "Pong":
                _lastPongReceived = DateTime.UtcNow;
                _missedHeartbeats = 0;
                HeartbeatStatusChanged?.Invoke(_missedHeartbeats);
                break;

            case "DaemonExit":
                _logger.Info("Daemon exiting");
                LogMessage("Daemon exiting");
                await DisconnectAsync();
                break;

            default:
                _logger.Warning($"Unknown message type: {type}");
                break;
        }
    }

    private void LogMessage(string message)
    {
        MessageLogged?.Invoke(message);
    }

    public void Dispose()
    {
        _heartbeatTimer?.Dispose();
        _cts?.Cancel();
        _cts?.Dispose();
        _ws?.Dispose();
    }
}
