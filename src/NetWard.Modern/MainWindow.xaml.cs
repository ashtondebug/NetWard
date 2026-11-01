using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Windows.Graphics;

namespace NetWard.Modern;

public sealed partial class MainWindow : Window
{
    private static readonly string HeartbeatPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WebBlocker",
        "admin.heartbeat");

    private static readonly string ManagementStatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WebBlocker",
        "management.state");

    private readonly EngineClient _client;
    private readonly ProxyHostService _proxy;
    private readonly DispatcherQueueTimer _connectionTimer;
    private readonly DispatcherQueueTimer _uptimeTimer;
    private readonly DispatcherQueueTimer _managementTimer;
    private DateTime _connectedAtUtc;
    private TimeSpan _lastDuration;
    private bool _connected;
    private bool _everConnected;
    private bool _managementEnabled;

    public MainWindow()
    {
        InitializeComponent();
        Title = "NetWard 网界卫士 · Modern 被控端";
        AppWindow.Resize(new SizeInt32(1300, 900));
        AppWindow.Move(new PointInt32(80, 20));

        var databasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WebBlocker",
            "proxy.db");
        _client = new EngineClient(databasePath);
        _proxy = new ProxyHostService(databasePath);

        DeviceNameText.Text = Environment.MachineName;
        DeviceIpText.Text = GetLocalIPv4();
        ConnectionCodeText.Text = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

        _connectionTimer = DispatcherQueue.CreateTimer();
        _connectionTimer.Interval = TimeSpan.FromSeconds(2);
        _connectionTimer.IsRepeating = true;
        _connectionTimer.Tick += (_, _) => CheckHeartbeat();
        _connectionTimer.Start();

        _uptimeTimer = DispatcherQueue.CreateTimer();
        _uptimeTimer.Interval = TimeSpan.FromSeconds(1);
        _uptimeTimer.IsRepeating = true;
        _uptimeTimer.Tick += (_, _) => UpdateUptime();
        _uptimeTimer.Start();

        _managementTimer = DispatcherQueue.CreateTimer();
        _managementTimer.Interval = TimeSpan.FromSeconds(2);
        _managementTimer.IsRepeating = true;
        _managementTimer.Tick += async (_, _) => await ApplyManagementStateAsync();
        _managementTimer.Start();

        RootGrid.Loaded += async (_, _) =>
        {
            CheckHeartbeat();
            await ApplyManagementStateAsync();
            await LoadPolicyAsync();
        };
        Closed += (_, _) =>
        {
            _connectionTimer.Stop();
            _uptimeTimer.Stop();
            _managementTimer.Stop();
            _proxy.Dispose();
        };
    }

    private async Task LoadPolicyAsync()
    {
        try
        {
            var policy = await _client.GetPolicyAsync();
            if (!policy.HasPolicy)
            {
                PolicyText.Text = "本机尚无已保存策略；请等待管理端保存并启用管理。";
                return;
            }

            PolicyText.Text = "本机策略：版本 " + policy.Version +
                " · " + (policy.Mode == "AllowList" ? "白名单" : "黑名单") +
                " · " + policy.Rules + " 条规则";
        }
        catch (Exception exception)
        {
            PolicyText.Text = "读取策略失败：" + exception.Message;
        }
    }

    private async Task ApplyManagementStateAsync()
    {
        var enabled = false;
        try
        {
            if (File.Exists(ManagementStatePath))
            {
                enabled = string.Equals(
                    File.ReadAllText(ManagementStatePath).Trim(),
                    "enabled",
                    StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (IOException)
        {
        }

        if (enabled == _managementEnabled && (enabled == _proxy.IsRunning || !enabled))
        {
            UpdateManagementText();
            return;
        }

        _managementEnabled = enabled;
        if (enabled)
        {
            try
            {
                await _proxy.StartAsync();
            }
            catch (Exception exception)
            {
                PolicyText.Text = "本地代理未能启动：" + exception.Message;
            }
        }
        else
        {
            _proxy.Stop();
        }

        UpdateManagementText();
    }

    private void UpdateManagementText()
    {
        ManagementStateText.Text = _managementEnabled
            ? "已启用（由管理端控制）"
            : "已暂停（由管理端控制）";
        if (!_managementEnabled)
        {
            StatusBar.Severity = InfoBarSeverity.Informational;
            StatusBar.Title = "管理已暂停";
            StatusBar.Message = "被控端不允许自行启用管理，请在管理端启用。";
        }
    }

    private void CheckHeartbeat()
    {
        var fresh = false;
        try
        {
            if (File.Exists(HeartbeatPath))
            {
                fresh = (DateTime.UtcNow - File.GetLastWriteTimeUtc(HeartbeatPath)).TotalSeconds < 8;
            }
        }
        catch (IOException)
        {
        }

        if (fresh)
        {
            if (!_connected)
            {
                _connected = true;
                _everConnected = true;
                _connectedAtUtc = DateTime.UtcNow;
                CodeCard.Visibility = Visibility.Collapsed;
            }

            ConnectionStateText.Text = "已连接";
            StatusBar.Severity = InfoBarSeverity.Success;
            StatusBar.Title = "已连接管理端";
            StatusBar.Message = "受管浏览与本地拦截策略由管理端控制。";
            FallbackBar.IsOpen = false;
            UpdateUptime();
            return;
        }

        if (_connected)
        {
            _connected = false;
            _lastDuration = DateTime.UtcNow - _connectedAtUtc;
        }

        if (!_managementEnabled)
        {
            ConnectionStateText.Text = _everConnected ? "连接已断开" : "未连接";
            StatusBar.Severity = InfoBarSeverity.Informational;
            StatusBar.Title = "管理已暂停";
            StatusBar.Message = "请在管理端启用管理。";
            FallbackBar.IsOpen = false;
            CodeCard.Visibility = Visibility.Visible;
            UpdateUptime();
            return;
        }

        ConnectionStateText.Text = _everConnected ? "连接已断开" : "未连接";
        StatusBar.Severity = _everConnected ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
        StatusBar.Title = _everConnected ? "连接已断开" : "未连接";
        StatusBar.Message = "本地拦截策略保持运行；显示连接码等待重新连接。";
        FallbackBar.IsOpen = _everConnected;
        CodeCard.Visibility = Visibility.Visible;
        UpdateUptime();
    }

    private void UpdateUptime()
    {
        if (_connected)
        {
            UptimeText.Text = FormatDuration(DateTime.UtcNow - _connectedAtUtc) + "（本次连接）";
        }
        else if (_lastDuration > TimeSpan.Zero)
        {
            UptimeText.Text = FormatDuration(_lastDuration) + "（上次连接）";
        }
        else
        {
            UptimeText.Text = "--:--:--";
        }
    }

    private static string FormatDuration(TimeSpan value)
    {
        return string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0:00}:{1:00}:{2:00}",
            (int)value.TotalHours,
            value.Minutes,
            value.Seconds);
    }

    private static string GetLocalIPv4()
    {
        try
        {
            var candidate = NetworkInterface.GetAllNetworkInterfaces()
                .Where(network => network.OperationalStatus == OperationalStatus.Up &&
                                  network.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                  network.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .SelectMany(network => network.GetIPProperties().UnicastAddresses)
                .Select(address => address.Address)
                .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork);
            return candidate == null ? "未检测到 IPv4" : candidate.ToString();
        }
        catch (NetworkInformationException)
        {
            return "无法读取网络接口";
        }
    }
}