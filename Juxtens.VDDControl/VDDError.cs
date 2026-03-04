namespace Juxtens.VDDControl;

public abstract class VDDError
{
    public string Message { get; }

    protected VDDError(string message)
    {
        Message = message;
    }

    public sealed class ConfigNotFound : VDDError
    {
        public string FilePath { get; }
        
        public ConfigNotFound(string filePath) 
            : base($"VDD config file not found: {filePath}")
        {
            FilePath = filePath;
        }
    }

    public sealed class ConfigParseFailed : VDDError
    {
        public Exception InnerException { get; }
        
        public ConfigParseFailed(string message, Exception ex) 
            : base($"Failed to parse VDD config: {message}")
        {
            InnerException = ex;
        }
    }

    public sealed class ConfigWriteFailed : VDDError
    {
        public Exception InnerException { get; }
        
        public ConfigWriteFailed(string message, Exception ex) 
            : base($"Failed to write VDD config: {message}")
        {
            InnerException = ex;
        }
    }

    public sealed class DeviceNotFound : VDDError
    {
        public DeviceNotFound() 
            : base("VDD device not found on system") { }
    }

    public sealed class DeviceOperationFailed : VDDError
    {
        public string Operation { get; }
        
        public DeviceOperationFailed(string operation, string reason) 
            : base($"Device operation '{operation}' failed: {reason}")
        {
            Operation = operation;
        }
    }

    public sealed class InvalidCount : VDDError
    {
        public int RequestedCount { get; }
        
        public InvalidCount(int count) 
            : base($"Invalid virtual display count: {count}")
        {
            RequestedCount = count;
        }
    }
}
