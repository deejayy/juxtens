using System.Windows;
using Juxtens.Logger;

namespace Juxtens.Daemon;

public partial class MainWindow : Window
{
    private readonly WebSocketServer _wsServer;
    private readonly ILogger _logger;

    public MainWindow(WebSocketServer wsServer, ILogger logger)
    {
        _wsServer = wsServer;
        _logger = logger;

        InitializeComponent();
        AttachEventHandlers();
        
        LogToUI("WebSocket server listening on 0.0.0.0:5021");
    }

    private void AttachEventHandlers()
    {
        _wsServer.MessageLogged += OnWebSocketMessage;
        _wsServer.ClientConnected += OnClientConnected;
        _wsServer.ClientDisconnected += OnClientDisconnected;

        Closing += OnWindowClosing;
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        var aboutWindow = new AboutWindow();
        aboutWindow.Owner = this;
        aboutWindow.ShowDialog();
    }

    private void OnWebSocketMessage(string message)
    {
        LogToUI($"[WS] {message}");
    }

    private void OnClientConnected()
    {
        LogToUI("[DAEMON] Client connected");
    }

    private void OnClientDisconnected()
    {
        LogToUI("[DAEMON] Client disconnected, cleaning up...");
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _logger.Info("Daemon form closing");
    }

    private void LogToUI(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => LogToUI(message));
            return;
        }

        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        LogTextBox.AppendText($"[{timestamp}] {message}{Environment.NewLine}");
        LogTextBox.ScrollToEnd();
        _logger.Info(message);
    }
}
