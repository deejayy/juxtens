using Juxtens.GStreamer;
using Juxtens.Logger;

namespace Juxtens.Client;

public sealed class StreamManager : IDisposable
{
    private readonly IGStreamerManager _gstManager;
    private readonly ILogger _logger;
    private readonly Dictionary<ushort, StreamHandle> _receivers = new();
    private readonly object _lock = new();

    public event Action<ushort>? ReceiverExited;
    public event Action? ReceiversChanged;

    public int ActiveScreenCount
    {
        get
        {
            lock (_lock)
            {
                return _receivers.Count;
            }
        }
    }

    public StreamManager(IGStreamerManager gstManager, ILogger logger)
    {
        _gstManager = gstManager;
        _logger = logger;
    }

    public void StartReceiver(ushort port, uint vdIndex, uint monitorIndex)
    {
        lock (_lock)
        {
            if (_receivers.ContainsKey(port))
            {
                _logger.Warning($"Receiver already running on port {port}");
                return;
            }

            _logger.Info($"Starting receiver for port {port} (VD:{vdIndex}, Monitor:{monitorIndex})");

            var config = new ReceiverConfig(port, fullscreen: false);
            var result = _gstManager.StartReceiver(config);

            if (!result.IsSuccess)
            {
                _logger.Error($"Failed to start receiver: {result.Error}");
                return;
            }

            var handle = result.Value;
            _receivers[port] = handle;

            handle.Exited += (sender, args) => OnReceiverExited(port);
        }

        ReceiversChanged?.Invoke();
    }

    public void StopReceiver(ushort port)
    {
        StreamHandle? handle;
        lock (_lock)
        {
            if (!_receivers.TryGetValue(port, out handle))
            {
                _logger.Warning($"No receiver running on port {port}");
                return;
            }

            _receivers.Remove(port);
        }

        _logger.Info($"Stopping receiver on port {port}");
        handle.Stop();

        ReceiversChanged?.Invoke();
    }

    public void StopAllReceivers()
    {
        StreamHandle[] handles;
        lock (_lock)
        {
            handles = _receivers.Values.ToArray();
            _receivers.Clear();
        }

        _logger.Info($"Stopping all {handles.Length} receivers");

        foreach (var handle in handles)
        {
            handle.Stop();
        }

        if (handles.Length > 0)
        {
            ReceiversChanged?.Invoke();
        }
    }

    private void OnReceiverExited(ushort port)
    {
        lock (_lock)
        {
            _receivers.Remove(port);
        }

        _logger.Info($"Receiver on port {port} exited");
        ReceiverExited?.Invoke(port);
        ReceiversChanged?.Invoke();
    }

    public void Dispose()
    {
        StopAllReceivers();
    }
}
