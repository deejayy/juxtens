using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Juxtens.Logger;

namespace Juxtens.Client;

public sealed class TrayIconService : IDisposable
{
    private NotifyIcon? _notifyIcon;
    private readonly ILogger _logger;
    private Window? _mainWindow;
    private readonly WebSocketClient _wsClient;
    private readonly StreamManager _streamManager;
    private readonly Func<MainWindow> _windowFactory;
    private ToolStripMenuItem? _connectionStatusItem;
    private ToolStripMenuItem? _activeScreensItem;
    private ToolStripMenuItem? _requestScreenItem;
    private bool _isQuitting = false;

    public TrayIconService(
        WebSocketClient wsClient,
        StreamManager streamManager,
        ILogger logger,
        Func<MainWindow> windowFactory)
    {
        _wsClient = wsClient ?? throw new ArgumentNullException(nameof(wsClient));
        _streamManager = streamManager ?? throw new ArgumentNullException(nameof(streamManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _windowFactory = windowFactory ?? throw new ArgumentNullException(nameof(windowFactory));
        
        Initialize();
    }

    public void SetMainWindow(Window window)
    {
        _mainWindow = window;
    }

    private void Initialize()
    {
        _notifyIcon = new NotifyIcon();
        
        UpdateTrayIcon();
        UpdateTooltip();
        
        _notifyIcon.Visible = true;
        
        CreateContextMenu();
        
        _notifyIcon.MouseClick += NotifyIcon_MouseClick;
        _notifyIcon.MouseDoubleClick += NotifyIcon_MouseDoubleClick;
        
        AttachEventHandlers();
        
        _logger.Info("Tray icon initialized");
    }

    private void AttachEventHandlers()
    {
        _wsClient.Connected += OnConnectionStateChanged;
        _wsClient.Disconnected += OnConnectionStateChanged;
        _wsClient.StreamStarted += OnStreamCountChanged;
        _wsClient.StreamStopped += OnStreamCountChanged;
        _streamManager.ReceiversChanged += OnReceiversChanged;
    }

    private void OnConnectionStateChanged()
    {
        if (_notifyIcon?.ContextMenuStrip != null)
        {
            if (_notifyIcon.ContextMenuStrip.IsHandleCreated)
            {
                _notifyIcon.ContextMenuStrip.Invoke(() =>
                {
                    UpdateTooltip();
                    UpdateMenuItems();
                });
            }
            else
            {
                UpdateTooltip();
                UpdateMenuItems();
            }
        }
    }

    private void OnStreamCountChanged(ushort port, uint vdIndex, uint monitorIndex)
    {
        if (_notifyIcon?.ContextMenuStrip != null)
        {
            if (_notifyIcon.ContextMenuStrip.IsHandleCreated)
            {
                _notifyIcon.ContextMenuStrip.Invoke(() =>
                {
                    UpdateTooltip();
                    UpdateMenuItems();
                });
            }
            else
            {
                UpdateTooltip();
                UpdateMenuItems();
            }
        }
    }

    private void OnStreamCountChanged(ushort port)
    {
        if (_notifyIcon?.ContextMenuStrip != null)
        {
            if (_notifyIcon.ContextMenuStrip.IsHandleCreated)
            {
                _notifyIcon.ContextMenuStrip.Invoke(() =>
                {
                    UpdateTooltip();
                    UpdateMenuItems();
                });
            }
            else
            {
                UpdateTooltip();
                UpdateMenuItems();
            }
        }
    }

    private void OnReceiversChanged()
    {
        if (_notifyIcon?.ContextMenuStrip != null)
        {
            if (_notifyIcon.ContextMenuStrip.IsHandleCreated)
            {
                _notifyIcon.ContextMenuStrip.Invoke(() =>
                {
                    UpdateTooltip();
                    UpdateMenuItems();
                });
            }
            else
            {
                UpdateTooltip();
                UpdateMenuItems();
            }
        }
    }

    private void UpdateTrayIcon()
    {
        try
        {
            var iconUri = new Uri("pack://application:,,,/Resources/Icon/juxtens-icon.png", UriKind.Absolute);
            var streamInfo = System.Windows.Application.GetResourceStream(iconUri);
            if (streamInfo != null)
            {
                using var bitmap = new Bitmap(streamInfo.Stream);
                _notifyIcon!.Icon = Icon.FromHandle(bitmap.GetHicon());
            }
            else
            {
                _notifyIcon!.Icon = SystemIcons.Application;
            }
        }
        catch
        {
            _notifyIcon!.Icon = SystemIcons.Application;
        }
    }

    private void UpdateTooltip()
    {
        if (_notifyIcon == null) return;

        var connectionStatus = _wsClient.IsConnected 
            ? $"Connected: {_wsClient.RemoteAddress}" 
            : "Disconnected";
        
        var screenCount = _streamManager.ActiveScreenCount;
        var screenText = screenCount == 1 ? "screen" : "screens";
        
        _notifyIcon.Text = $"Juxtens - {connectionStatus}\n{screenCount} active {screenText}";
    }

    private void CreateContextMenu()
    {
        var contextMenu = new ContextMenuStrip();

        _connectionStatusItem = new ToolStripMenuItem("Disconnected");
        _connectionStatusItem.Enabled = false;
        contextMenu.Items.Add(_connectionStatusItem);

        _activeScreensItem = new ToolStripMenuItem("0 active screens");
        _activeScreensItem.Enabled = false;
        contextMenu.Items.Add(_activeScreensItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        _requestScreenItem = new ToolStripMenuItem("Request Screen");
        _requestScreenItem.Click += async (s, e) => await RequestScreenAsync();
        contextMenu.Items.Add(_requestScreenItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        var aboutMenuItem = new ToolStripMenuItem("About");
        aboutMenuItem.Click += (s, e) => ShowAbout();
        contextMenu.Items.Add(aboutMenuItem);

        var quitMenuItem = new ToolStripMenuItem("Quit");
        quitMenuItem.Click += (s, e) => QuitApplication();
        contextMenu.Items.Add(quitMenuItem);

        _notifyIcon!.ContextMenuStrip = contextMenu;
        
        UpdateMenuItems();
    }

    private void UpdateMenuItems()
    {
        if (_connectionStatusItem == null || _activeScreensItem == null || _requestScreenItem == null) return;

        if (_wsClient.IsConnected)
        {
            _connectionStatusItem.Text = $"Connected: {_wsClient.RemoteAddress}";
            _requestScreenItem.Enabled = true;
        }
        else
        {
            _connectionStatusItem.Text = "Disconnected";
            _requestScreenItem.Enabled = false;
        }

        var screenCount = _streamManager.ActiveScreenCount;
        var screenText = screenCount == 1 ? "screen" : "screens";
        _activeScreensItem.Text = $"{screenCount} active {screenText}";
    }

    private void ShowAbout()
    {
        if (_mainWindow == null)
        {
            EnsureWindowExists();
        }

        _mainWindow?.Dispatcher.Invoke(() =>
        {
            var aboutWindow = new AboutWindow();
            aboutWindow.Owner = _mainWindow;
            aboutWindow.ShowDialog();
        });
    }

    private async Task RequestScreenAsync()
    {
        try
        {
            await _wsClient.SendAddStreamAsync();
            _logger.Info("Screen request sent from tray menu");
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to request screen from tray", ex);
        }
    }

    private void QuitApplication()
    {
        _logger.Info("Application exit requested from tray menu");
        _isQuitting = true;
        System.Windows.Application.Current.Shutdown();
    }

    public bool IsQuitting => _isQuitting;

    public void MinimizeToTray()
    {
        _mainWindow?.Hide();
        _logger.Info("Window minimized to tray");
    }

    private void NotifyIcon_MouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            EnsureWindowExists();
            ToggleWindowVisibility();
        }
    }

    private void NotifyIcon_MouseDoubleClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            EnsureWindowExists();
            ToggleWindowVisibility();
        }
    }

    private void EnsureWindowExists()
    {
        if (_mainWindow == null || !_mainWindow.IsLoaded)
        {
            _mainWindow = _windowFactory();
            _mainWindow.Show();
            _logger.Info("Created and showed new main window");
        }
    }

    private void ToggleWindowVisibility()
    {
        if (_mainWindow == null) return;

        if (_mainWindow.WindowState == WindowState.Minimized || !_mainWindow.IsVisible)
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
            _logger.Info("Window restored from tray");
        }
        else
        {
            _mainWindow.Hide();
            _logger.Info("Window hidden to tray");
        }
    }

    public void Dispose()
    {
        if (_notifyIcon != null)
        {
            _wsClient.Connected -= OnConnectionStateChanged;
            _wsClient.Disconnected -= OnConnectionStateChanged;
            _streamManager.ReceiversChanged -= OnReceiversChanged;
            
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }
}
