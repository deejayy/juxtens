using System.Windows;
using System.Windows.Media;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using Juxtens.Logger;
using Juxtens.VDDControl;

namespace Juxtens.Daemon;

public partial class MainWindow : Window
{
    private readonly WebSocketServer _wsServer;
    private readonly IVDDController _vddController;
    private readonly ILogger _logger;
    private readonly TrayIconService? _trayIcon;

    public MainWindow(WebSocketServer wsServer, IVDDController vddController, ILogger logger, TrayIconService? trayIcon = null)
    {
        _wsServer = wsServer;
        _vddController = vddController;
        _logger = logger;
        _trayIcon = trayIcon;

        InitializeComponent();
        AttachEventHandlers();
        CheckVDDInstallation();
        
        LogListeningAddresses();
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

    private async void InstallVDDButton_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            "This will install the Virtual Display Driver (VDD).\n\n" +
            "Administrator privileges are required.\n\n" +
            "Do you want to continue?",
            "Install VDD Driver",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        InstallVDDButton.IsEnabled = false;
        LogToUI("[DAEMON] Installing VDD driver...");

        try
        {
            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var vddInfPath = Path.Combine(appDirectory, "vdd", "MttVDD.inf");
            var vddXmlPath = Path.Combine(appDirectory, "vdd", "vdd_settings.xml");
            var devconPath = Path.Combine(appDirectory, "vdd", "devcon.exe");

            if (!File.Exists(vddInfPath))
            {
                LogToUI($"[ERROR] VDD driver file not found: {vddInfPath}");
                System.Windows.MessageBox.Show(
                    $"VDD driver file not found:\n{vddInfPath}",
                    "Installation Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                InstallVDDButton.IsEnabled = true;
                return;
            }

            if (!File.Exists(vddXmlPath))
            {
                LogToUI($"[ERROR] VDD settings file not found: {vddXmlPath}");
                System.Windows.MessageBox.Show(
                    $"VDD settings file not found:\n{vddXmlPath}",
                    "Installation Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                InstallVDDButton.IsEnabled = true;
                return;
            }

            if (!File.Exists(devconPath))
            {
                LogToUI($"[ERROR] devcon.exe not found: {devconPath}");
                System.Windows.MessageBox.Show(
                    $"devcon.exe not found:\n{devconPath}\n\nPlease ensure devcon.exe is in the vdd directory.",
                    "Installation Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                InstallVDDButton.IsEnabled = true;
                return;
            }

            // Step 1: Copy XML settings file to driver directory
            const string vddDriverDir = @"C:\VirtualDisplayDriver";
            var vddXmlDestPath = Path.Combine(vddDriverDir, "vdd_settings.xml");

            try
            {
                LogToUI("[DAEMON] Creating VDD driver directory...");
                Directory.CreateDirectory(vddDriverDir);
                
                LogToUI($"[DAEMON] Copying settings file to {vddXmlDestPath}...");
                File.Copy(vddXmlPath, vddXmlDestPath, overwrite: true);
                
                _logger.Info($"VDD settings copied to: {vddXmlDestPath}");
            }
            catch (UnauthorizedAccessException)
            {
                LogToUI("[ERROR] Access denied creating VDD directory - administrator privileges required");
                System.Windows.MessageBox.Show(
                    "Failed to create VDD driver directory.\n\nAdministrator privileges are required.\n\nPlease run the application as administrator.",
                    "Installation Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                InstallVDDButton.IsEnabled = true;
                return;
            }
            catch (Exception ex)
            {
                LogToUI($"[ERROR] Failed to copy settings file: {ex.Message}");
                System.Windows.MessageBox.Show(
                    $"Failed to copy VDD settings file:\n\n{ex.Message}",
                    "Installation Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                InstallVDDButton.IsEnabled = true;
                return;
            }

            // Step 2: Install driver using devcon
            _logger.Info($"Installing VDD from: {vddInfPath}");
            _logger.Info($"Using devcon: {devconPath}");

            var startInfo = new ProcessStartInfo
            {
                FileName = devconPath,
                Arguments = $"install \"{vddInfPath}\" \"Root\\MttVDD\"",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = false
            };

            _logger.Info($"Executing: {startInfo.FileName} {startInfo.Arguments}");
            LogToUI("[DAEMON] Installing driver (UAC prompt will appear)...");

            var process = Process.Start(startInfo);
            if (process == null)
            {
                LogToUI("[ERROR] Failed to start devcon.exe process");
                System.Windows.MessageBox.Show("Failed to start installation process.", "Installation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                InstallVDDButton.IsEnabled = true;
                return;
            }

            await process.WaitForExitAsync();

            _logger.Info($"devcon.exe exited with code: {process.ExitCode}");

            // Step 3: Verify driver is actually installed
            var isInstalled = _vddController.IsDriverInstalled();

            if (isInstalled)
            {
                LogToUI($"[DAEMON] VDD driver installed successfully (exit code: {process.ExitCode})");
                System.Windows.MessageBox.Show(
                    "VDD driver installed successfully!\n\nThe driver is now available for use.",
                    "Installation Successful",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                
                CheckVDDInstallation();
            }
            else
            {
                LogToUI($"[ERROR] VDD installation failed - driver not detected (exit code: {process.ExitCode})");
                System.Windows.MessageBox.Show(
                    $"VDD driver installation failed.\n\nExit code: {process.ExitCode}\nDriver not detected after installation.\n\nCheck logs for details.",
                    "Installation Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                InstallVDDButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("VDD installation failed", ex);
            LogToUI($"[ERROR] VDD installation failed: {ex.Message}");
            System.Windows.MessageBox.Show(
                $"VDD driver installation failed:\n\n{ex.Message}",
                "Installation Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            InstallVDDButton.IsEnabled = true;
        }
    }

    private void CheckVDDInstallation()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(CheckVDDInstallation);
            return;
        }

        var isInstalled = _vddController.IsDriverInstalled();
        
        if (isInstalled)
        {
            InstallVDDButton.IsEnabled = false;
            InstallVDDButton.Content = "VDD Driver Installed";
            LogToUI("[DAEMON] VDD driver is installed");
        }
        else
        {
            InstallVDDButton.IsEnabled = true;
            InstallVDDButton.Content = "Install VDD Driver";
            LogToUI("[WARNING] VDD driver not found - click 'Install VDD Driver' to install");
        }
    }

    private void LogListeningAddresses()
    {
        const int port = 5021;
        
        LogToUI($"[DAEMON] WebSocket server listening on port {port}");
        LogToUI("[DAEMON] Clients can connect to:");
        
        var addresses = GetLocalIPAddresses();
        
        if (addresses.Count == 0)
        {
            LogToUI("[WARNING] No network interfaces found");
            return;
        }

        foreach (var addr in addresses)
        {
            LogToUI($"  • {addr}:{port}");
        }
    }

    private List<string> GetLocalIPAddresses()
    {
        var addresses = new List<string>();

        try
        {
            var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
            
            foreach (var ni in networkInterfaces)
            {
                // Skip loopback and non-operational interfaces
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;
                    
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var ipProperties = ni.GetIPProperties();
                
                foreach (var unicast in ipProperties.UnicastAddresses)
                {
                    // Only include IPv4 addresses
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        addresses.Add(unicast.Address.ToString());
                    }
                }
            }
            
            // Always include localhost as fallback
            if (addresses.Count == 0 || !addresses.Contains("127.0.0.1"))
            {
                addresses.Insert(0, "127.0.0.1");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to enumerate network interfaces", ex);
            addresses.Clear();
            addresses.Add("127.0.0.1");
        }

        return addresses;
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
