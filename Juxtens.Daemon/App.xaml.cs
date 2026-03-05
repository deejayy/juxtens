using System.Reflection;
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
    private TrayIconService? _trayIcon;
    private VDDController? _vddController;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _logger = new FileLogger("daemon.log");
        
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "0.0.0.0";
        _logger.Info($"Juxtens Daemon v{version} starting...");

        try
        {
            var deviceManager = new WindowsDeviceManager(new DeviceManagerConfig(), _logger);
            _vddController = new VDDController(deviceManager, _logger);
            var gstreamerManager = new GStreamerManager(null, _logger);

            _orchestrator = new DaemonOrchestrator(_vddController, gstreamerManager, _logger);
            _wsServer = new WebSocketServer(_orchestrator, _logger);
            _wsServer.Start();

            _trayIcon = new TrayIconService(_wsServer, _orchestrator, _logger, CreateMainWindow);
            var mainWindow = CreateMainWindow();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            _logger.Error("Fatal error during startup", ex);
            System.Windows.MessageBox.Show($"Fatal error: {ex.Message}", "Daemon Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            Shutdown();
        }
    }
    
    private MainWindow CreateMainWindow()
    {
        var window = new MainWindow(_wsServer!, _vddController!, _logger!, _trayIcon);
        _trayIcon!.SetMainWindow(window);
        return window;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Info("Daemon shutting down");
        _trayIcon?.Dispose();
        _wsServer?.Dispose();
        _orchestrator?.Dispose();
        base.OnExit(e);
    }
}
