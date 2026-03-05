using System.Diagnostics;
using System.Text;
using Juxtens.DeviceManager;
using Juxtens.Logger;

namespace Juxtens.GStreamer;

public sealed class GStreamerManager : IGStreamerManager
{
    private readonly GstConfig _config;
    private readonly ILogger? _logger;

    public GStreamerManager(GstConfig? config = null, ILogger? logger = null)
    {
        _config = config ?? GstConfig.Default;
        _logger = logger;
    }

    public Result<StreamHandle, GstError> StartSender(SenderConfig config)
    {
        var binaryPath = Path.Combine(_config.BaseDir, "gst-launch-1.0.exe");
        if (!File.Exists(binaryPath))
            return Result<StreamHandle, GstError>.Failure(new GstError.BinaryNotFound(binaryPath));

        var args = BuildSenderArgs(config);
        return SpawnProcess(binaryPath, args);
    }

    public Result<StreamHandle, GstError> StartReceiver(ReceiverConfig config)
    {
        var binaryPath = Path.Combine(_config.BaseDir, "gst-launch-1.0.exe");
        if (!File.Exists(binaryPath))
            return Result<StreamHandle, GstError>.Failure(new GstError.BinaryNotFound(binaryPath));

        var args = BuildReceiverArgs(config);
        return SpawnProcess(binaryPath, args);
    }

    private Result<StreamHandle, GstError> SpawnProcess(string binaryPath, string args)
    {
        var fullCommand = $"\"{binaryPath}\" {args}";
        _logger?.Info($"[GStreamer] Executing: {fullCommand}");
        
        var redirectOutput = _config.StderrMode == StderrMode.Pipe;
        var psi = new ProcessStartInfo
        {
            FileName = binaryPath,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = redirectOutput
        };

        try
        {
            var process = Process.Start(psi);
            if (process == null)
                return Result<StreamHandle, GstError>.Failure(
                    new GstError.SpawnFailed($"gst-launch-1.0.exe {args}", 
                        new InvalidOperationException("Process.Start returned null")));

            var handle = new StreamHandle(process, _config.ShutdownTimeout, _config.LogRingCapacity);
            return Result<StreamHandle, GstError>.Success(handle);
        }
        catch (Exception ex)
        {
            return Result<StreamHandle, GstError>.Failure(
                new GstError.SpawnFailed($"gst-launch-1.0.exe {args}", ex));
        }
    }

    private static string BuildSenderArgs(SenderConfig config)
    {
        var sb = new StringBuilder();
        sb.Append("-v ");
        sb.Append("d3d11screencapturesrc ");
        sb.Append($"monitor-index={config.MonitorIndex} ");
        sb.Append("capture-api=dxgi do-timestamp=true show-cursor=true ");
        sb.Append("! video/x-raw(memory:D3D11Memory),format=BGRA,width=1920,height=1080,framerate=144/1 ");
        sb.Append("! queue max-size-buffers=2 leaky=downstream ");
        sb.Append("! cudaupload ");
        sb.Append("! nvh265enc preset=p1 tune=ultra-low-latency zerolatency=true rc-mode=constqp qp-const-i=20 qp-const-p=20 gop-size=30 strict-gop=true repeat-sequence-header=true ");
        sb.Append("! h265parse ");
        sb.Append("! rtph265pay pt=96 mtu=1400 aggregate-mode=zero-latency config-interval=-1 ");
        sb.Append($"! udpsink host={config.Host} port={config.Port} sync=false async=false buffer-size=4194304");
        return sb.ToString();
    }

    private static string BuildReceiverArgs(ReceiverConfig config)
    {
        var sb = new StringBuilder();
        sb.Append("-v ");
        sb.Append($"udpsrc port={config.Port} buffer-size=4194304 ");
        sb.Append("caps=\"application/x-rtp,media=video,encoding-name=H265,payload=96,clock-rate=90000\" ");
        sb.Append("! rtpjitterbuffer latency=10 drop-on-latency=true do-lost=true ");
        sb.Append("! rtph265depay ");
        sb.Append("! h265parse ");
        sb.Append("! nvh265dec max-display-delay=0 ");
        sb.Append("! videoconvert ");
        sb.Append($"! d3d11videosink sync=false fullscreen-toggle-mode=alt-enter fullscreen={config.Fullscreen.ToString().ToLower()} force-aspect-ratio=true");
        return sb.ToString();
    }
}
