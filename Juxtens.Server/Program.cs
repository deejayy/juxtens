using Juxtens.DeviceManager;
using Juxtens.Logger;
using Juxtens.VDDControl;

namespace Juxtens.Server;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        
        var logPath = Path.Combine("device-manager.log");
        
        using var logger = new FileLogger(logPath);
        logger.Info("Application started");
        
        var config = DeviceManagerConfig.Default;
        var deviceManager = new WindowsDeviceManager(config, logger);
        using var vddController = new VDDController(deviceManager, logger);
        
        Application.Run(new MainForm(deviceManager, vddController, logger));
        
        logger.Info("Application exiting");
    }
}
