namespace Juxtens.GStreamer;

public abstract class GstError
{
    public string Message { get; }

    protected GstError(string message)
    {
        Message = message;
    }

    public sealed class BinaryNotFound : GstError
    {
        public string SearchedPath { get; }

        public BinaryNotFound(string searchedPath)
            : base($"GStreamer binary not found: {searchedPath}")
        {
            SearchedPath = searchedPath;
        }
    }

    public sealed class SpawnFailed : GstError
    {
        public Exception InnerException { get; }
        public string CommandSummary { get; }

        public SpawnFailed(string commandSummary, Exception innerException)
            : base($"Failed to spawn process: {commandSummary}")
        {
            CommandSummary = commandSummary;
            InnerException = innerException;
        }
    }

    public sealed class AlreadyRunning : GstError
    {
        public AlreadyRunning()
            : base("Stream handle is already running") { }
    }

    public sealed class ExitedUnexpectedly : GstError
    {
        public int? ExitCode { get; }
        public IReadOnlyList<string> LastStderrLines { get; }

        public ExitedUnexpectedly(int? exitCode, IReadOnlyList<string> lastStderrLines)
            : base($"Process exited unexpectedly (exit code: {exitCode?.ToString() ?? "unknown"})")
        {
            ExitCode = exitCode;
            LastStderrLines = lastStderrLines;
        }
    }

    public sealed class StopFailed : GstError
    {
        public Exception InnerException { get; }

        public StopFailed(Exception innerException)
            : base($"Failed to stop process: {innerException.Message}")
        {
            InnerException = innerException;
        }
    }

    public sealed class Io : GstError
    {
        public Exception InnerException { get; }

        public Io(Exception innerException)
            : base($"I/O error: {innerException.Message}")
        {
            InnerException = innerException;
        }
    }
}
