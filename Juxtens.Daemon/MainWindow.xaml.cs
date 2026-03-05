using System.Windows;
using System.Windows.Media;
using Juxtens.Logger;

namespace Juxtens.Daemon;

public partial class MainWindow : Window
{
    private readonly WebSocketServer _wsServer;
    private readonly ILogger _logger;
    private readonly TrayIconService? _trayIcon;

    public MainWindow(WebSocketServer wsServer, ILogger logger, TrayIconService? trayIcon = null)
    {
        _wsServer = wsServer;
        _logger = logger;
        _trayIcon = trayIcon;

        InitializeComponent();
        AttachEventHandlers();
        
        LogToUI("WebSocket server listening on 0.0.0.0:5021");
    }

    private void AttachEventHandlers()
    {
        _wsServer.MessageLogged += OnWebSocketMessage;
        _wsServer.ClientConnected += OnClientConnected;
        _wsServer.ClientDisconnected += OnClientDisconnected;
        _wsServer.HeartbeatStatusChanged += OnHeartbeatStatusChanged;

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
        if (message.Contains("Ping") || message.Contains("Pong"))
        {
            return;
        }
        LogToUI($"[WS] {message}");
    }

    private void OnClientConnected()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(OnClientConnected);
            return;
        }

        LogToUI("[DAEMON] Client connected");
        UpdateHeartbeatIndicator(0);
        ConnectionStatusText.Text = $"Client connected: {_wsServer.ClientAddress}";
        ConnectionStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 255, 0));
    }

    private void OnClientDisconnected()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(OnClientDisconnected);
            return;
        }

        LogToUI("[DAEMON] Client disconnected, cleaning up...");
        UpdateHeartbeatIndicator(-1);
        ConnectionStatusText.Text = "No client connected";
        ConnectionStatusText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(128, 128, 128));
    }

    private void OnHeartbeatStatusChanged(int missedHeartbeats)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnHeartbeatStatusChanged(missedHeartbeats));
            return;
        }

        UpdateHeartbeatIndicator(missedHeartbeats);
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _logger.Info("Daemon form closing");
        
        if (_trayIcon != null && !_trayIcon.IsQuitting)
        {
            _logger.Info("Window closed, but app continues in tray");
        }
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

    private void UpdateHeartbeatIndicator(int missedHeartbeats)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => UpdateHeartbeatIndicator(missedHeartbeats));
            return;
        }

        SolidColorBrush brush = missedHeartbeats switch
        {
            -1 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(128, 128, 128)),      // Gray - disconnected
            0 or 1 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 255, 0)),      // Green - healthy
            2 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 255, 0)),         // Yellow - 1 skip
            3 => new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 165, 0)),         // Orange - 2 skips
            _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 0, 0))            // Red - 3+ skips
        };

        HeartbeatIndicator.Fill = brush;
    }
}
