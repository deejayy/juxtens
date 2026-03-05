using System.Reflection;
using System.Windows;
using Juxtens.Logger;
using Juxtens.GStreamer;

namespace Juxtens.Client;

public partial class App : System.Windows.Application
{
    private ILogger? _logger;
    private WebSocketClient? _wsClient;
    private StreamManager? _streamManager;
    private TrayIconService? _trayIcon;
    private ClientConfig? _config;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _logger = new FileLogger("client.log");
        
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var version = assembly
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "0.0.0.0";
        _logger.Info($"Juxtens Client v{version} starting...");

        try
        {
            _config = ClientConfig.Load(_logger);
            
            var gstreamerManager = new GStreamerManager(null, _logger);
            _wsClient = new WebSocketClient(_logger);
            _streamManager = new StreamManager(gstreamerManager, _logger);

            _trayIcon = new TrayIconService(_wsClient, _streamManager, _logger, CreateMainWindow);
            
            var mainWindow = CreateMainWindow();
            mainWindow.Show();
            
            _ = mainWindow.TriggerAutoConnectAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("Fatal error during startup", ex);
            System.Windows.MessageBox.Show($"Fatal error: {ex.Message}", "Client Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private MainWindow CreateMainWindow()
    {
        var window = new MainWindow(_wsClient!, _streamManager!, _logger!, _config!, _trayIcon);
        _trayIcon!.SetMainWindow(window);
        return window;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Info("Client shutting down");
        _trayIcon?.Dispose();
        _wsClient?.Dispose();
        _streamManager?.Dispose();
        base.OnExit(e);
    }
}
