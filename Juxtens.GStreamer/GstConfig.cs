namespace Juxtens.GStreamer;

public sealed class GstConfig
{
    public string BaseDir { get; }
    public StderrMode StderrMode { get; }
    public TimeSpan ShutdownTimeout { get; }
    public int LogRingCapacity { get; }

    public GstConfig(
        string? baseDir = null,
        StderrMode stderrMode = StderrMode.Pipe,
        TimeSpan? shutdownTimeout = null,
        int logRingCapacity = 200)
    {
        BaseDir = baseDir ?? Path.Combine(Directory.GetCurrentDirectory(), "gstreamer", "bin");
        StderrMode = stderrMode;
        ShutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(5);
        LogRingCapacity = logRingCapacity;
    }

    public static GstConfig Default => new();
}

public enum StderrMode
{
    Pipe,
    Inherit
}
