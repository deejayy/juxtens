using Juxtens.DeviceManager;
using Juxtens.DeviceManager.Predicates;
using Juxtens.Logger;
using Juxtens.VDDControl;
using Juxtens.GStreamer;

namespace Juxtens.Server;

public partial class MainForm : Form
{
    private readonly IDeviceManager _deviceManager;
    private readonly IVDDController _vddController;
    private readonly ILogger _logger;
    private readonly IGStreamerManager _gstManager;
    private readonly StreamOrchestrator _orchestrator;

    private readonly List<StreamHandle> _senderHandles = new();
    private readonly List<StreamHandle> _receiverHandles = new();
    private readonly object _streamLock = new();

    private TextBox _instanceIdInput = null!;
    private ListBox _deviceList = null!;
    private TextBox _logOutput = null!;
    private Button _findDeviceButton = null!;
    private Button _findVirtualDisplaysButton = null!;
    private Button _getStateButton = null!;
    private Button _enableButton = null!;
    private Button _disableButton = null!;
    private Button _restartButton = null!;
    private Button _refreshListButton = null!;
    
    private Label _vddCountLabel = null!;
    private Label _vddEffectiveLabel = null!;
    private NumericUpDown _vddCountInput = null!;
    private Button _vddSetCountButton = null!;
    private Button _vddAddOneButton = null!;
    private Button _vddRemoveOneButton = null!;
    private Button _vddGetEffectiveButton = null!;

    private TextBox _gstSenderHost = null!;
    private NumericUpDown _gstSenderPort = null!;
    private NumericUpDown _gstSenderMonitor = null!;
    private Button _gstStartSenderButton = null!;
    private Button _gstStopSenderButton = null!;
    private Label _gstSenderStatusLabel = null!;
    private Label _gstSenderCountLabel = null!;

    private NumericUpDown _gstReceiverPort = null!;
    private CheckBox _gstReceiverFullscreen = null!;
    private Button _gstStartReceiverButton = null!;
    private Button _gstStopReceiverButton = null!;
    private Label _gstReceiverStatusLabel = null!;
    private Label _gstReceiverCountLabel = null!;

    private TextBox _gstLogOutput = null!;

    private Button _addStreamButton = null!;
    private Label _streamCountLabel = null!;
    private Label _queueStatusLabel = null!;
    private ListBox _streamListBox = null!;
    private int _addStreamInProgress = 0;

    public MainForm(IDeviceManager deviceManager, IVDDController vddController, ILogger logger)
    {
        _deviceManager = deviceManager;
        _vddController = vddController;
        _logger = logger;
        _gstManager = new GStreamerManager();
        _orchestrator = new StreamOrchestrator(_vddController, _gstManager, _deviceManager, _logger);
        _orchestrator.StatusChanged += OnOrchestratorStatusChanged;
        _orchestrator.StreamAdded += OnOrchestratorStreamChanged;
        _orchestrator.StreamRemoved += OnOrchestratorStreamChanged;
        InitializeComponent();
        LoadVDDCount();
    }

    private void OnOrchestratorStatusChanged(object? sender, string message)
    {
        Log(message);
        if (InvokeRequired)
            BeginInvoke(UpdateOrchestratorUI);
        else
            UpdateOrchestratorUI();
    }

    private void OnOrchestratorStreamChanged(object? sender, StreamPair stream)
    {
        if (InvokeRequired)
            BeginInvoke(UpdateOrchestratorUI);
        else
            UpdateOrchestratorUI();
    }

    private void InitializeComponent()
    {
        Text = "Juxtens Device Manager";
        Size = new Size(1400, 1000);
        StartPosition = FormStartPosition.CenterScreen;

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 4,
            Padding = new Padding(10)
        };

        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 15));

        var leftPanel = CreateLeftPanel();
        var rightPanel = CreateRightPanel();
        var vddPanel = CreateVDDPanel();
        var orchestratorPanel = CreateOrchestratorPanel();
        var gstPanel = CreateGStreamerPanel();
        var logPanel = CreateLogPanel();

        mainLayout.Controls.Add(leftPanel, 0, 0);
        mainLayout.Controls.Add(rightPanel, 1, 0);
        mainLayout.Controls.Add(vddPanel, 2, 0);
        mainLayout.Controls.Add(orchestratorPanel, 0, 1);
        mainLayout.SetColumnSpan(orchestratorPanel, 3);
        mainLayout.Controls.Add(gstPanel, 0, 2);
        mainLayout.SetColumnSpan(gstPanel, 3);
        mainLayout.Controls.Add(logPanel, 0, 3);
        mainLayout.SetColumnSpan(logPanel, 3);

        Controls.Add(mainLayout);

        FormClosing += MainForm_FormClosing;
    }

    private Panel CreateLeftPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(5)
        };

        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var searchGroup = new GroupBox { Text = "Find Device", AutoSize = true, Dock = DockStyle.Top };
        var searchLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true };
        
        _instanceIdInput = new TextBox { Width = 350, PlaceholderText = "Device Instance ID (e.g., ROOT\\DISPLAY\\0000)" };
        _findDeviceButton = new Button { Text = "Find by ID", Width = 150 };
        _findDeviceButton.Click += FindDeviceButton_Click;
        
        _findVirtualDisplaysButton = new Button { Text = "Find Virtual Displays", Width = 150 };
        _findVirtualDisplaysButton.Click += FindVirtualDisplaysButton_Click;
        
        _refreshListButton = new Button { Text = "List All Devices", Width = 150 };
        _refreshListButton.Click += RefreshListButton_Click;

        searchLayout.Controls.Add(_instanceIdInput);
        searchLayout.Controls.Add(_findDeviceButton);
        searchLayout.Controls.Add(_findVirtualDisplaysButton);
        searchLayout.Controls.Add(_refreshListButton);
        searchGroup.Controls.Add(searchLayout);

        var listGroup = new GroupBox { Text = "Devices", Dock = DockStyle.Fill };
        _deviceList = new ListBox { Dock = DockStyle.Fill };
        _deviceList.SelectedIndexChanged += DeviceList_SelectedIndexChanged;
        listGroup.Controls.Add(_deviceList);

        var actionGroup = new GroupBox { Text = "Actions", AutoSize = true, Dock = DockStyle.Bottom };
        var actionLayout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, AutoSize = true };

        _getStateButton = new Button { Text = "Get State", Width = 150, Enabled = false };
        _getStateButton.Click += GetStateButton_Click;
        
        _enableButton = new Button { Text = "Enable", Width = 150, Enabled = false };
        _enableButton.Click += EnableButton_Click;
        
        _disableButton = new Button { Text = "Disable", Width = 150, Enabled = false };
        _disableButton.Click += DisableButton_Click;
        
        _restartButton = new Button { Text = "Restart", Width = 150, Enabled = false };
        _restartButton.Click += RestartButton_Click;

        actionLayout.Controls.Add(_getStateButton);
        actionLayout.Controls.Add(_enableButton);
        actionLayout.Controls.Add(_disableButton);
        actionLayout.Controls.Add(_restartButton);
        actionGroup.Controls.Add(actionLayout);

        layout.Controls.Add(searchGroup, 0, 0);
        layout.Controls.Add(listGroup, 0, 1);
        layout.Controls.Add(actionGroup, 0, 2);
        
        panel.Controls.Add(layout);
        return panel;
    }

    private Panel CreateRightPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        var group = new GroupBox { Text = "Device Details", Dock = DockStyle.Fill };
        var details = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            Font = new Font("Consolas", 9)
        };
        details.Name = "deviceDetails";
        group.Controls.Add(details);
        panel.Controls.Add(group);
        return panel;
    }

    private Panel CreateLogPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        var group = new GroupBox { Text = "Operation Log", Dock = DockStyle.Fill };
        _logOutput = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            Font = new Font("Consolas", 8)
        };
        group.Controls.Add(_logOutput);
        panel.Controls.Add(group);
        return panel;
    }

    private Panel CreateVDDPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        var group = new GroupBox { Text = "VDD Control", Dock = DockStyle.Fill };
        var layout = new FlowLayoutPanel 
        { 
            Dock = DockStyle.Fill, 
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(5)
        };

        _vddCountLabel = new Label 
        { 
            Text = "Config count: ?", 
            AutoSize = true,
            Padding = new Padding(0, 5, 0, 2)
        };

        _vddEffectiveLabel = new Label 
        { 
            Text = "Effective count: ?", 
            AutoSize = true,
            Padding = new Padding(0, 2, 0, 5),
            Font = new Font(Font, FontStyle.Bold)
        };

        var setCountPanel = new Panel { AutoSize = true, Width = 180 };
        var setCountLayout = new FlowLayoutPanel 
        { 
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            Dock = DockStyle.Fill
        };
        
        var setLabel = new Label { Text = "Set count:", AutoSize = true };
        _vddCountInput = new NumericUpDown 
        { 
            Minimum = 1, 
            Maximum = 10, 
            Value = 1,
            Width = 160
        };
        _vddSetCountButton = new Button { Text = "Set VD Count", Width = 160 };
        _vddSetCountButton.Click += VddSetCountButton_Click;
        
        setCountLayout.Controls.Add(setLabel);
        setCountLayout.Controls.Add(_vddCountInput);
        setCountLayout.Controls.Add(_vddSetCountButton);
        setCountPanel.Controls.Add(setCountLayout);

        var quickPanel = new Panel { AutoSize = true, Width = 180 };
        var quickLayout = new FlowLayoutPanel 
        { 
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            Dock = DockStyle.Fill
        };
        
        var quickLabel = new Label { Text = "Quick actions:", AutoSize = true };
        _vddAddOneButton = new Button { Text = "Add 1 VD", Width = 160 };
        _vddAddOneButton.Click += VddAddOneButton_Click;
        
        _vddRemoveOneButton = new Button { Text = "Remove 1 VD", Width = 160 };
        _vddRemoveOneButton.Click += VddRemoveOneButton_Click;
        
        _vddGetEffectiveButton = new Button { Text = "Get Effective Count", Width = 160 };
        _vddGetEffectiveButton.Click += VddGetEffectiveButton_Click;
        
        quickLayout.Controls.Add(quickLabel);
        quickLayout.Controls.Add(_vddAddOneButton);
        quickLayout.Controls.Add(_vddRemoveOneButton);
        quickLayout.Controls.Add(_vddGetEffectiveButton);
        quickPanel.Controls.Add(quickLayout);

        layout.Controls.Add(_vddCountLabel);
        layout.Controls.Add(_vddEffectiveLabel);
        layout.Controls.Add(setCountPanel);
        layout.Controls.Add(quickPanel);
        
        group.Controls.Add(layout);
        panel.Controls.Add(group);
        return panel;
    }

    private Panel CreateOrchestratorPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        var mainGroup = new GroupBox { Text = "Stream Orchestration (Automated VDD + Sender + Receiver)", Dock = DockStyle.Fill };
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(5)
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));

        var controlPanel = new Panel { Dock = DockStyle.Fill };
        var controlLayout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(5)
        };

        _addStreamButton = new Button { Text = "Add Stream", Width = 200, Height = 40 };
        _addStreamButton.Click += AddStreamButton_Click;

        _streamCountLabel = new Label { Text = "Active streams: 0", AutoSize = true, Font = new Font(Font.FontFamily, 10f, FontStyle.Bold) };
        _queueStatusLabel = new Label { Text = "Queue: idle", AutoSize = true, ForeColor = Color.DarkGray };

        var hintLabel = new Label 
        { 
            Text = "Click 'Add Stream' to create VD + start sender/receiver.\nClose receiver window to remove stream.", 
            AutoSize = true,
            ForeColor = Color.DarkGray,
            Font = new Font(Font.FontFamily, 7.5f),
            MaximumSize = new Size(250, 0)
        };

        controlLayout.Controls.Add(_addStreamButton);
        controlLayout.Controls.Add(_streamCountLabel);
        controlLayout.Controls.Add(_queueStatusLabel);
        controlLayout.Controls.Add(hintLabel);
        controlPanel.Controls.Add(controlLayout);

        var listGroup = new GroupBox { Text = "Active Streams", Dock = DockStyle.Fill };
        _streamListBox = new ListBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 9) };
        _streamListBox.DoubleClick += StreamListBox_DoubleClick;
        listGroup.Controls.Add(_streamListBox);

        mainLayout.Controls.Add(controlPanel, 0, 0);
        mainLayout.Controls.Add(listGroup, 1, 0);

        mainGroup.Controls.Add(mainLayout);
        panel.Controls.Add(mainGroup);
        return panel;
    }

    private Panel CreateGStreamerPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        var mainGroup = new GroupBox { Text = "GStreamer Control", Dock = DockStyle.Fill };
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(5)
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));

        var senderPanel = new GroupBox { Text = "Sender", Dock = DockStyle.Fill };
        var senderLayout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(5)
        };

        var hostLabel = new Label { Text = "Host:", AutoSize = true };
        _gstSenderHost = new TextBox { Width = 200, Text = "127.0.0.1" };

        var portLabel = new Label { Text = "Port:", AutoSize = true };
        _gstSenderPort = new NumericUpDown { Minimum = 1024, Maximum = 65535, Value = 5000, Width = 200 };

        var monitorLabel = new Label { Text = "Monitor Index:", AutoSize = true };
        _gstSenderMonitor = new NumericUpDown { Minimum = 0, Maximum = 10, Value = 0, Width = 200 };

        _gstStartSenderButton = new Button { Text = "Start Sender", Width = 200 };
        _gstStartSenderButton.Click += GstStartSenderButton_Click;

        _gstStopSenderButton = new Button { Text = "Stop Sender", Width = 200, Enabled = false };
        _gstStopSenderButton.Click += GstStopSenderButton_Click;

        _gstSenderStatusLabel = new Label { Text = "Active streams: 0", AutoSize = true, ForeColor = Color.Gray };
        _gstSenderCountLabel = new Label { Text = "", AutoSize = true, Font = new Font(Font.FontFamily, 7.5f), ForeColor = Color.DarkGray };

        senderLayout.Controls.Add(hostLabel);
        senderLayout.Controls.Add(_gstSenderHost);
        senderLayout.Controls.Add(portLabel);
        senderLayout.Controls.Add(_gstSenderPort);
        senderLayout.Controls.Add(monitorLabel);
        senderLayout.Controls.Add(_gstSenderMonitor);
        senderLayout.Controls.Add(_gstStartSenderButton);
        senderLayout.Controls.Add(_gstStopSenderButton);
        senderLayout.Controls.Add(_gstSenderStatusLabel);
        senderLayout.Controls.Add(_gstSenderCountLabel);
        senderPanel.Controls.Add(senderLayout);

        var receiverPanel = new GroupBox { Text = "Receiver", Dock = DockStyle.Fill };
        var receiverLayout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(5)
        };

        var recvPortLabel = new Label { Text = "Port:", AutoSize = true };
        _gstReceiverPort = new NumericUpDown { Minimum = 1024, Maximum = 65535, Value = 5000, Width = 200 };

        _gstReceiverFullscreen = new CheckBox { Text = "Start in fullscreen", AutoSize = true };
        
        var fullscreenHintLabel = new Label 
        { 
            Text = "(Press Alt+Enter to toggle)", 
            AutoSize = true, 
            ForeColor = Color.DarkGray,
            Font = new Font(Font.FontFamily, 7.5f)
        };

        _gstStartReceiverButton = new Button { Text = "Start Receiver", Width = 200 };
        _gstStartReceiverButton.Click += GstStartReceiverButton_Click;

        _gstStopReceiverButton = new Button { Text = "Stop Receiver", Width = 200, Enabled = false };
        _gstStopReceiverButton.Click += GstStopReceiverButton_Click;

        _gstReceiverStatusLabel = new Label { Text = "Active streams: 0", AutoSize = true, ForeColor = Color.Gray };
        _gstReceiverCountLabel = new Label { Text = "", AutoSize = true, Font = new Font(Font.FontFamily, 7.5f), ForeColor = Color.DarkGray };

        receiverLayout.Controls.Add(recvPortLabel);
        receiverLayout.Controls.Add(_gstReceiverPort);
        receiverLayout.Controls.Add(_gstReceiverFullscreen);
        receiverLayout.Controls.Add(fullscreenHintLabel);
        receiverLayout.Controls.Add(_gstStartReceiverButton);
        receiverLayout.Controls.Add(_gstStopReceiverButton);
        receiverLayout.Controls.Add(_gstReceiverStatusLabel);
        receiverLayout.Controls.Add(_gstReceiverCountLabel);
        receiverPanel.Controls.Add(receiverLayout);

        var logGroup = new GroupBox { Text = "Stream Logs", Dock = DockStyle.Fill };
        _gstLogOutput = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            Font = new Font("Consolas", 8)
        };
        logGroup.Controls.Add(_gstLogOutput);

        mainLayout.Controls.Add(senderPanel, 0, 0);
        mainLayout.Controls.Add(receiverPanel, 1, 0);
        mainLayout.Controls.Add(logGroup, 2, 0);

        mainGroup.Controls.Add(mainLayout);
        panel.Controls.Add(mainGroup);
        return panel;
    }

    private void FindDeviceButton_Click(object? sender, EventArgs e)
    {
        var instanceId = _instanceIdInput.Text.Trim();
        if (string.IsNullOrEmpty(instanceId))
        {
            Log("Error: Instance ID cannot be empty");
            return;
        }

        try
        {
            var deviceId = new DeviceId(instanceId);
            var result = _deviceManager.FindDevice(deviceId);
            
            result.Match(
                device =>
                {
                    _deviceList.Items.Clear();
                    _deviceList.Items.Add(device);
                    Log($"Found device: {device.Id}");
                    return 0;
                },
                error =>
                {
                    Log($"Error finding device: {error.Message}");
                    return 0;
                });
        }
        catch (Exception ex)
        {
            Log($"Exception: {ex.Message}");
        }
    }

    private void FindVirtualDisplaysButton_Click(object? sender, EventArgs e)
    {
        SafeExecute(() =>
        {
            Log("Searching for virtual display devices...");
            var result = _deviceManager.FindDevices(DevicePredicates.VirtualDisplay());
            
            result.Match(
                devices =>
                {
                    _deviceList.Items.Clear();
                    foreach (var device in devices)
                        _deviceList.Items.Add(device);
                    Log($"Found {devices.Count} virtual display device(s)");
                    return 0;
                },
                error =>
                {
                    Log($"Error: {error.Message}");
                    return 0;
                });
        }, "Find virtual displays");
    }

    private void RefreshListButton_Click(object? sender, EventArgs e)
    {
        SafeExecute(() =>
        {
            Log("Enumerating all devices...");
            var result = _deviceManager.FindDevices(_ => true);
            
            result.Match(
                devices =>
                {
                    _deviceList.Items.Clear();
                    foreach (var device in devices)
                        _deviceList.Items.Add(device);
                    Log($"Found {devices.Count} device(s)");
                    return 0;
                },
                error =>
                {
                    Log($"Error: {error.Message}");
                    return 0;
                });
        }, "Enumerate devices");
    }

    private void DeviceList_SelectedIndexChanged(object? sender, EventArgs e)
    {
        SafeExecute(() =>
        {
            var hasSelection = _deviceList.SelectedItem != null;
            _getStateButton.Enabled = hasSelection;
            _enableButton.Enabled = hasSelection;
            _disableButton.Enabled = hasSelection;
            _restartButton.Enabled = hasSelection;

            if (_deviceList.SelectedItem is DeviceInfo device)
            {
                var details = this.Controls.Find("deviceDetails", true).FirstOrDefault() as TextBox;
                if (details != null)
                {
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine($"Instance ID:");
                    sb.AppendLine($"  {device.Id}");
                    sb.AppendLine();
                    sb.AppendLine($"Friendly Name:");
                    sb.AppendLine($"  {device.FriendlyName ?? "(none)"}");
                    sb.AppendLine();
                    sb.AppendLine($"Description:");
                    sb.AppendLine($"  {device.Description ?? "(none)"}");
                    sb.AppendLine();
                    sb.AppendLine($"Hardware IDs:");
                    if (device.HardwareIds != null && device.HardwareIds.Length > 0)
                    {
                        foreach (var hwId in device.HardwareIds)
                            sb.AppendLine($"  {hwId}");
                    }
                    else
                    {
                        sb.AppendLine("  (none)");
                    }
                    
                    details.Text = sb.ToString();
                }
            }
        }, "Update device details");
    }

    private void GetStateButton_Click(object? sender, EventArgs e)
    {
        SafeExecute(() =>
        {
            if (_deviceList.SelectedItem is not DeviceInfo device)
                return;

            Log($"Getting state for {device.Id}...");
            var result = _deviceManager.GetState(device.Id);
            
            result.Match(
                state =>
                {
                    Log($"Device state: {state}");
                    MessageBox.Show($"Device state: {state}", "Device State", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                },
                error =>
                {
                    Log($"Error: {error.Message}");
                    MessageBox.Show($"Error: {error.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 0;
                });
        }, "Get device state");
    }

    private void EnableButton_Click(object? sender, EventArgs e)
    {
        SafeExecute(() =>
        {
            if (_deviceList.SelectedItem is not DeviceInfo device)
                return;

            if (MessageBox.Show($"Enable device?\n\n{device.Id}", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            Log($"Enabling {device.Id}...");
            var result = _deviceManager.Enable(device.Id);
            
            result.Match(
                _ =>
                {
                    Log("Device enabled successfully");
                    MessageBox.Show("Device enabled successfully", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                },
                error =>
                {
                    Log($"Error: {error.Message}");
                    MessageBox.Show($"Error: {error.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 0;
                });
        }, "Enable device");
    }

    private void DisableButton_Click(object? sender, EventArgs e)
    {
        SafeExecute(() =>
        {
            if (_deviceList.SelectedItem is not DeviceInfo device)
                return;

            if (MessageBox.Show($"Disable device?\n\n{device.Id}", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            Log($"Disabling {device.Id}...");
            var result = _deviceManager.Disable(device.Id);
            
            result.Match(
                _ =>
                {
                    Log("Device disabled successfully");
                    MessageBox.Show("Device disabled successfully", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                },
                error =>
                {
                    Log($"Error: {error.Message}");
                    MessageBox.Show($"Error: {error.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 0;
                });
        }, "Disable device");
    }

    private void RestartButton_Click(object? sender, EventArgs e)
    {
        SafeExecute(() =>
        {
            if (_deviceList.SelectedItem is not DeviceInfo device)
                return;

            if (MessageBox.Show($"Restart device?\n\n{device.Id}", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            Log($"Restarting {device.Id}...");
            var result = _deviceManager.Restart(device.Id);
            
            result.Match(
                _ =>
                {
                    Log("Device restarted successfully");
                    MessageBox.Show("Device restarted successfully", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                },
                error =>
                {
                    Log($"Error: {error.Message}");
                    MessageBox.Show($"Error: {error.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 0;
                });
        }, "Restart device");
    }

    private void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        _logOutput.AppendText($"[{timestamp}] {message}\r\n");
        _logger.Info(message);
    }

    private void SafeExecute(Action action, string operationName)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            var errorMsg = $"{operationName} failed: {ex.Message}";
            Log(errorMsg);
            _logger.Error(errorMsg, ex);
            MessageBox.Show(errorMsg, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadVDDCount()
    {
        SafeExecute(() =>
        {
            var configResult = _vddController.GetCurrentCount();
            var effectiveResult = _vddController.GetEffectiveCount();
            
            configResult.Match(
                configCount =>
                {
                    _vddCountLabel.Text = $"Config count: {configCount}";
                    if (configCount == 0)
                    {
                        _vddCountInput.Value = 1;
                        Log("Config has invalid count=0, input reset to 1");
                    }
                    else
                    {
                        _vddCountInput.Value = configCount;
                    }
                    return 0;
                },
                error =>
                {
                    _vddCountLabel.Text = "Config count: (error)";
                    Log($"Failed to load VDD config count: {error.Message}");
                    return 0;
                });

            effectiveResult.Match(
                effectiveCount =>
                {
                    _vddEffectiveLabel.Text = $"Effective count: {effectiveCount}";
                    return 0;
                },
                error =>
                {
                    _vddEffectiveLabel.Text = "Effective count: (error)";
                    Log($"Failed to load VDD effective count: {error.Message}");
                    return 0;
                });
        }, "Load VDD count");
    }

    private void VddSetCountButton_Click(object? sender, EventArgs e)
    {
        SafeExecute(() =>
        {
            var count = (uint)_vddCountInput.Value;
            
            if (count == 0)
            {
                MessageBox.Show("Count cannot be 0.\n\nThe VDD driver ignores count=0 and creates 1 display anyway.\n\nTo disable virtual displays, use the device Disable button instead.", 
                    "Invalid Count", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            if (MessageBox.Show($"Set virtual display count to {count}?\n\nThis will modify the VDD configuration and may restart the device.", 
                "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            Log($"Setting VDD count to {count}...");
            var result = _vddController.SetVirtualDisplayCount(count);
            
            result.Match(
                _ =>
                {
                    Log($"VDD count set to {count} successfully");
                    LoadVDDCount();
                    MessageBox.Show($"Virtual display count set to {count}", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                },
                error =>
                {
                    Log($"Error: {error.Message}");
                    MessageBox.Show($"Error: {error.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 0;
                });
        }, "Set VDD count");
    }

    private void VddAddOneButton_Click(object? sender, EventArgs e)
    {
        SafeExecute(() =>
        {
            var result = _vddController.GetCurrentCount();
            result.Match(
                currentCount =>
                {
                    var newCount = currentCount + 1;
                    if (newCount > 10)
                    {
                        MessageBox.Show("Cannot exceed maximum of 10 virtual displays", "Limit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return 0;
                    }

                    Log($"Adding 1 virtual display (current: {currentCount}, new: {newCount})...");
                    var setResult = _vddController.SetVirtualDisplayCount(newCount);
                    
                    setResult.Match(
                        _ =>
                        {
                            Log($"VDD count increased to {newCount}");
                            LoadVDDCount();
                            return 0;
                        },
                        error =>
                        {
                            Log($"Error: {error.Message}");
                            MessageBox.Show($"Error: {error.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return 0;
                        });
                    return 0;
                },
                error =>
                {
                    Log($"Failed to read current count: {error.Message}");
                    MessageBox.Show($"Error: {error.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 0;
                });
        }, "Add 1 VD");
    }

    private void VddRemoveOneButton_Click(object? sender, EventArgs e)
    {
        SafeExecute(() =>
        {
            var result = _vddController.GetCurrentCount();
            result.Match(
                currentCount =>
                {
                    if (currentCount == 0)
                    {
                        MessageBox.Show("Already at 0 virtual displays", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return 0;
                    }

                    var newCount = currentCount - 1;
                    Log($"Removing 1 virtual display (current: {currentCount}, new: {newCount})...");
                    var setResult = _vddController.SetVirtualDisplayCount(newCount);
                    
                    setResult.Match(
                        _ =>
                        {
                            Log($"VDD count decreased to {newCount}");
                            LoadVDDCount();
                            return 0;
                        },
                        error =>
                        {
                            Log($"Error: {error.Message}");
                            MessageBox.Show($"Error: {error.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return 0;
                        });
                    return 0;
                },
                error =>
                {
                    Log($"Failed to read current count: {error.Message}");
                    MessageBox.Show($"Error: {error.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 0;
                });
        }, "Remove 1 VD");
    }

    private void VddGetEffectiveButton_Click(object? sender, EventArgs e)
    {
        SafeExecute(() =>
        {
            Log("Querying effective VDD count...");
            var result = _vddController.GetEffectiveCount();
            
            result.Match(
                effectiveCount =>
                {
                    _vddEffectiveLabel.Text = $"Effective count: {effectiveCount}";
                    Log($"Effective VDD count: {effectiveCount}");
                    MessageBox.Show($"Effective virtual display count: {effectiveCount}\n\n" +
                                  $"(This is the actual number of active virtual displays based on device state)",
                        "Effective Count", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                },
                error =>
                {
                    Log($"Error: {error.Message}");
                    MessageBox.Show($"Error: {error.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 0;
                });
        }, "Get effective VDD count");
    }

    private void UpdateSenderStatus()
    {
        var count = _senderHandles.Count;
        _gstSenderStatusLabel.Text = $"Active streams: {count}";
        _gstSenderStatusLabel.ForeColor = count > 0 ? Color.Green : Color.Gray;
        _gstSenderCountLabel.Text = count > 0 ? $"({count} sender{(count != 1 ? "s" : "")} running)" : "";
        _gstStopSenderButton.Enabled = count > 0;
    }

    private void UpdateReceiverStatus()
    {
        var count = _receiverHandles.Count;
        _gstReceiverStatusLabel.Text = $"Active streams: {count}";
        _gstReceiverStatusLabel.ForeColor = count > 0 ? Color.Green : Color.Gray;
        _gstReceiverCountLabel.Text = count > 0 ? $"({count} receiver{(count != 1 ? "s" : "")} running)" : "";
        _gstStopReceiverButton.Enabled = count > 0;
    }

    private void GstStartSenderButton_Click(object? sender, EventArgs e)
    {
        SafeExecute(() =>
        {
            var host = _gstSenderHost.Text.Trim();
            if (string.IsNullOrEmpty(host))
            {
                MessageBox.Show("Host cannot be empty", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var port = (ushort)_gstSenderPort.Value;
            var monitorIndex = (uint)_gstSenderMonitor.Value;

            var config = new SenderConfig(host, port, monitorIndex);
            Log($"Starting sender (host={host}, port={port}, monitor={monitorIndex})...");

            var result = _gstManager.StartSender(config);
            result.Match(
                handle =>
                {
                    lock (_streamLock)
                    {
                        handle.Exited += OnSenderExited;
                        handle.StderrDataReceived += OnSenderStderr;
                        _senderHandles.Add(handle);
                        UpdateSenderStatus();
                    }
                    Log($"Sender started successfully (total: {_senderHandles.Count})");
                    return 0;
                },
                error =>
                {
                    Log($"Failed to start sender: {error.Message}");
                    MessageBox.Show($"Error: {error.Message}", "Start Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 0;
                });
        }, "Start GStreamer sender");
    }

    private void GstStopSenderButton_Click(object? sender, EventArgs e)
    {
        SafeExecute(() =>
        {
            lock (_streamLock)
            {
                if (_senderHandles.Count == 0)
                    return;

                Log($"Stopping {_senderHandles.Count} sender stream(s)...");

                foreach (var handle in _senderHandles.ToList())
                {
                    try
                    {
                        handle.Exited -= OnSenderExited;
                        handle.StderrDataReceived -= OnSenderStderr;
                        handle.Stop();
                        var exitCode = handle.ExitCode;
                        var lastLines = handle.GetLastStderrLines();

                        handle.Dispose();

                        Log($"Sender stopped (exit code: {exitCode?.ToString() ?? "null"}, captured {lastLines.Count} log lines)");
                        if (lastLines.Count > 0)
                        {
                            _gstLogOutput.AppendText($"=== Sender Stopped (Last {lastLines.Count} Logs) ===\r\n");
                            foreach (var line in lastLines)
                                _gstLogOutput.AppendText(line + "\r\n");
                            _gstLogOutput.SelectionStart = _gstLogOutput.Text.Length;
                            _gstLogOutput.ScrollToCaret();
                        }
                        else
                        {
                            _gstLogOutput.AppendText("=== Sender Stopped (No Logs Captured) ===\r\n");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Error stopping sender: {ex.Message}");
                    }
                }

                _senderHandles.Clear();
                UpdateSenderStatus();
            }
        }, "Stop GStreamer senders");
    }

    private void GstStartReceiverButton_Click(object? sender, EventArgs e)
    {
        SafeExecute(() =>
        {
            var port = (ushort)_gstReceiverPort.Value;
            var fullscreen = _gstReceiverFullscreen.Checked;

            var config = new ReceiverConfig(port, fullscreen);
            Log($"Starting receiver (port={port}, fullscreen={fullscreen})...");

            var result = _gstManager.StartReceiver(config);
            result.Match(
                handle =>
                {
                    lock (_streamLock)
                    {
                        handle.Exited += OnReceiverExited;
                        handle.StderrDataReceived += OnReceiverStderr;
                        _receiverHandles.Add(handle);
                        UpdateReceiverStatus();
                    }
                    Log($"Receiver started successfully (total: {_receiverHandles.Count})");
                    return 0;
                },
                error =>
                {
                    Log($"Failed to start receiver: {error.Message}");
                    MessageBox.Show($"Error: {error.Message}", "Start Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 0;
                });
        }, "Start GStreamer receiver");
    }

    private void GstStopReceiverButton_Click(object? sender, EventArgs e)
    {
        SafeExecute(() =>
        {
            lock (_streamLock)
            {
                if (_receiverHandles.Count == 0)
                    return;

                Log($"Stopping {_receiverHandles.Count} receiver stream(s)...");

                foreach (var handle in _receiverHandles.ToList())
                {
                    try
                    {
                        handle.Exited -= OnReceiverExited;
                        handle.StderrDataReceived -= OnReceiverStderr;
                        handle.Stop();
                        var exitCode = handle.ExitCode;
                        var lastLines = handle.GetLastStderrLines();

                        handle.Dispose();

                        Log($"Receiver stopped (exit code: {exitCode?.ToString() ?? "null"}, captured {lastLines.Count} log lines)");
                        if (lastLines.Count > 0)
                        {
                            _gstLogOutput.AppendText($"=== Receiver Stopped (Last {lastLines.Count} Logs) ===\r\n");
                            foreach (var line in lastLines)
                                _gstLogOutput.AppendText(line + "\r\n");
                            _gstLogOutput.SelectionStart = _gstLogOutput.Text.Length;
                            _gstLogOutput.ScrollToCaret();
                        }
                        else
                        {
                            _gstLogOutput.AppendText("=== Receiver Stopped (No Logs Captured) ===\r\n");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Error stopping receiver: {ex.Message}");
                    }
                }

                _receiverHandles.Clear();
                UpdateReceiverStatus();
            }
        }, "Stop GStreamer receivers");
    }

    private void OnSenderExited(object? sender, EventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnSenderExited(sender, e));
            return;
        }

        var handle = sender as StreamHandle;
        if (handle == null)
            return;

        lock (_streamLock)
        {
            if (!_senderHandles.Remove(handle))
                return;

            var exitCode = handle.ExitCode;
            var lastLines = handle.GetLastStderrLines();

            handle.Exited -= OnSenderExited;
            handle.StderrDataReceived -= OnSenderStderr;
            handle.Dispose();

            UpdateSenderStatus();

            Log($"Sender exited unexpectedly (exit code: {exitCode?.ToString() ?? "null"}, {_senderHandles.Count} remaining)");
            if (lastLines.Count > 0)
            {
                _gstLogOutput.AppendText("=== Sender Exited (Last Logs) ===\r\n");
                foreach (var line in lastLines)
                    _gstLogOutput.AppendText(line + "\r\n");
            }
        }
    }

    private void OnReceiverExited(object? sender, EventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnReceiverExited(sender, e));
            return;
        }

        var handle = sender as StreamHandle;
        if (handle == null)
            return;

        lock (_streamLock)
        {
            if (!_receiverHandles.Remove(handle))
                return;

            var exitCode = handle.ExitCode;
            var lastLines = handle.GetLastStderrLines();

            handle.Exited -= OnReceiverExited;
            handle.StderrDataReceived -= OnReceiverStderr;
            handle.Dispose();

            UpdateReceiverStatus();

            Log($"Receiver exited unexpectedly (exit code: {exitCode?.ToString() ?? "null"}, {_receiverHandles.Count} remaining)");
            if (lastLines.Count > 0)
            {
                _gstLogOutput.AppendText("=== Receiver Exited (Last Logs) ===\r\n");
                foreach (var line in lastLines)
                    _gstLogOutput.AppendText(line + "\r\n");
            }
        }
    }

    private void OnSenderStderr(object? sender, string data)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnSenderStderr(sender, data));
            return;
        }

        _gstLogOutput.AppendText($"[SENDER] {data}\r\n");
        _gstLogOutput.SelectionStart = _gstLogOutput.Text.Length;
        _gstLogOutput.ScrollToCaret();
    }

    private void OnReceiverStderr(object? sender, string data)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnReceiverStderr(sender, data));
            return;
        }

        _gstLogOutput.AppendText($"[RECEIVER] {data}\r\n");
        _gstLogOutput.SelectionStart = _gstLogOutput.Text.Length;
        _gstLogOutput.ScrollToCaret();
    }

    private void AddStreamButton_Click(object? sender, EventArgs e)
    {
        SafeExecute(() =>
        {
            // Atomic check-and-set to prevent double-clicks
            if (Interlocked.CompareExchange(ref _addStreamInProgress, 1, 0) != 0)
                return;
            
            _addStreamButton.Enabled = false;
            Task.Run(async () =>
            {
                try
                {
                    await _orchestrator.AddStreamAsync();
                }
                finally
                {
                    Interlocked.Exchange(ref _addStreamInProgress, 0);
                    if (InvokeRequired)
                        BeginInvoke(() =>
                        {
                            _addStreamButton.Enabled = true;
                            UpdateOrchestratorUI();
                        });
                    else
                    {
                        _addStreamButton.Enabled = true;
                        UpdateOrchestratorUI();
                    }
                }
            });
        }, "Add stream via orchestrator");
    }

    private void StreamListBox_DoubleClick(object? sender, EventArgs e)
    {
        SafeExecute(() =>
        {
            if (_streamListBox.SelectedItem is string selected)
            {
                var parts = selected.Split('|');
                if (parts.Length > 0 && ushort.TryParse(parts[0].Replace("Port:", "").Trim(), out var port))
                {
                    if (MessageBox.Show($"Remove stream on port {port}?", "Confirm", 
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    {
                        Task.Run(async () =>
                        {
                            await _orchestrator.RemoveStreamAsync(port);
                            if (InvokeRequired)
                                BeginInvoke(UpdateOrchestratorUI);
                            else
                                UpdateOrchestratorUI();
                        });
                    }
                }
            }
        }, "Remove stream");
    }

    private void UpdateOrchestratorUI()
    {
        var streams = _orchestrator.Streams;
        _streamCountLabel.Text = $"Active streams: {streams.Count}";
        
        var queueCount = _orchestrator.QueuedOperations;
        _queueStatusLabel.Text = queueCount > 0 
            ? $"Queue: {queueCount} operation(s) in progress..." 
            : "Queue: idle";
        _queueStatusLabel.ForeColor = queueCount > 0 ? Color.Orange : Color.DarkGray;

        _streamListBox.Items.Clear();
        foreach (var stream in streams)
        {
            _streamListBox.Items.Add($"Port:{stream.Port} | VD:{stream.VdIndex} | Monitor:{stream.MonitorIndex} | State:{stream.State}");
        }
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        _orchestrator?.Dispose();
        
        lock (_streamLock)
        {
            foreach (var handle in _senderHandles.ToList())
            {
                try
                {
                    handle.Exited -= OnSenderExited;
                    handle.StderrDataReceived -= OnSenderStderr;
                    handle.Dispose();
                }
                catch
                {
                }
            }
            _senderHandles.Clear();

            foreach (var handle in _receiverHandles.ToList())
            {
                try
                {
                    handle.Exited -= OnReceiverExited;
                    handle.StderrDataReceived -= OnReceiverStderr;
                    handle.Dispose();
                }
                catch
                {
                }
            }
            _receiverHandles.Clear();
        }

        try
        {
            Log("Cleaning up VDD on application close...");
            var result = _vddController.SetVirtualDisplayCount(0);
            if (result.IsSuccess)
                Log("VDD count set to 0");
            else
                Log($"Failed to cleanup VDD: {result.Error.Message}");
        }
        catch (Exception ex)
        {
            Log($"Exception during VDD cleanup: {ex.Message}");
        }
    }
}
