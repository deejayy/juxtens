using System.Windows;
using System.Windows.Media;
using Juxtens.Logger;

namespace Juxtens.Client;

public partial class MainWindow : Window
{
    private readonly WebSocketClient _wsClient;
    private readonly StreamManager _streamManager;
    private readonly ILogger _logger;
    private readonly TrayIconService? _trayIcon;
    private readonly ClientConfig _config;

    public MainWindow(WebSocketClient wsClient, StreamManager streamManager, ILogger logger, ClientConfig config, TrayIconService? trayIcon = null)
    {
        _wsClient = wsClient;
        _streamManager = streamManager;
        _logger = logger;
        _config = config;
        _trayIcon = trayIcon;

        InitializeComponent();
        LoadConfig();
        AttachEventHandlers();
        UpdateButtonStates();
    }

    private void LoadConfig()
    {
        AddressTextBox.Text = _config.LastConnectionAddress;
        AutoConnectCheckBox.IsChecked = _config.AutoConnectOnStartup;
    }

    public async Task TriggerAutoConnectAsync()
    {
        if (_config.AutoConnectOnStartup && !string.IsNullOrEmpty(_config.LastConnectionAddress))
        {
            _logger.Info("Auto-connect: attempting to connect...");
            await Task.Delay(500);
            await ConnectAsync();
        }
    }

    private void AttachEventHandlers()
    {
        _wsClient.Connected += OnConnected;
        _wsClient.Disconnected += OnDisconnected;
        _wsClient.StreamStarted += OnStreamStarted;
        _wsClient.StreamStopped += OnStreamStopped;
        _wsClient.ErrorReceived += OnErrorReceived;
        _wsClient.MessageLogged += OnWebSocketMessage;
        _wsClient.HeartbeatStatusChanged += OnHeartbeatStatusChanged;

        _streamManager.ReceiverExited += OnReceiverExited;

        Closing += OnWindowClosing;
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_wsClient.IsConnected)
        {
            await DisconnectAsync();
        }
        else
        {
            await ConnectAsync();
        }
    }

    private async Task ConnectAsync()
    {
        var address = AddressTextBox.Text.Trim();
        if (string.IsNullOrEmpty(address))
        {
            LogToUI("[ERROR] Please enter daemon address");
            return;
        }

        ConnectButton.IsEnabled = false;
        AddressTextBox.IsEnabled = false;

        try
        {
            await _wsClient.ConnectAsync(address);
            
            _config.LastConnectionAddress = address;
            _config.Save(_logger);
        }
        catch (Exception ex)
        {
            _logger.Error("Connection failed", ex);
            LogToUI($"[ERROR] Connection failed: {ex.Message}");
            ConnectButton.IsEnabled = true;
            AddressTextBox.IsEnabled = true;
        }
    }

    private async Task DisconnectAsync()
    {
        ConnectButton.IsEnabled = false;

        try
        {
            _streamManager.StopAllReceivers();
            await _wsClient.DisconnectAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("Disconnect failed", ex);
            LogToUI($"[ERROR] Disconnect failed: {ex.Message}");
        }
        finally
        {
            ConnectButton.IsEnabled = true;
            AddressTextBox.IsEnabled = true;
        }
    }

    private async void AddStreamButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _wsClient.SendAddStreamAsync();
        }
        catch (Exception ex)
        {
            _logger.Error("Add stream failed", ex);
            LogToUI($"[ERROR] Add stream failed: {ex.Message}");
        }
    }

    private void AboutButton_Click(object sender, RoutedEventArgs e)
    {
        var aboutWindow = new AboutWindow();
        aboutWindow.Owner = this;
        aboutWindow.ShowDialog();
    }

    private void AutoConnectCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _config.AutoConnectOnStartup = AutoConnectCheckBox.IsChecked == true;
        _config.Save(_logger);
        _logger.Info($"Auto-connect set to: {_config.AutoConnectOnStartup}");
    }

    private void OnConnected()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(OnConnected);
            return;
        }

        LogToUI("[CLIENT] Connected to daemon");
        ConnectButton.IsEnabled = true;
        UpdateButtonStates();
        UpdateHeartbeatIndicator(0);
    }

    private void OnDisconnected()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(OnDisconnected);
            return;
        }

        LogToUI("[CLIENT] Disconnected from daemon");
        ConnectButton.IsEnabled = true;
        AddressTextBox.IsEnabled = true;
        UpdateButtonStates();
        UpdateHeartbeatIndicator(-1);
    }

    private void OnStreamStarted(ushort port, uint vdIndex, uint monitorIndex)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnStreamStarted(port, vdIndex, monitorIndex));
            return;
        }

        LogToUI($"[CLIENT] Stream started: Port={port}, VD={vdIndex}, Monitor={monitorIndex}");
        _streamManager.StartReceiver(port, vdIndex, monitorIndex);
    }

    private void OnStreamStopped(ushort port)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnStreamStopped(port));
            return;
        }

        LogToUI($"[CLIENT] Stream stopped: Port={port}");
        _streamManager.StopReceiver(port);
    }

    private void OnErrorReceived(string error)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnErrorReceived(error));
            return;
        }

        LogToUI($"[DAEMON ERROR] {error}");
    }

    private void OnWebSocketMessage(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnWebSocketMessage(message));
            return;
        }

        if (message.Contains("Ping") || message.Contains("Pong"))
        {
            return;
        }

        LogToUI($"[WS] {message}");
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

    private async void OnReceiverExited(ushort port)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnReceiverExited(port));
            return;
        }

        LogToUI($"[CLIENT] Receiver exited: Port={port}, notifying daemon...");

        try
        {
            await _wsClient.SendRemoveStreamAsync(port);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to notify daemon of receiver exit", ex);
            LogToUI($"[ERROR] Failed to notify daemon: {ex.Message}");
        }
    }

    private async void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_trayIcon?.IsQuitting == true)
        {
            _logger.Info("Client window closing - application quit");

            if (_wsClient.IsConnected)
            {
                try
                {
                    _streamManager.StopAllReceivers();
                    await _wsClient.DisconnectAsync();
                }
                catch (Exception ex)
                {
                    _logger.Error("Error during cleanup on close", ex);
                }
            }
        }
        else
        {
            _logger.Info("Client window closing - app continues running");
        }
    }

    private void UpdateButtonStates()
    {
        ConnectButton.Content = _wsClient.IsConnected ? "Disconnect" : "Connect";
        AddStreamButton.IsEnabled = _wsClient.IsConnected;
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
