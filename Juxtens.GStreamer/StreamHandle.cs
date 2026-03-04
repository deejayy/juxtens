using System.Diagnostics;

namespace Juxtens.GStreamer;

public sealed class StreamHandle : IDisposable
{
    private readonly Process _process;
    private readonly TimeSpan _shutdownTimeout;
    private readonly RingBuffer _stderrBuffer;
    private readonly object _lock = new();
    private bool _disposed;

    public event EventHandler? Exited;
    public event EventHandler<string>? StderrDataReceived;

    internal StreamHandle(Process process, TimeSpan shutdownTimeout, int logCapacity)
    {
        _process = process;
        _shutdownTimeout = shutdownTimeout;
        _stderrBuffer = new RingBuffer(logCapacity);

        _process.EnableRaisingEvents = true;
        _process.Exited += OnProcessExited;

        if (process.StartInfo.RedirectStandardOutput)
        {
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    _stderrBuffer.Add(e.Data);
                    StderrDataReceived?.Invoke(this, e.Data);
                }
            };
            process.BeginOutputReadLine();
        }

        if (process.StartInfo.RedirectStandardError)
        {
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    _stderrBuffer.Add(e.Data);
                    StderrDataReceived?.Invoke(this, e.Data);
                }
            };
            process.BeginErrorReadLine();
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                ThrowIfDisposed();
                return !_process.HasExited;
            }
        }
    }

    public int? ExitCode
    {
        get
        {
            lock (_lock)
            {
                ThrowIfDisposed();
                return _process.HasExited ? _process.ExitCode : null;
            }
        }
    }

    public IReadOnlyList<string> GetLastStderrLines()
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            return _stderrBuffer.GetAll();
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            ThrowIfDisposed();

            if (_process.HasExited)
                return;

            try
            {
                _process.Kill(false);

                if (_process.WaitForExit((int)_shutdownTimeout.TotalMilliseconds))
                {
                    _process.WaitForExit();
                    return;
                }

                _process.Kill(true);
                _process.WaitForExit();
            }
            catch (Exception ex) when (ex is InvalidOperationException)
            {
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _process.Exited -= OnProcessExited;
            Stop();
            _process.Dispose();
            _disposed = true;
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        Exited?.Invoke(this, EventArgs.Empty);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(StreamHandle));
    }

    private sealed class RingBuffer
    {
        private readonly string[] _buffer;
        private int _head;
        private int _count;

        public RingBuffer(int capacity)
        {
            _buffer = new string[capacity];
        }

        public void Add(string line)
        {
            lock (_buffer)
            {
                _buffer[_head] = line;
                _head = (_head + 1) % _buffer.Length;
                if (_count < _buffer.Length)
                    _count++;
            }
        }

        public IReadOnlyList<string> GetAll()
        {
            lock (_buffer)
            {
                if (_count == 0)
                    return Array.Empty<string>();

                var result = new string[_count];
                var start = (_head - _count + _buffer.Length) % _buffer.Length;

                for (var i = 0; i < _count; i++)
                    result[i] = _buffer[(start + i) % _buffer.Length];

                return result;
            }
        }
    }
}
