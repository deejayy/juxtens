using System.Windows;
using Juxtens.Logger;

namespace Juxtens.Client;

public partial class MainWindow : Window
{
    private readonly WebSocketClient _wsClient;
    private readonly StreamManager _streamManager;
    private readonly ILogger _logger;

    public MainWindow(WebSocketClient wsClient, StreamManager streamManager, ILogger logger)
    {
        _wsClient = wsClient;
        _streamManager = streamManager;
        _logger = logger;

        InitializeComponent();
        AttachEventHandlers();
        UpdateButtonStates();
    }

    private void AttachEventHandlers()
    {
        _wsClient.Connected += OnConnected;
        _wsClient.Disconnected += OnDisconnected;
        _wsClient.StreamStarted += OnStreamStarted;
        _wsClient.StreamStopped += OnStreamStopped;
        _wsClient.ErrorReceived += OnErrorReceived;
        _wsClient.MessageLogged += OnWebSocketMessage;

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
    }

    private void OnDisconnected()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(OnDisconnected);
            return;
        }

        LogToUI("[CLIENT] Disconnected from daemon");
        AddressTextBox.IsEnabled = true;
        UpdateButtonStates();
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

        LogToUI($"[WS] {message}");
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
        _logger.Info("Client window closing");

        if (_wsClient.IsConnected)
        {
            e.Cancel = true;
            
            await Task.Run(async () =>
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
            });

            e.Cancel = false;
            Close();
        }
    }

    private void UpdateButtonStates()
    {
        ConnectButton.Content = _wsClient.IsConnected ? "Disconnect" : "Connect";
        AddStreamButton.IsEnabled = _wsClient.IsConnected;
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
