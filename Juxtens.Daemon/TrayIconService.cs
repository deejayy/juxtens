using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Juxtens.Logger;

namespace Juxtens.Daemon;

public sealed class TrayIconService : IDisposable
{
    private NotifyIcon? _notifyIcon;
    private readonly ILogger _logger;
    private Window? _mainWindow;
    private readonly WebSocketServer _wsServer;
    private readonly DaemonOrchestrator _orchestrator;
    private readonly Func<MainWindow> _windowFactory;
    private ToolStripMenuItem? _connectionStatusItem;
    private ToolStripMenuItem? _activeStreamsItem;
    private bool _isQuitting = false;

    public TrayIconService(
        WebSocketServer wsServer,
        DaemonOrchestrator orchestrator,
        ILogger logger,
        Func<MainWindow> windowFactory)
    {
        _wsServer = wsServer ?? throw new ArgumentNullException(nameof(wsServer));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
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
        _wsServer.ClientConnected += OnConnectionStateChanged;
        _wsServer.ClientDisconnected += OnConnectionStateChanged;
        _orchestrator.StreamAdded += OnStreamCountChanged;
        _orchestrator.StreamRemoved += OnStreamCountChanged;
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

    private void OnStreamCountChanged(object? sender, StreamInfo e)
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

        var connectionStatus = _wsServer.IsClientConnected 
            ? $"Connected: {_wsServer.ClientAddress}" 
            : "Disconnected";
        
        var streamCount = _orchestrator.Streams.Count;
        var streamText = streamCount == 1 ? "stream" : "streams";
        
        _notifyIcon.Text = $"Juxtens Daemon - {connectionStatus}\n{streamCount} active {streamText}";
    }

    private void CreateContextMenu()
    {
        var contextMenu = new ContextMenuStrip();

        _connectionStatusItem = new ToolStripMenuItem("Disconnected");
        _connectionStatusItem.Enabled = false;
        contextMenu.Items.Add(_connectionStatusItem);

        _activeStreamsItem = new ToolStripMenuItem("0 active streams");
        _activeStreamsItem.Enabled = false;
        contextMenu.Items.Add(_activeStreamsItem);

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
        if (_connectionStatusItem == null || _activeStreamsItem == null) return;

        if (_wsServer.IsClientConnected)
        {
            _connectionStatusItem.Text = $"Connected: {_wsServer.ClientAddress}";
        }
        else
        {
            _connectionStatusItem.Text = "Disconnected";
        }

        var streamCount = _orchestrator.Streams.Count;
        var streamText = streamCount == 1 ? "stream" : "streams";
        _activeStreamsItem.Text = $"{streamCount} active {streamText}";
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

    private void QuitApplication()
    {
        _logger.Info("Application exit requested from tray menu");
        _isQuitting = true;
        System.Windows.Application.Current.Shutdown();
    }

    public bool IsQuitting => _isQuitting;

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
            _wsServer.ClientConnected -= OnConnectionStateChanged;
            _wsServer.ClientDisconnected -= OnConnectionStateChanged;
            _orchestrator.StreamAdded -= OnStreamCountChanged;
            _orchestrator.StreamRemoved -= OnStreamCountChanged;
            
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }
}
