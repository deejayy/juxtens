using Juxtens.DeviceManager;
using Juxtens.GStreamer;
using Juxtens.Logger;
using Juxtens.VDDControl;

namespace Juxtens.Server;

public sealed class StreamOrchestrator : IDisposable
{
    private const ushort MinPort = 5000;
    private const ushort MaxPort = 5019;
    private const int MaxPortRetries = 10;
    private static readonly TimeSpan VddStabilizationTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan VddPollInterval = TimeSpan.FromMilliseconds(500);

    private readonly IVDDController _vddController;
    private readonly IGStreamerManager _gstManager;
    private readonly IDeviceManager _deviceManager;
    private readonly ILogger _logger;
    private readonly OperationQueue _queue = new();
    private readonly List<StreamPair> _streams = new();
    private readonly object _streamLock = new();
    private readonly Random _random = new();
    private readonly uint _physicalMonitorCount;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<StreamPair>? StreamAdded;
    public event EventHandler<StreamPair>? StreamRemoved;

    public int QueuedOperations => _queue.QueuedCount;

    public IReadOnlyList<StreamPair> Streams
    {
        get
        {
            lock (_streamLock)
                return _streams.ToList();
        }
    }

    public StreamOrchestrator(IVDDController vddController, IGStreamerManager gstManager, IDeviceManager deviceManager, ILogger logger)
    {
        _vddController = vddController;
        _gstManager = gstManager;
        _deviceManager = deviceManager;
        _logger = logger;
        
        // Capture physical monitor count at initialization (before any VDD manipulation)
        var effectiveVddCountResult = _vddController.GetEffectiveCount();
        var effectiveVddCount = effectiveVddCountResult.IsSuccess ? effectiveVddCountResult.Value : 0u;
        var totalScreens = System.Windows.Forms.Screen.AllScreens.Length;
        _physicalMonitorCount = (uint)Math.Max(1, totalScreens - (int)effectiveVddCount);
    }

    public Task AddStreamAsync() =>
        _queue.EnqueueAsync(async () =>
        {
            var opId = Guid.NewGuid().ToString()[..8];
            UpdateStatus($"[{opId}] Adding stream: incrementing VDD count...");
            var currentCountResult = _vddController.GetEffectiveCount();
            if (!currentCountResult.IsSuccess)
            {
                UpdateStatus($"[{opId}] Failed to get effective VDD count: {currentCountResult.Error.Message}");
                return;
            }

            var currentCount = currentCountResult.Value;
            var newCount = currentCount + 1;
            UpdateStatus($"[{opId}] Current effective count: {currentCount}, new count: {newCount}");
            
            var setResult = _vddController.SetVirtualDisplayCount(newCount);
            if (!setResult.IsSuccess)
            {
                UpdateStatus($"[{opId}] Failed to set VDD count: {setResult.Error.Message}");
                return;
            }

            UpdateStatus($"[{opId}] VDD count set to {newCount}, waiting for stabilization...");
            if (!await WaitForVddStabilizationAsync(newCount))
            {
                UpdateStatus("VDD stabilization timeout - stream creation aborted");
                return;
            }

            var vdIndex = newCount - 1;
            var monitorIndex = (uint)(_physicalMonitorCount + vdIndex);

            var port = AllocatePort();
            if (port == null)
            {
                UpdateStatus("Failed to allocate port - all ports in use");
                await RemoveVddAsync(vdIndex);
                return;
            }

            UpdateStatus($"[{opId}] Starting sender on monitor {monitorIndex}, port {port.Value}...");
            var senderConfig = new SenderConfig("127.0.0.1", port.Value, monitorIndex);
            var senderResult = _gstManager.StartSender(senderConfig);
            if (!senderResult.IsSuccess)
            {
                UpdateStatus($"Failed to start sender: {senderResult.Error.Message}");
                await RemoveVddAsync(vdIndex);
                return;
            }

            var sender = senderResult.Value;
            sender.Exited += OnSenderExited;
            sender.StderrDataReceived += OnSenderOutput;

            UpdateStatus($"Starting receiver on port {port.Value}...");
            var receiverConfig = new ReceiverConfig(port.Value, false);
            var receiverResult = _gstManager.StartReceiver(receiverConfig);
            if (!receiverResult.IsSuccess)
            {
                UpdateStatus($"Failed to start receiver: {receiverResult.Error.Message}");
                sender.Exited -= OnSenderExited;
                sender.StderrDataReceived -= OnSenderOutput;
                sender.Dispose();
                await RemoveVddAsync(vdIndex);
                return;
            }

            var receiver = receiverResult.Value;
            receiver.Exited += OnReceiverExited;
            receiver.StderrDataReceived += OnReceiverOutput;

            var pair = new StreamPair(port.Value, vdIndex, monitorIndex, sender, receiver);
            lock (_streamLock)
                _streams.Add(pair);

            UpdateStatus($"Stream added successfully on port {port.Value}");
            StreamAdded?.Invoke(this, pair);
        });

    public Task RemoveStreamAsync(ushort port) =>
        _queue.EnqueueAsync(async () =>
        {
            StreamPair? pair;
            lock (_streamLock)
            {
                pair = _streams.FirstOrDefault(s => s.Port == port);
                if (pair == null)
                {
                    UpdateStatus($"Stream on port {port} not found");
                    return;
                }
                pair.State = StreamState.Stopping;
            }

            UpdateStatus($"Removing stream on port {port}...");

            pair.Receiver.Exited -= OnReceiverExited;
            pair.Receiver.StderrDataReceived -= OnReceiverOutput;
            pair.Receiver.Dispose();

            pair.Sender.Exited -= OnSenderExited;
            pair.Sender.StderrDataReceived -= OnSenderOutput;
            pair.Sender.Dispose();

            var removedVdIndex = pair.VdIndex;

            lock (_streamLock)
                _streams.Remove(pair);

            pair.State = StreamState.Stopped;
            StreamRemoved?.Invoke(this, pair);

            await RemoveVddAsync(removedVdIndex);
            UpdateStatus($"Stream on port {port} removed successfully");
        });

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

    private void OnReceiverExited(object? sender, EventArgs e)
    {
        var handle = sender as StreamHandle;
        if (handle == null)
            return;

        ushort? port = null;
        lock (_streamLock)
        {
            var pair = _streams.FirstOrDefault(s => ReferenceEquals(s.Receiver, handle));
            if (pair != null)
                port = pair.Port;
        }

        if (port.HasValue)
        {
            UpdateStatus($"Receiver on port {port.Value} exited unexpectedly");
            Task.Run(() => RemoveStreamAsync(port.Value));
        }
    }

    private void OnSenderExited(object? sender, EventArgs e)
    {
        var handle = sender as StreamHandle;
        if (handle == null)
            return;

        StreamPair? pair;
        lock (_streamLock)
        {
            pair = _streams.FirstOrDefault(s => ReferenceEquals(s.Sender, handle));
            if (pair == null || pair.State != StreamState.Running)
                return;

            pair.State = StreamState.SenderRestarting;
        }

        UpdateStatus($"Sender on port {pair.Port} exited, scheduling restart...");
        Task.Run(() => RestartSenderAsync(pair.Port));
    }

    private Task RestartSenderAsync(ushort port) =>
        _queue.EnqueueAsync(async () =>
        {
            StreamPair? pair;
            lock (_streamLock)
            {
                pair = _streams.FirstOrDefault(s => s.Port == port);
                if (pair == null || pair.State != StreamState.SenderRestarting)
                    return;
            }

            UpdateStatus($"Restarting sender: port {port}, VD index {pair.VdIndex}...");

            var currentVddCount = _vddController.GetEffectiveCount();
            if (!currentVddCount.IsSuccess)
            {
                UpdateStatus($"Failed to get VDD count for restart: {currentVddCount.Error.Message}");
                lock (_streamLock)
                    pair.State = StreamState.Running;
                return;
            }

            await WaitForVddStabilizationAsync(currentVddCount.Value);

            var monitorIndex = _physicalMonitorCount + pair.VdIndex;
            lock (_streamLock)
                pair.MonitorIndex = monitorIndex;

            var senderConfig = new SenderConfig("127.0.0.1", port, monitorIndex);
            var senderResult = _gstManager.StartSender(senderConfig);
            if (!senderResult.IsSuccess)
            {
                UpdateStatus($"Failed to restart sender: {senderResult.Error.Message}");
                lock (_streamLock)
                    pair.State = StreamState.Running;
                return;
            }

            var newSender = senderResult.Value;
            newSender.Exited += OnSenderExited;
            newSender.StderrDataReceived += OnSenderOutput;

            lock (_streamLock)
            {
                pair.Sender.Exited -= OnSenderExited;
                pair.Sender.StderrDataReceived -= OnSenderOutput;
                pair.Sender.Dispose();
                pair.Sender = newSender;
                pair.State = StreamState.Running;
            }

            UpdateStatus($"Sender restarted successfully on monitor {monitorIndex}, port {port}");
        });

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
        lock (_streamLock)
        {
            var usedPorts = _streams.Select(s => s.Port).ToHashSet();
            var availablePorts = Enumerable.Range(MinPort, MaxPort - MinPort + 1)
                .Select(p => (ushort)p)
                .Where(p => !usedPorts.Contains(p))
                .ToList();

            if (availablePorts.Count == 0)
                return null;

            for (var i = 0; i < MaxPortRetries; i++)
            {
                var port = availablePorts[_random.Next(availablePorts.Count)];
                if (!usedPorts.Contains(port))
                    return port;
            }

            return availablePorts.FirstOrDefault();
        }
    }

    private void OnSenderOutput(object? sender, string data) { }
    private void OnReceiverOutput(object? sender, string data) { }

    private void UpdateStatus(string message)
    {
        StatusChanged?.Invoke(this, message);
    }

    public void Dispose()
    {
        lock (_streamLock)
        {
            foreach (var pair in _streams.ToList())
            {
                try
                {
                    pair.Sender.Exited -= OnSenderExited;
                    pair.Sender.StderrDataReceived -= OnSenderOutput;
                    pair.Sender.Dispose();

                    pair.Receiver.Exited -= OnReceiverExited;
                    pair.Receiver.StderrDataReceived -= OnReceiverOutput;
                    pair.Receiver.Dispose();
                }
                catch { }
            }
            _streams.Clear();
        }
    }
}
