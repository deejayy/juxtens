using System.Text;

namespace Juxtens.Logger;

public sealed class FileLogger : ILogger, IDisposable
{
    private readonly string _filePath;
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private bool _disposed;

    public FileLogger(string filePath)
    {
        _filePath = filePath;
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            _writer = new StreamWriter(_filePath, append: true, Encoding.UTF8) { AutoFlush = true };
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to create log file at '{filePath}': {ex.Message}", ex);
        }
    }

    public void Info(string message) => Log("INFO", message);

    public void Warning(string message) => Log("WARN", message);

    public void Error(string message) => Log("ERROR", message);

    public void Error(string message, Exception ex) => Log("ERROR", $"{message}\n{ex}");

    private void Log(string level, string message)
    {
        lock (_lock)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FileLogger), "Cannot log after logger has been disposed");
            
            _writer?.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}");
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            
            _writer?.Dispose();
            _writer = null;
            _disposed = true;
        }
    }
}
