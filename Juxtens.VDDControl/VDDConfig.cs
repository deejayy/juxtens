using System.Xml.Linq;
using Juxtens.Logger;
using Juxtens.DeviceManager;

namespace Juxtens.VDDControl;

public sealed class VDDConfig
{
    private const string DefaultConfigPath = @"C:\VirtualDisplayDriver\vdd_settings.xml";
    private const int MaxBackoffMs = 1000;
    
    private readonly string _configPath;
    private readonly ILogger _logger;
    private readonly Mutex _mutex;

    public VDDConfig(ILogger logger, string? configPath = null)
    {
        _logger = logger;
        _configPath = configPath ?? DefaultConfigPath;
        _mutex = new Mutex(false, "Global\\Juxtens_VDD_Config_Mutex");
    }

    public Result<uint, VDDError> GetMonitorCount()
    {
        if (!File.Exists(_configPath))
        {
            _logger.Error($"VDD config file not found: {_configPath}");
            return Result<uint, VDDError>.Failure(new VDDError.ConfigNotFound(_configPath));
        }

        try
        {
            var doc = XDocument.Load(_configPath);
            var countElement = doc.Root?.Element("monitors")?.Element("count");
            
            if (countElement == null)
            {
                return Result<uint, VDDError>.Failure(
                    new VDDError.ConfigParseFailed("Missing <monitors><count> element", new Exception("Element not found")));
            }

            if (!uint.TryParse(countElement.Value, out var count))
            {
                return Result<uint, VDDError>.Failure(
                    new VDDError.ConfigParseFailed($"Invalid count value: {countElement.Value}", new FormatException()));
            }

            return Result<uint, VDDError>.Success(count);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to read VDD config: {ex.Message}", ex);
            return Result<uint, VDDError>.Failure(new VDDError.ConfigParseFailed(ex.Message, ex));
        }
    }

    public Result<DeviceManager.Unit, VDDError> SetMonitorCount(uint count)
    {
        _logger.Info($"Setting VDD monitor count to {count}");

        if (!File.Exists(_configPath))
        {
            _logger.Error($"VDD config file not found: {_configPath}");
            return Result<DeviceManager.Unit, VDDError>.Failure(new VDDError.ConfigNotFound(_configPath));
        }

        bool lockAcquired = false;
        try
        {
            lockAcquired = _mutex.WaitOne(5000);
            if (!lockAcquired)
            {
                return Result<DeviceManager.Unit, VDDError>.Failure(
                    new VDDError.ConfigWriteFailed("Timeout acquiring config lock", new TimeoutException()));
            }

            var directory = Path.GetDirectoryName(_configPath)!;
            var backupPath = _configPath + ".bak";
            var tempPath = _configPath + ".tmp";

            if (!File.Exists(backupPath))
            {
                try
                {
                    File.Copy(_configPath, backupPath);
                    _logger.Info($"Created backup: {backupPath}");
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to create backup (non-fatal): {ex.Message}");
                }
            }

            var doc = XDocument.Load(_configPath);
            var countElement = doc.Root?.Element("monitors")?.Element("count");
            
            if (countElement == null)
            {
                return Result<DeviceManager.Unit, VDDError>.Failure(
                    new VDDError.ConfigParseFailed("Missing <monitors><count> element", new Exception("Element not found")));
            }

            countElement.Value = count.ToString();

            doc.Save(tempPath);
            
            using (var fs = new FileStream(tempPath, FileMode.Open, FileAccess.Read))
            {
                fs.Flush(flushToDisk: true);
            }

            File.Move(tempPath, _configPath, overwrite: true);
            
            _logger.Info($"VDD config updated: count={count}");
            return Result<DeviceManager.Unit, VDDError>.Success(DeviceManager.Unit.Value);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to write VDD config: {ex.Message}", ex);
            return Result<DeviceManager.Unit, VDDError>.Failure(new VDDError.ConfigWriteFailed(ex.Message, ex));
        }
        finally
        {
            if (lockAcquired)
                _mutex.ReleaseMutex();
        }
    }

    public void Dispose()
    {
        _mutex?.Dispose();
    }
}
