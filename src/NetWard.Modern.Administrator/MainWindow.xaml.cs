using System.Text.Json;
using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace NetWard.Modern.Administrator;

public sealed partial class MainWindow : Window
{
    private readonly AdminEngineClient _client;
    private readonly DeviceLinkService _deviceLink;
    private readonly Dictionary<string, FrameworkElement> _pages;
    private AdminPolicy _policy = new();
    private bool _dirty;
    private bool _managementSwitchReady;
    private readonly DispatcherQueueTimer _infoBarTimer;
    private readonly DispatcherQueueTimer _heartbeatTimer;
    private static readonly string ManagementStatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WebBlocker",
        "management.state");
    private static readonly string HeartbeatPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WebBlocker",
        "admin.heartbeat");
    private static readonly string ThemeStatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WebBlocker",
        "ui.theme");

    public MainWindow()
    {
        InitializeComponent();
        Title = "NetWard 网界卫士 · Modern 管理端";

        ApplySavedTheme();

        var databasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WebBlocker",
            "proxy.db");
        _client = new AdminEngineClient(databasePath);
        _deviceLink = new DeviceLinkService(_client);

        _pages = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal)
        {
            ["overview"] = OverviewPage,
            ["rules"] = RulesPage,
            ["timed"] = TimedPage,
            ["categories"] = CategoriesPage,
            ["requests"] = RequestsPage,
            ["devices"] = DevicesPage,
            ["logs"] = LogsPage,
            ["history"] = HistoryPage
        };

        _infoBarTimer = DispatcherQueue.CreateTimer();
        _infoBarTimer.Interval = TimeSpan.FromSeconds(5);
        _infoBarTimer.IsRepeating = false;
        _infoBarTimer.Tick += (_, _) =>
        {
            StatusBar.IsOpen = false;
            _infoBarTimer.Stop();
        };

        _heartbeatTimer = DispatcherQueue.CreateTimer();
        _heartbeatTimer.Interval = TimeSpan.FromSeconds(2);
        _heartbeatTimer.IsRepeating = true;
        _heartbeatTimer.Tick += (_, _) => WriteHeartbeat();
        _heartbeatTimer.Start();
        WriteHeartbeat();
        Closed += (_, _) =>
        {
            _heartbeatTimer.Stop();
            _deviceLink.Dispose();
            try
            {
                File.Delete(HeartbeatPath);
            }
            catch (Exception)
            {
            }
        };

        RootGrid.Loaded += async (_, _) =>
        {
            AppWindow.Resize(new SizeInt32(1400, 900));
            AppWindow.Move(new PointInt32(50, 20));
            await LoadAsync();
        };
    }

    private async Task LoadAsync()
    {
        try
        {
            _policy = await _client.GetPolicyAsync();
            _dirty = false;
            LoadManagementState();
            UpdateStats();
            RefreshRuleList();
            RefreshTimedLists();
            RefreshCategoryList();
            RefreshCategoryCombo();
            await RefreshLogsAsync();
            await RefreshHistoryAsync();
            SetInfoBar(InfoBarSeverity.Informational, "就绪。编辑规则后请点击「保存策略」。");
        }
        catch (Exception exception)
        {
            SetInfoBar(InfoBarSeverity.Error, "读取策略失败：" + exception.Message);
        }
    }

    private void UpdateStats()
    {
        VersionText.Text = _policy.HasPolicy ? _policy.Version.ToString() : "-";
        ModeText.Text = _policy.HasPolicy
            ? (_policy.Mode == "AllowList" ? "白名单模式" : "黑名单模式")
            : "尚未保存策略";
        RuleCountText.Text = _policy.Rules.Count.ToString();
        CategoryCountText.Text = _policy.Categories.Count.ToString();
        CategoryHintText.Text = _policy.HasPolicy
            ? "分类规则 " + _policy.CategoryRules.Count + " 条"
            : "尚未导入分类";
        EventCountText.Text = _policy.Events.ToString();
        VersionFooter.Text = "策略版本 " + (_policy.HasPolicy ? _policy.Version.ToString() : "-");
        SaveButton.IsEnabled = _dirty || !_policy.HasPolicy;
        ModeCombo.SelectedIndex = _policy.Mode == "BlockList" ? 1 : 0;
    }

    private void RefreshRuleList()
    {
        RulesList.ItemsSource = null;
        RulesList.ItemsSource = _policy.Rules;
        RulesHint.Text = _policy.Rules.Count == 0
            ? "尚无规则；白名单为空会拒绝全部域名。"
            : "共 " + _policy.Rules.Count + " 条规则。";
    }

    private void RefreshTimedLists()
    {
        TimedCategoriesList.ItemsSource = null;
        TimedCategoriesList.ItemsSource = _policy.TimedCategories;
        TimedRulesList.ItemsSource = null;
        TimedRulesList.ItemsSource = _policy.TimedRules;
        TimedCategoryCombo.ItemsSource = _policy.TimedCategories.Select(category => category.Name).ToList();
    }

    private void RefreshCategoryCombo()
    {
        TimedCategoryCombo.ItemsSource = _policy.TimedCategories.Select(category => category.Name).ToList();
    }

    private void RefreshCategoryList()
    {
        CategoriesList.ItemsSource = null;
        CategoriesList.ItemsSource = _policy.Categories;
        CategoryHint.Text = _policy.Categories.Count == 0
            ? "尚未导入分类名单。"
            : "已导入 " + _policy.Categories.Count + " 个类别 / " + _policy.CategoryRules.Count + " 条分类规则。保存后生效。";
    }

    private async Task RefreshLogsAsync()
    {
        try
        {
            var rows = await _client.GetEventsAsync(BlockedOnlyCheck.IsChecked == true, LogSearch.Text);
            LogsList.ItemsSource = null;
            LogsList.ItemsSource = rows;
            RecentList.ItemsSource = null;
            RecentList.ItemsSource = rows.Take(10).ToList();
        }
        catch (Exception exception)
        {
            SetInfoBar(InfoBarSeverity.Error, "读取日志失败：" + exception.Message);
        }
    }

    private async Task RefreshHistoryAsync()
    {
        try
        {
            HistoryList.ItemsSource = null;
            HistoryList.ItemsSource = await _client.GetHistoryAsync();
        }
        catch (Exception exception)
        {
            SetInfoBar(InfoBarSeverity.Error, "读取历史失败：" + exception.Message);
        }
    }

    private void MarkDirty()
    {
        _dirty = true;
        SaveButton.IsEnabled = true;
        SetInfoBar(InfoBarSeverity.Warning, "有未保存的更改。点击「保存策略」写入数据库。");
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveButton.IsEnabled = false;
            var version = await _client.SavePolicyAsync(_policy);
            SetInfoBar(InfoBarSeverity.Success, "策略已保存为版本 " + version + "。");
            await LoadAsync();
            SetInfoBar(InfoBarSeverity.Success, "策略已保存为版本 " + version + "。");
        }
        catch (Exception exception)
        {
            SaveButton.IsEnabled = true;
            SetInfoBar(InfoBarSeverity.Error, "保存失败：" + exception.Message);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadAsync();
    }

    private void Mode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModeCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string mode)
        {
            return;
        }

        if (_policy.Mode != mode)
        {
            _policy.Mode = mode;
            MarkDirty();
        }
    }

    private async void AddRule_Click(object sender, RoutedEventArgs e)
    {
        var domainBox = new TextBox { Text = DomainBox.Text?.Trim() ?? string.Empty, PlaceholderText = "example.com（可粘贴 https:// 网址）" };
        var subdomainCheck = new CheckBox { Content = "包含子域名", IsChecked = SubdomainCheck.IsChecked };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(domainBox);
        panel.Children.Add(subdomainCheck);

        var dialog = new ContentDialog
        {
            Title = "添加规则",
            Content = panel,
            PrimaryButtonText = "添加",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var domain = domainBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(domain))
        {
            SetInfoBar(InfoBarSeverity.Error, "域名不能为空。");
            return;
        }

        _policy.Rules.Add(new AdminRule
        {
            Domain = domain,
            IncludeSubdomains = subdomainCheck.IsChecked == true,
            Enabled = true
        });
        DomainBox.Text = string.Empty;
        RefreshRuleList();
        MarkDirty();
    }

    private async void EditRule_Click(object sender, RoutedEventArgs e)
    {
        if (RulesList.SelectedItem is not AdminRule rule)
        {
            SetInfoBar(InfoBarSeverity.Warning, "请先选择一条规则。");
            return;
        }

        var domainBox = new TextBox { Text = rule.Domain };
        var subdomainCheck = new CheckBox { Content = "包含子域名", IsChecked = rule.IncludeSubdomains };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(domainBox);
        panel.Children.Add(subdomainCheck);

        var dialog = new ContentDialog
        {
            Title = "编辑规则",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var index = _policy.Rules.IndexOf(rule);
        if (index < 0)
        {
            return;
        }

        var domain = domainBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(domain))
        {
            SetInfoBar(InfoBarSeverity.Error, "域名不能为空。");
            return;
        }

        _policy.Rules[index] = new AdminRule
        {
            Domain = domain,
            IncludeSubdomains = subdomainCheck.IsChecked == true,
            Enabled = rule.Enabled
        };
        RefreshRuleList();
        MarkDirty();
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (RulesList.SelectedItem is not AdminRule rule)
        {
            SetInfoBar(InfoBarSeverity.Warning, "请先选择一条规则。");
            return;
        }

        _policy.Rules.Remove(rule);
        RefreshRuleList();
        MarkDirty();
    }

    private void ToggleRule_Click(object sender, RoutedEventArgs e)
    {
        if (RulesList.SelectedItem is not AdminRule rule)
        {
            SetInfoBar(InfoBarSeverity.Warning, "请先选择一条规则。");
            return;
        }

        var index = _policy.Rules.IndexOf(rule);
        _policy.Rules[index] = new AdminRule
        {
            Domain = rule.Domain,
            IncludeSubdomains = rule.IncludeSubdomains,
            Enabled = !rule.Enabled
        };
        RefreshRuleList();
        MarkDirty();
    }

    private async void AddTimedCategory_Click(object sender, RoutedEventArgs e)
    {
        var name = TimedNameBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            SetInfoBar(InfoBarSeverity.Error, "类别名称不能为空。");
            return;
        }

        if (_policy.TimedCategories.Any(category => string.Equals(category.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            SetInfoBar(InfoBarSeverity.Error, "类别名称重复。");
            return;
        }

        var minutes = double.IsNaN(TimedMinutesBox.Value) ? 30 : (int)TimedMinutesBox.Value;
        _policy.TimedCategories.Add(new AdminTimedCategory { Name = name, LimitMinutes = minutes });
        TimedNameBox.Text = string.Empty;
        RefreshTimedLists();
        MarkDirty();
        await Task.CompletedTask;
    }

    private void DeleteTimedCategory_Click(object sender, RoutedEventArgs e)
    {
        if (TimedCategoriesList.SelectedItem is not AdminTimedCategory category)
        {
            SetInfoBar(InfoBarSeverity.Warning, "请先选择一个类别。");
            return;
        }

        _policy.TimedCategories.Remove(category);
        _policy.TimedRules.RemoveAll(rule => string.Equals(rule.Category, category.Name, StringComparison.OrdinalIgnoreCase));
        RefreshTimedLists();
        MarkDirty();
    }

    private void AddTimedRule_Click(object sender, RoutedEventArgs e)
    {
        var domain = TimedDomainBox.Text?.Trim();
        var category = TimedCategoryCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(category))
        {
            SetInfoBar(InfoBarSeverity.Error, "请填写域名并选择类别。");
            return;
        }

        _policy.TimedRules.Add(new AdminTimedRule
        {
            Domain = domain,
            IncludeSubdomains = TimedSubdomainCheck.IsChecked == true,
            Category = category,
            Enabled = true
        });
        TimedDomainBox.Text = string.Empty;
        RefreshTimedLists();
        MarkDirty();
    }

    private void DeleteTimedRule_Click(object sender, RoutedEventArgs e)
    {
        if (TimedRulesList.SelectedItem is not AdminTimedRule rule)
        {
            SetInfoBar(InfoBarSeverity.Warning, "请先选择一条限时网站。");
            return;
        }

        _policy.TimedRules.Remove(rule);
        RefreshTimedLists();
        MarkDirty();
    }

    private async void BrowseCategories_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
            picker.FileTypeFilter.Add(".md");
            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                CategoryPathBox.Text = file.Path;
            }
        }
        catch (Exception exception)
        {
            SetInfoBar(InfoBarSeverity.Error, "无法打开文件选择器：" + exception.Message);
        }
    }

    private async void ImportCategories_Click(object sender, RoutedEventArgs e)
    {
        var path = CategoryPathBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            SetInfoBar(InfoBarSeverity.Error, "请选择有效的 Markdown 名单文件。");
            return;
        }

        try
        {
            var parsed = await _client.ParseCategoriesAsync(path);
            var summary = "类别：" + parsed.Categories.Count + " 个\r\n域名规则：" + parsed.Rules.Count + " 条\r\n重复项：" + parsed.Duplicates;
            if (parsed.Errors.Count > 0)
            {
                summary += "\r\n\r\n问题（前 8 条）：\r\n" + string.Join("\r\n", parsed.Errors.Take(8));
            }

            summary += "\r\n\r\n导入将替换当前分类名单（同名类别的启用状态保留）。是否继续？";
            var dialog = new ContentDialog
            {
                Title = "导入分类名单",
                Content = new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = "导入",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = RootGrid.XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary || parsed.Categories.Count == 0)
            {
                return;
            }

            var enabled = new HashSet<string>(
                _policy.Categories.Where(category => category.Enabled).Select(category => category.Name),
                StringComparer.OrdinalIgnoreCase);
            _policy.Categories.Clear();
            _policy.CategoryRules.Clear();
            foreach (var category in parsed.Categories)
            {
                category.Enabled = enabled.Contains(category.Name);
                _policy.Categories.Add(category);
            }

            _policy.CategoryRules.AddRange(parsed.Rules);
            RefreshCategoryList();
            MarkDirty();
            SetInfoBar(InfoBarSeverity.Warning, "已解析名单，点击「保存策略」后生效。");
        }
        catch (Exception exception)
        {
            SetInfoBar(InfoBarSeverity.Error, "导入失败：" + exception.Message);
        }
    }

    private void ToggleCategory_Click(object sender, RoutedEventArgs e)
    {
        if (CategoriesList.SelectedItem is not AdminCategory category)
        {
            SetInfoBar(InfoBarSeverity.Warning, "请先选择一个类别。");
            return;
        }

        category.Enabled = !category.Enabled;
        RefreshCategoryList();
        MarkDirty();
    }

    private async void LogSearch_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        await RefreshLogsAsync();
    }

    private async void BlockedOnly_Click(object sender, RoutedEventArgs e)
    {
        await RefreshLogsAsync();
    }

    private async void RefreshLogs_Click(object sender, RoutedEventArgs e)
    {
        await RefreshLogsAsync();
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not AdminHistoryRow row)
        {
            SetInfoBar(InfoBarSeverity.Warning, "请先选择一个历史版本。");
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "恢复策略版本",
            Content = "将版本 " + row.Version + " 恢复为新的当前版本？现有历史不会被改写。",
            PrimaryButtonText = "恢复",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            var version = await _client.RestoreAsync(row.Version);
            await LoadAsync();
            SetInfoBar(InfoBarSeverity.Success, "已恢复为版本 " + version + "。");
        }
        catch (Exception exception)
        {
            SetInfoBar(InfoBarSeverity.Error, "恢复失败：" + exception.Message);
        }
    }

    private async void PairDevice_Click(object sender, RoutedEventArgs e)
    {
        var address = (DeviceAddressBox.Text ?? string.Empty).Trim();
        var code = (PairingCodeBox.Text ?? string.Empty).Trim();
        var port = double.IsNaN(DevicePortBox.Value) ? 18770 : (int)DevicePortBox.Value;
        if (address.Length == 0 || code.Length == 0)
        {
            SetInfoBar(InfoBarSeverity.Warning, "请填写被控端 IP 与配对码。");
            return;
        }

        try
        {
            SetInfoBar(InfoBarSeverity.Informational, "正在与被控端建立安全连接并配对…");
            var device = await Task.Run(() => _deviceLink.Pair(address, port, code));
            PairingCodeBox.Text = string.Empty;
            SetInfoBar(InfoBarSeverity.Success,
                "已与被控端「" + device.Name + "」配对。请核对两端指纹一致：" + device.Fingerprint);
            await RefreshDevicesAsync();
        }
        catch (Exception exception)
        {
            SetInfoBar(InfoBarSeverity.Error, "配对失败：" + exception.Message);
        }
    }

    private async void RefreshDevices_Click(object sender, RoutedEventArgs e)
    {
        await RefreshDevicesAsync();
    }

    private async Task RefreshDevicesAsync()
    {
        var stored = _deviceLink.LoadDevices();
        var rows = new List<AdminDeviceRow>();
        foreach (var device in stored)
        {
            rows.Add(new AdminDeviceRow
            {
                Device = device,
                Name = string.IsNullOrEmpty(device.Name) ? device.DeviceId : device.Name,
                Address = device.Address + ":" + device.Port,
                StatusText = "检测中…",
                Detail = "指纹 " + device.Fingerprint,
                StatusBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 88, 103, 125))
            });
        }

        DeviceList.ItemsSource = rows;

        foreach (var row in rows)
        {
            var device = row.Device;
            try
            {
                var status = await Task.Run(() => _deviceLink.RefreshStatus(device));
                row.StatusText = "在线";
                row.StatusBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 15, 123, 15));
                row.Detail = "策略 v" + status.PolicyVersion + " · " + status.Rules + " 条规则 · " +
                             status.Categories + " 个类别 · 事件 " + status.Events + " · 已配对管理端 " + status.PairedAdmins;
            }
            catch (Exception exception)
            {
                row.StatusText = "离线";
                row.StatusBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 157, 93, 0));
                row.Detail = exception.Message;
            }
        }

        DeviceList.ItemsSource = null;
        DeviceList.ItemsSource = rows;
        DeviceSyncHint.Text = "共 " + rows.Count + " 台已配对设备。日志按设备隔离保存到本地审计库。";
    }

    private async void PushPolicy_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is not AdminDeviceRow row)
        {
            SetInfoBar(InfoBarSeverity.Warning, "请先在列表中选择一台被控端。");
            return;
        }

        if (!_policy.HasPolicy)
        {
            SetInfoBar(InfoBarSeverity.Warning, "本机尚未保存策略，请先保存策略后再下发。");
            return;
        }

        try
        {
            SetInfoBar(InfoBarSeverity.Informational, "正在向「" + row.Name + "」下发当前策略…");
            var version = await Task.Run(() => _deviceLink.PushPolicy(row.Device, _policy));
            SetInfoBar(InfoBarSeverity.Success,
                "已下发到「" + row.Name + "」，被控端策略版本 " + version + "；被控端代理会在 1 秒内热加载。");
            await RefreshDevicesAsync();
        }
        catch (Exception exception)
        {
            SetInfoBar(InfoBarSeverity.Error, "下发失败：" + exception.Message);
        }
    }

    private async void SyncDeviceEvents_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is not AdminDeviceRow row)
        {
            SetInfoBar(InfoBarSeverity.Warning, "请先在列表中选择一台被控端。");
            return;
        }

        try
        {
            SetInfoBar(InfoBarSeverity.Informational, "正在同步「" + row.Name + "」的访问日志…");
            var imported = await Task.Run(async () => await _deviceLink.SyncEventsAsync(row.Device));
            await RefreshDevicesAsync();
            SetInfoBar(InfoBarSeverity.Success, "已同步 " + imported + " 条新事件（重复事件会被忽略）。");
        }
        catch (Exception exception)
        {
            SetInfoBar(InfoBarSeverity.Error, "同步失败：" + exception.Message);
        }
    }

    private async void UnpairDevice_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceList.SelectedItem is not AdminDeviceRow row)
        {
            SetInfoBar(InfoBarSeverity.Warning, "请先在列表中选择一台被控端。");
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "解除配对",
            Content = "解除后将无法再管理「" + row.Name + "」，需要在其窗口重新读取配对码才能再次配对。",
            PrimaryButtonText = "解除配对",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await Task.Run(() => _deviceLink.Unpair(row.Device));
            SetInfoBar(InfoBarSeverity.Informational, "已解除与「" + row.Name + "」的配对。");
            await RefreshDevicesAsync();
        }
        catch (Exception exception)
        {
            SetInfoBar(InfoBarSeverity.Error, "解除配对失败：" + exception.Message);
        }
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string destination ||
            !_pages.ContainsKey(destination))
        {
            return;
        }

        foreach (var page in _pages.Values)
        {
            page.Visibility = Visibility.Collapsed;
        }

        _pages[destination].Visibility = Visibility.Visible;

        if (destination == "requests")
        {
            RefreshRequests();
        }

        if (destination == "devices")
        {
            _ = RefreshDevicesAsync();
        }

        (PageTitle.Text, PageSubtitle.Text) = destination switch
        {
            "rules" => ("网页访问规则", "白名单 / 黑名单与域名匹配规则"),
            "timed" => ("限时访问", "每日连续墙钟额度的类别与网站"),
            "categories" => ("分类管控", "从管理员审核的 Markdown 导入分类名单"),
            "requests" => ("访问申请", "来自拦截页、保存在本机的访问申请"),
            "devices" => ("设备连接", "局域网配对、下发策略与同步日志"),
            "logs" => ("访问日志", "本地访问事件，违规记录以红色标注"),
            "history" => ("策略历史", "按版本恢复，历史只增不改"),
            _ => ("总览", "本机策略与审计（WebBlocker\\proxy.db）")
        };
    }

    private void ThemeSystem_Click(object sender, RoutedEventArgs e) => ApplyTheme(ElementTheme.Default, "跟随系统");

    private void ThemeLight_Click(object sender, RoutedEventArgs e) => ApplyTheme(ElementTheme.Light, "浅色");

    private void ThemeDark_Click(object sender, RoutedEventArgs e) => ApplyTheme(ElementTheme.Dark, "深色");

    /// <summary>
    /// Applies the theme to the whole content root and remembers the choice. The window background is a
    /// theme resource, so a dark theme really darkens the page instead of leaving light text on a light
    /// surface.
    /// </summary>
    private void ApplyTheme(ElementTheme theme, string label)
    {
        RootGrid.RequestedTheme = theme;
        ThemeLabel.Text = label;
        ApplyTitleBarTheme(theme);
        SaveTheme(label == "深色" ? "dark" : label == "浅色" ? "light" : "system");
    }

    /// <summary>
    /// Keeps the native title bar in step with the content theme; otherwise a dark page keeps a white
    /// title bar strip. Unsupported combinations (for example Windows 10) are ignored.
    /// </summary>
    private void ApplyTitleBarTheme(ElementTheme theme)
    {
        try
        {
            var titleBar = AppWindow.TitleBar;
            if (theme == ElementTheme.Dark)
            {
                titleBar.BackgroundColor = Color.FromArgb(255, 32, 32, 32);
                titleBar.ForegroundColor = Colors.White;
                titleBar.ButtonBackgroundColor = Color.FromArgb(255, 32, 32, 32);
                titleBar.ButtonForegroundColor = Colors.White;
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 58, 58, 58);
                titleBar.ButtonHoverForegroundColor = Colors.White;
            }
            else if (theme == ElementTheme.Light)
            {
                titleBar.BackgroundColor = Color.FromArgb(255, 243, 243, 243);
                titleBar.ForegroundColor = Colors.Black;
                titleBar.ButtonBackgroundColor = Color.FromArgb(255, 243, 243, 243);
                titleBar.ButtonForegroundColor = Colors.Black;
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(255, 229, 229, 229);
                titleBar.ButtonHoverForegroundColor = Colors.Black;
            }
            else
            {
                titleBar.BackgroundColor = null;
                titleBar.ForegroundColor = null;
                titleBar.ButtonBackgroundColor = null;
                titleBar.ButtonForegroundColor = null;
                titleBar.ButtonHoverBackgroundColor = null;
                titleBar.ButtonHoverForegroundColor = null;
            }
        }
        catch (Exception)
        {
            // Title bar customisation is a cosmetic extra; never let it break the window.
        }
    }

    private void ApplySavedTheme()
    {
        var value = "system";
        try
        {
            if (File.Exists(ThemeStatePath))
            {
                value = File.ReadAllText(ThemeStatePath).Trim().ToLowerInvariant();
            }
        }
        catch (IOException)
        {
        }

        switch (value)
        {
            case "dark":
                ThemeDark.IsChecked = true;
                ApplyTheme(ElementTheme.Dark, "深色");
                break;
            case "light":
                ThemeLight.IsChecked = true;
                ApplyTheme(ElementTheme.Light, "浅色");
                break;
            default:
                ThemeSystem.IsChecked = true;
                ApplyTheme(ElementTheme.Default, "跟随系统");
                break;
        }
    }

    private static void SaveTheme(string value)
    {
        try
        {
            var directory = Path.GetDirectoryName(ThemeStatePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(ThemeStatePath, value);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void WriteHeartbeat()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(HeartbeatPath));
            File.WriteAllText(HeartbeatPath, DateTime.UtcNow.ToString("o"));
        }
        catch (Exception)
        {
        }
    }

    private void LoadManagementState()
    {
        bool enabled = false;
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

        _managementSwitchReady = false;
        ManagementSwitch.IsOn = enabled;
        _managementSwitchReady = true;
    }

    private async void ManagementSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_managementSwitchReady)
        {
            return;
        }

        if (!ManagementSwitch.IsOn)
        {
            var dialog = new ContentDialog
            {
                Title = "暂停管理",
                Content = "暂停后，被控端将停止本地拦截。确定要暂停管理吗？",
                PrimaryButtonText = "暂停",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                _managementSwitchReady = false;
                ManagementSwitch.IsOn = true;
                _managementSwitchReady = true;
                return;
            }
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ManagementStatePath));
            File.WriteAllText(ManagementStatePath, ManagementSwitch.IsOn ? "enabled" : "disabled");
            SetInfoBar(
                InfoBarSeverity.Success,
                ManagementSwitch.IsOn
                    ? "管理已启用，被控端将在数秒内开始拦截。"
                    : "管理已暂停，被控端将在数秒内停止拦截。");
        }
        catch (Exception exception)
        {
            SetInfoBar(InfoBarSeverity.Error, "写入管理状态失败：" + exception.Message);
        }
    }
    private void ManagedBrowse_Click(object sender, RoutedEventArgs e)
    {
        var portPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WebBlocker",
            "agent.port");
        if (!File.Exists(portPath) ||
            !int.TryParse(File.ReadAllText(portPath).Trim(), out var port) ||
            port <= 0)
        {
            SetInfoBar(InfoBarSeverity.Warning, "未检测到被控端代理。请先启动被控端，并确认「启用管理」已打开。");
            return;
        }

        var edge = FindEdge();
        if (edge == null)
        {
            SetInfoBar(InfoBarSeverity.Error, "未找到 Microsoft Edge，无法打开受管浏览。");
            return;
        }

        var profile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WebBlocker",
            "managed-browser");
        Directory.CreateDirectory(profile);
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = edge,
                Arguments = "--proxy-server=http://127.0.0.1:" + port +
                    " --user-data-dir=\"" + profile + "\" --disable-quic --window-size=1200,900 --new-window http://example.com/",
                UseShellExecute = false
            });
            SetInfoBar(InfoBarSeverity.Success, "已用受管代理打开浏览器（端口 " + port + "）。访问未授权网站应显示拦截页。");
        }
        catch (Exception exception)
        {
            SetInfoBar(InfoBarSeverity.Error, "打开受管浏览失败：" + exception.Message);
        }
    }

    private static string FindEdge()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe")
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
    private void RefreshRequests_Click(object sender, RoutedEventArgs e) => RefreshRequests();

    private void RefreshRequests()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WebBlocker",
            "access-requests.jsonl");
        var items = new List<AdminAccessRequest>();
        if (File.Exists(path))
        {
            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var document = JsonDocument.Parse(line.TrimStart('\uFEFF'));
                    var root = document.RootElement;
                    var createdAt = root.TryGetProperty("createdAtUtc", out var created) &&
                                    created.TryGetDateTime(out var parsed)
                        ? parsed.ToLocalTime()
                        : DateTime.Now;
                    items.Add(new AdminAccessRequest
                    {
                        Id = root.TryGetProperty("id", out var id) ? id.GetString() : "",
                        Url = root.TryGetProperty("url", out var url) ? url.GetString() : "",
                        Host = root.TryGetProperty("host", out var host) ? host.GetString() : "",
                        Reason = root.TryGetProperty("reason", out var reason) ? reason.GetString() : "",
                        TimeText = createdAt.ToString("MM-dd HH:mm"),
                        StatusText = "待处理"
                    });
                }
                catch (JsonException)
                {
                }
            }
        }

        RequestsList.ItemsSource = items;
        RequestsHint.Text = items.Count == 0
            ? "暂无申请。拦截页提交后会保存在这里。"
            : "共 " + items.Count + " 条申请；批准后会将域名加入当前策略。";
    }

    private async void ApproveRequest_Click(object sender, RoutedEventArgs e)
    {
        if (RequestsList.SelectedItem is not AdminAccessRequest request ||
            string.IsNullOrWhiteSpace(request.Host))
        {
            SetInfoBar(InfoBarSeverity.Warning, "请先选择一条带域名的申请。");
            return;
        }

        if (_policy.Rules.Any(rule => string.Equals(rule.Domain, request.Host, StringComparison.OrdinalIgnoreCase)))
        {
            request.StatusText = "已在规则中";
            SetInfoBar(InfoBarSeverity.Informational, request.Host + " 已在当前规则中。");
            return;
        }

        _policy.Rules.Add(new AdminRule
        {
            Domain = request.Host,
            IncludeSubdomains = false,
            Enabled = true
        });
        try
        {
            await _client.SavePolicyAsync(_policy);
            request.StatusText = "已允许";
            RefreshRuleList();
            SetInfoBar(InfoBarSeverity.Success, "已允许 " + request.Host + "，策略已保存并即时生效。");
        }
        catch (Exception exception)
        {
            _policy.Rules.Remove(_policy.Rules.Last());
            SetInfoBar(InfoBarSeverity.Error, "保存失败：" + exception.Message);
        }
    }
    private void SetInfoBar(InfoBarSeverity severity, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
    }
}
