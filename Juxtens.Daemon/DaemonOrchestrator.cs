using Juxtens.DeviceManager;
using Juxtens.GStreamer;
using Juxtens.Logger;
using Juxtens.VDDControl;

namespace Juxtens.Daemon;

public sealed class StreamInfo
{
    public ushort Port { get; }
    public uint VdIndex { get; set; }
    public uint MonitorIndex { get; set; }
    public string ClientHost { get; }
    public StreamHandle Sender { get; set; }
    public StreamState State { get; set; }

    public StreamInfo(ushort port, uint vdIndex, uint monitorIndex, string clientHost, StreamHandle sender)
    {
        Port = port;
        VdIndex = vdIndex;
        MonitorIndex = monitorIndex;
        ClientHost = clientHost;
        Sender = sender;
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

public sealed class DaemonOrchestrator : IDisposable
{
    private const ushort MinPort = 5000;
    private const ushort MaxPort = 5019;
    private const int MaxPortRetries = 10;
    private static readonly TimeSpan VddStabilizationTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan VddPollInterval = TimeSpan.FromMilliseconds(500);

    private readonly IVDDController _vddController;
    private readonly IGStreamerManager _gstManager;
    private readonly ILogger _logger;
    private readonly OperationQueue _queue = new();
    private readonly List<StreamInfo> _streams = new();
    private readonly object _streamLock = new();
    private readonly Random _random = new();
    private readonly uint _physicalMonitorCount;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<StreamInfo>? StreamAdded;
    public event EventHandler<StreamInfo>? StreamRemoved;

    public int QueuedOperations => _queue.QueuedCount;

    public IReadOnlyList<StreamInfo> Streams
    {
        get
        {
            lock (_streamLock)
                return _streams.ToList();
        }
    }

    public DaemonOrchestrator(IVDDController vddController, IGStreamerManager gstManager, ILogger logger)
    {
        _vddController = vddController;
        _gstManager = gstManager;
        _logger = logger;
        
        var effectiveVddCountResult = _vddController.GetEffectiveCount();
        var effectiveVddCount = effectiveVddCountResult.IsSuccess ? effectiveVddCountResult.Value : 0u;
        var totalScreens = System.Windows.Forms.Screen.AllScreens.Length;
        _physicalMonitorCount = (uint)Math.Max(1, totalScreens - (int)effectiveVddCount);
    }

    public Task<(bool Success, string? Error, StreamInfo? Stream)> AddStreamAsync(string clientHost)
    {
        return _queue.EnqueueAsync<(bool Success, string? Error, StreamInfo? Stream)>(async () =>
        {
            var opId = Guid.NewGuid().ToString()[..8];
            UpdateStatus($"[{opId}] Adding stream: incrementing VDD count...");
            
            var currentCountResult = _vddController.GetEffectiveCount();
            if (!currentCountResult.IsSuccess)
            {
                var error = $"Failed to get effective VDD count: {currentCountResult.Error.Message}";
                UpdateStatus($"[{opId}] {error}");
                return (false, error, null);
            }

            var currentCount = currentCountResult.Value;
            var newCount = currentCount + 1;
            UpdateStatus($"[{opId}] Current effective count: {currentCount}, new count: {newCount}");
            
            var setResult = _vddController.SetVirtualDisplayCount(newCount);
            if (!setResult.IsSuccess)
            {
                var error = $"Failed to set VDD count: {setResult.Error.Message}";
                UpdateStatus($"[{opId}] {error}");
                return (false, error, null);
            }

            UpdateStatus($"[{opId}] VDD count set to {newCount}, waiting for stabilization...");
            if (!await WaitForVddStabilizationAsync(newCount))
            {
                var error = "VDD stabilization timeout";
                UpdateStatus($"[{opId}] {error}");
                return (false, error, null);
            }

            var vdIndex = newCount - 1;
            var monitorIndex = (uint)(_physicalMonitorCount + vdIndex);

            var port = AllocatePort();
            if (port == null)
            {
                var error = "Failed to allocate port - all ports in use";
                UpdateStatus($"[{opId}] {error}");
                await RemoveVddAsync(vdIndex);
                return (false, error, null);
            }

            UpdateStatus($"[{opId}] Starting sender on monitor {monitorIndex}, port {port.Value}...");
            var senderConfig = new SenderConfig(clientHost, port.Value, monitorIndex);
            var senderResult = _gstManager.StartSender(senderConfig);
            if (!senderResult.IsSuccess)
            {
                var error = $"Failed to start sender: {senderResult.Error.Message}";
                UpdateStatus($"[{opId}] {error}");
                await RemoveVddAsync(vdIndex);
                return (false, error, null);
            }

            var sender = senderResult.Value;
            sender.Exited += OnSenderExited;
            sender.StderrDataReceived += (_, data) => _logger.Info($"[Sender {port}] {data}");

            var stream = new StreamInfo(port.Value, vdIndex, monitorIndex, clientHost, sender);
            lock (_streamLock)
                _streams.Add(stream);

            UpdateStatus($"[{opId}] Stream added successfully on port {port.Value}");
            StreamAdded?.Invoke(this, stream);
            
            return (true, null, stream);
        });
    }

    public Task<(bool Success, string? Error)> RemoveStreamAsync(ushort port)
    {
        return _queue.EnqueueAsync<(bool Success, string? Error)>(async () =>
        {
            StreamInfo? stream;
            lock (_streamLock)
            {
                stream = _streams.FirstOrDefault(s => s.Port == port);
                if (stream == null)
                {
                    var error = $"Stream on port {port} not found";
                    UpdateStatus(error);
                    return (false, error);
                }
                stream.State = StreamState.Stopping;
            }

            UpdateStatus($"Removing stream on port {port}...");

            stream.Sender.Exited -= OnSenderExited;
            stream.Sender.Dispose();

            var removedVdIndex = stream.VdIndex;

            lock (_streamLock)
                _streams.Remove(stream);

            stream.State = StreamState.Stopped;
            StreamRemoved?.Invoke(this, stream);

            await RemoveVddAsync(removedVdIndex);
            UpdateStatus($"Stream on port {port} removed successfully");
            
            return (true, null);
        });
    }

    public Task CleanupAllAsync()
    {
        return _queue.EnqueueAsync(() =>
        {
            UpdateStatus("Cleaning up all streams and VDDs...");
            
            lock (_streamLock)
            {
                foreach (var stream in _streams.ToList())
                {
                    try
                    {
                        stream.Sender.Exited -= OnSenderExited;
                        stream.Sender.Dispose();
                    }
                    catch { }
                }
                _streams.Clear();
            }

            var result = _vddController.SetVirtualDisplayCount(0);
            if (result.IsSuccess)
                UpdateStatus("All streams and VDDs cleaned up");
            else
                UpdateStatus($"Failed to cleanup VDDs: {result.Error.Message}");
            
            return Task.CompletedTask;
        });
    }

    private async Task RemoveVddAsync(uint removedVdIndex)
    {
        var currentCountResult = _vddController.GetEffectiveCount();
        if (!currentCountResult.IsSuccess)
        {
            UpdateStatus($"Failed to get effective VDD count: {currentCountResult.Error.Message}");
            return;
        }

        if (currentCountResult.Value == 0)
            return;

        var newCount = currentCountResult.Value - 1;
        var setResult = _vddController.SetVirtualDisplayCount(newCount);
        if (!setResult.IsSuccess)
        {
            UpdateStatus($"Failed to decrement VDD count: {setResult.Error.Message}");
            return;
        }

        UpdateStatus($"VDD count decremented to {newCount}, reassigning VD indices...");
        
        lock (_streamLock)
        {
            foreach (var stream in _streams.Where(s => s.VdIndex > removedVdIndex))
            {
                var oldVdIndex = stream.VdIndex;
                stream.VdIndex--;
                stream.MonitorIndex = _physicalMonitorCount + stream.VdIndex;
                UpdateStatus($"Stream port {stream.Port}: VD {oldVdIndex}→{stream.VdIndex}, monitor {oldVdIndex + _physicalMonitorCount}→{stream.MonitorIndex}");
            }
        }

        await WaitForVddStabilizationAsync(newCount);
    }

    private void OnSenderExited(object? sender, EventArgs e)
    {
        var handle = sender as StreamHandle;
        if (handle == null)
            return;

        ushort? port = null;
        lock (_streamLock)
        {
            var stream = _streams.FirstOrDefault(s => ReferenceEquals(s.Sender, handle));
            if (stream == null)
                return;

            if (stream.State == StreamState.Stopping || stream.State == StreamState.Stopped)
                return;

            port = stream.Port;
            stream.State = StreamState.SenderRestarting;
        }

        if (port.HasValue)
        {
            UpdateStatus($"Sender on port {port.Value} exited, scheduling restart...");
            Task.Run(() => RestartSenderAsync(port.Value));
        }
    }

    private Task RestartSenderAsync(ushort port)
    {
        return _queue.EnqueueAsync(async () =>
        {
            StreamInfo? stream;
            lock (_streamLock)
            {
                stream = _streams.FirstOrDefault(s => s.Port == port);
                if (stream == null || stream.State != StreamState.SenderRestarting)
                    return;
            }

            UpdateStatus($"Restarting sender: port {port}, VD index {stream.VdIndex}...");

            var currentVddCount = _vddController.GetEffectiveCount();
            if (!currentVddCount.IsSuccess)
            {
                UpdateStatus($"Failed to get VDD count for restart: {currentVddCount.Error.Message}");
                lock (_streamLock)
                    stream.State = StreamState.Running;
                return;
            }

            await WaitForVddStabilizationAsync(currentVddCount.Value);

            var monitorIndex = _physicalMonitorCount + stream.VdIndex;
            lock (_streamLock)
                stream.MonitorIndex = monitorIndex;

            var senderConfig = new SenderConfig(stream.ClientHost, port, monitorIndex);
            var senderResult = _gstManager.StartSender(senderConfig);
            if (!senderResult.IsSuccess)
            {
                UpdateStatus($"Failed to restart sender: {senderResult.Error.Message}");
                lock (_streamLock)
                    stream.State = StreamState.Running;
                return;
            }

            var newSender = senderResult.Value;
            newSender.Exited += OnSenderExited;
            newSender.StderrDataReceived += (_, data) => _logger.Info($"[Sender {port}] {data}");

            lock (_streamLock)
            {
                stream.Sender.Exited -= OnSenderExited;
                stream.Sender.Dispose();
                stream.Sender = newSender;
                stream.State = StreamState.Running;
            }

            UpdateStatus($"Sender restarted successfully on monitor {monitorIndex}, port {port}");
        });
    }

    private async Task<bool> WaitForVddStabilizationAsync(uint expectedCount)
    {
        var startTime = DateTime.UtcNow;
        while (DateTime.UtcNow - startTime < VddStabilizationTimeout)
        {
            await Task.Delay(VddPollInterval);

            var result = _vddController.GetEffectiveCount();
            if (result.IsSuccess && result.Value == expectedCount)
                return true;
        }
        return false;
    }

    private ushort? AllocatePort()
    {
        var usedPorts = new HashSet<ushort>();
        lock (_streamLock)
        {
            foreach (var stream in _streams)
                usedPorts.Add(stream.Port);
        }

        for (int i = 0; i < MaxPortRetries; i++)
        {
            var port = (ushort)_random.Next(MinPort, MaxPort + 1);
            if (!usedPorts.Contains(port))
                return port;
        }

        return null;
    }

    private void UpdateStatus(string message)
    {
        StatusChanged?.Invoke(this, message);
        _logger.Info(message);
    }

    public void Dispose()
    {
        _logger.Info("DaemonOrchestrator disposing...");
        
        lock (_streamLock)
        {
            foreach (var stream in _streams.ToList())
            {
                try
                {
                    stream.Sender.Exited -= OnSenderExited;
                    stream.Sender.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error disposing stream on port {stream.Port}", ex);
                }
            }
            _streams.Clear();
        }

        try
        {
            var result = _vddController.SetVirtualDisplayCount(0);
            if (!result.IsSuccess)
            {
                _logger.Error($"Failed to reset VDD count: {result.Error.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Error resetting VDD count", ex);
        }
        
        _logger.Info("DaemonOrchestrator disposed");
    }
}
