using System.Windows;
using Juxtens.Logger;
using Juxtens.GStreamer;

namespace Juxtens.Client;

public partial class App : Application
{
    private ILogger? _logger;
    private WebSocketClient? _wsClient;
    private StreamManager? _streamManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _logger = new FileLogger("client.log");
        _logger.Info("Client starting...");

        try
        {
            var gstreamerManager = new GStreamerManager();
            _wsClient = new WebSocketClient(_logger);
            _streamManager = new StreamManager(gstreamerManager, _logger);

            var mainWindow = new MainWindow(_wsClient, _streamManager, _logger);
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            _logger.Error("Fatal error during startup", ex);
            MessageBox.Show($"Fatal error: {ex.Message}", "Client Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Info("Client shutting down");
        _wsClient?.Dispose();
        _streamManager?.Dispose();
        base.OnExit(e);
    }
}
