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
    private static readonly string AgentPortPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WebBlocker",
        "agent.port");

    private static readonly string ManagementStatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WebBlocker",
        "management.state");

    private readonly EngineClient _client;
    private readonly ProxyHostService _proxy;
    private readonly AgentLanService _lan;
    private readonly SystemProxyManager _systemProxy = new SystemProxyManager();
    private readonly AccessRequestServer _accessRequests;
    private readonly DispatcherQueueTimer _connectionTimer;
    private readonly DispatcherQueueTimer _uptimeTimer;
    private readonly DispatcherQueueTimer _managementTimer;
    private readonly DispatcherQueueTimer _policyTimer;
    private DateTime _connectedAtUtc;
    private bool _connected;
    private bool _everConnected;
    private bool _managementEnabled;
    private long _shownPolicyVersion;

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
        _lan = new AgentLanService(_client, databasePath);
        _accessRequests = new AccessRequestServer(Path.GetDirectoryName(databasePath));
        try
        {
            _accessRequests.Start();
        }
        catch (System.Net.Sockets.SocketException)
        {
            // Port 18765 is occupied; the rest of the agent still works.
        }
        AppDomain.CurrentDomain.ProcessExit += (_, __) => _systemProxy.Dispose();
        AppDomain.CurrentDomain.UnhandledException += (_, __) => _systemProxy.Dispose();
        _proxy = new ProxyHostService(databasePath);
        _systemProxy.RecoverIfNeeded();

        DeviceNameText.Text = Environment.MachineName;
        DeviceIpText.Text = GetLocalIPv4();
        DeviceIdText.Text = _lan.DeviceId;
        ConnectionCodeText.Text = _lan.GetPairingCode();
        LanPortText.Text = _lan.Port + "（TCP，需允许本地防火墙入站）";
        try
        {
            _lan.Start();
            LanPortText.Text = _lan.Port + "（TCP，需允许本地防火墙入站）";
            LanStateText.Text = "局域网监听已启动：等待管理端配对或连接。";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LanStateText.Text = "局域网监听未能启动：" + exception.Message +
                "。端口 18770 可能被占用，关闭占用程序后重启被控端。";
        }

        _connectionTimer = DispatcherQueue.CreateTimer();
        _connectionTimer.Interval = TimeSpan.FromSeconds(2);
        _connectionTimer.IsRepeating = true;
        _connectionTimer.Tick += (_, _) => CheckHeartbeat();
        _connectionTimer.Start();

        _uptimeTimer = DispatcherQueue.CreateTimer();
        _uptimeTimer.Interval = TimeSpan.FromSeconds(1);
        _uptimeTimer.IsRepeating = true;
        _uptimeTimer.Tick += (_, _) => UpdateUptimeText();
        _uptimeTimer.Start();

        _policyTimer = DispatcherQueue.CreateTimer();
        _policyTimer.Interval = TimeSpan.FromSeconds(1);
        _policyTimer.IsRepeating = true;
        _policyTimer.Tick += async (_, _) =>
        {
            var version = _proxy.PolicyVersion;
            if (version > 0 && version != _shownPolicyVersion)
            {
                await LoadPolicyAsync();
            }
        };
        _policyTimer.Start();

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
            _policyTimer.Stop();
            _lan.Dispose();
            _systemProxy.Dispose();
            _accessRequests.Dispose();
            _proxy.Dispose();
            TryDeletePortFile();
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

            _shownPolicyVersion = policy.Version;
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
                var port = await _proxy.StartAsync();
                File.WriteAllText(AgentPortPath, port.ToString());
                _systemProxy.Enable(port);
            }
            catch (Exception exception)
            {
                PolicyText.Text = "本地代理未能启动：" + exception.Message;
            }
        }
        else
        {
            _proxy.Stop();
            _systemProxy.Disable();
            TryDeletePortFile();
        }

        UpdateManagementText();
    }

    private static void TryDeletePortFile()
    {
        try
        {
            if (File.Exists(AgentPortPath)) File.Delete(AgentPortPath);
        }
        catch (IOException)
        {
        }
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

    /// <summary>
    /// Connection state is derived from real authenticated LAN sessions, not from a local file: a device is
    /// "connected" while an administrator has talked to it recently. The pairing code stays visible so a
    /// second administrator can still be enrolled.
    /// </summary>
    private void CheckHeartbeat()
    {
        ConnectionCodeText.Text = _lan.GetPairingCode();

        var last = _lan.LastSessionUtc;
        var paired = _lan.PairedAdminCount;
        var recent = last != DateTime.MinValue && (DateTime.UtcNow - last).TotalSeconds < 20;

        LanStateText.Text = _lan.IsRunning
            ? "局域网监听已启动（端口 " + _lan.Port + "）。设备指纹：" + _lan.Fingerprint
            : "局域网监听未启动。";
        SessionText.Text = _lan.LastSessionDetail +
            (last == DateTime.MinValue ? string.Empty : "（" + last.ToLocalTime().ToString("HH:mm:ss") + "）");

        if (recent)
        {
            _connected = true;
            _everConnected = true;
            _connectedAtUtc = last;
            ConnectionStateText.Text = "已连接";
            StatusBar.Severity = InfoBarSeverity.Success;
            StatusBar.Title = "已连接管理端";
            StatusBar.Message = (paired > 0 ? "已配对 " + paired + " 个管理端。" : string.Empty) +
                "策略由管理端下发并热加载。";
            FallbackBar.IsOpen = false;
            UpdateUptimeText();
            return;
        }

        _connected = false;
        ConnectionStateText.Text = paired > 0 ? "等待管理端连接" : "未配对";
        if (paired > 0)
        {
            StatusBar.Severity = _everConnected ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
            StatusBar.Title = _everConnected ? "管理端连接已断开" : "等待管理端连接";
            StatusBar.Message = "已配对 " + paired + " 个管理端；继续按最后策略拦截。";
            FallbackBar.IsOpen = _everConnected;
        }
        else
        {
            StatusBar.Severity = InfoBarSeverity.Informational;
            StatusBar.Title = "等待配对";
            StatusBar.Message = "在管理端输入本机 IP、端口与配对码完成配对。";
            FallbackBar.IsOpen = false;
        }

        UpdateUptimeText();
    }

    private void UpdateUptimeText()
    {
        if (_connected)
        {
            UptimeText.Text = FormatDuration(DateTime.UtcNow - _connectedAtUtc) + "（距最近通信）";
            return;
        }

        var last = _lan.LastSessionUtc;
        UptimeText.Text = last == DateTime.MinValue
            ? "--:--:--"
            : FormatDuration(DateTime.UtcNow - last) + "（距上次通信）";
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
            var candidates = NetworkInterface.GetAllNetworkInterfaces()
                .Where(network => network.OperationalStatus == OperationalStatus.Up &&
                                  network.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                  network.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .SelectMany(network => network.GetIPProperties().UnicastAddresses)
                .Select(address => address.Address)
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
                .Select(address => address.ToString())
                .ToList();

            // Prefer a private LAN address; VPN/TUN adapters often expose a public or unusual address that
            // an administrator could not reach from the management computer.
            return candidates.FirstOrDefault(IsPrivateIPv4)
                ?? candidates.FirstOrDefault()
                ?? "未检测到 IPv4";
        }
        catch (NetworkInformationException)
        {
            return "无法读取网络接口";
        }
    }

    private static bool IsPrivateIPv4(string value)
    {
        System.Net.IPAddress address;
        if (!System.Net.IPAddress.TryParse(value, out address))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
        {
            return false;
        }

        return bytes[0] == 10 ||
               (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
               (bytes[0] == 192 && bytes[1] == 168);
    }
}