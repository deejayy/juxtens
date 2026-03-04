using System.Windows;
using Juxtens.Logger;
using Juxtens.GStreamer;
using Juxtens.DeviceManager;
using Juxtens.VDDControl;

namespace Juxtens.Daemon;

public partial class App : System.Windows.Application
{
    private ILogger? _logger;
    private DaemonOrchestrator? _orchestrator;
    private WebSocketServer? _wsServer;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _logger = new FileLogger("daemon.log");
        _logger.Info("Daemon starting...");

        try
        {
            var deviceManager = new WindowsDeviceManager(new DeviceManagerConfig(), _logger);
            var vddController = new VDDController(deviceManager, _logger);
            var gstreamerManager = new GStreamerManager();

            _orchestrator = new DaemonOrchestrator(vddController, gstreamerManager, _logger);
            _wsServer = new WebSocketServer(_orchestrator, _logger);
            _wsServer.Start();

            var mainWindow = new MainWindow(_wsServer, _logger);
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            _logger.Error("Fatal error during startup", ex);
            System.Windows.MessageBox.Show($"Fatal error: {ex.Message}", "Daemon Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Info("Daemon shutting down");
        _wsServer?.Dispose();
        _orchestrator?.Dispose();
        base.OnExit(e);
    }
}
