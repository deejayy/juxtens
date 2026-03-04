namespace Juxtens.DeviceManager;

public abstract class DeviceError
{
    public string Message { get; }

    protected DeviceError(string message)
    {
        Message = message;
    }

    public sealed class NotFound : DeviceError
    {
        public NotFound(DeviceId id) : base($"Device not found: {id}") { }
    }

    public sealed class InvalidId : DeviceError
    {
        public InvalidId(DeviceId id) : base($"Invalid device ID: {id}") { }
    }

    public sealed class PermissionDenied : DeviceError
    {
        public PermissionDenied() : base("Access denied. Administrator privileges required.") { }
    }

    public sealed class Timeout : DeviceError
    {
        public Timeout(TimeSpan elapsed) : base($"Operation timed out after {elapsed.TotalSeconds:F1}s") { }
    }

    public sealed class WindowsApi : DeviceError
    {
        public uint ErrorCode { get; }

        public WindowsApi(uint errorCode, string context) 
            : base($"{context}: 0x{errorCode:X8}") 
        {
            ErrorCode = errorCode;
        }
    }

    public sealed class Unsupported : DeviceError
    {
        public Unsupported(string operation) : base($"Operation not supported: {operation}") { }
    }

    public sealed class Busy : DeviceError
    {
        public Busy(DeviceId id) : base($"Device is busy: {id}") { }
    }
}
