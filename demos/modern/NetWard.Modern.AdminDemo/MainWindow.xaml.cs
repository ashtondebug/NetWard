using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;

namespace NetWard.Modern.AdminDemo;

public sealed partial class MainWindow : Window
{
    private readonly Dictionary<string, Grid> _pages;
    private readonly Dictionary<string, ToggleButton> _navigation;

    public MainWindow()
    {
        InitializeComponent();
        Title = "NetWard 管理端 · Modern Demo";
        _pages = new Dictionary<string, Grid>(StringComparer.Ordinal)
        {
            ["overview"] = OverviewPage,
            ["devices"] = DevicesPage,
            ["rules"] = RulesPage,
            ["logs"] = LogsPage
        };
        _navigation = new Dictionary<string, ToggleButton>(StringComparer.Ordinal)
        {
            ["overview"] = OverviewNav,
            ["devices"] = DevicesNav,
            ["rules"] = RulesNav,
            ["logs"] = LogsNav
        };
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not string destination ||
            !_pages.ContainsKey(destination))
        {
            return;
        }

        foreach (var page in _pages.Values)
        {
            page.Visibility = Visibility.Collapsed;
        }

        _pages[destination].Visibility = Visibility.Visible;
        foreach (var item in _navigation)
        {
            item.Value.IsChecked = item.Key == destination;
        }

        (PageTitle.Text, PageSubtitle.Text) = destination switch
        {
            "devices" => ("设备管理", "静态设备示例 · 未连接任何被控端"),
            "rules" => ("访问策略", "界面交互演示 · 不会保存或下发策略"),
            "logs" => ("事件记录", "示例数据 · 非真实访问日志"),
            _ => ("管理总览", "演示状态 · 示例数据，不代表实际设备或审计记录")
        };
    }

    private void PolicyMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string mode)
        {
            return;
        }

        var allowSelected = mode == "allow";
        PolicyModeDescription.Text = allowSelected
            ? "命中规则允许；未命中默认拒绝。（仅当前界面演示）"
            : "命中规则拒绝；未命中默认允许。（仅当前界面演示）";
        AllowModeButton.Background = GetThemeBrush("C3267D5");
        AllowModeButton.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
        BlockModeButton.Background = GetThemeBrush("C202B3C");
        BlockModeButton.Foreground = GetThemeBrush("MutedBrush");
        if (!allowSelected)
        {
            BlockModeButton.Background = GetThemeBrush("C3267D5");
            BlockModeButton.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
            AllowModeButton.Background = GetThemeBrush("C202B3C");
            AllowModeButton.Foreground = GetThemeBrush("MutedBrush");
        }
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button)
        {
            return;
        }

        var theme = RootGrid.RequestedTheme switch
        {
            ElementTheme.Default => ElementTheme.Light,
            ElementTheme.Light => ElementTheme.Dark,
            ElementTheme.Dark => ElementTheme.Default,
            _ => throw new InvalidOperationException($"Unknown current theme: {RootGrid.RequestedTheme}")
        };
        RootGrid.RequestedTheme = theme;
        ThemeButton.Content = theme switch
        {
            ElementTheme.Light => "主题：浅色",
            ElementTheme.Dark => "主题：深色",
            ElementTheme.Default => "主题：跟随系统",
            _ => throw new InvalidOperationException($"Unknown theme: {theme}")
        };
    }

    private Brush GetThemeBrush(string key)
    {
        var themeName = RootGrid.ActualTheme.ToString();
        if (RootGrid.Resources.ThemeDictionaries[themeName] is not ResourceDictionary palette ||
            palette[key] is not Brush brush)
        {
            throw new InvalidOperationException($"Theme resource '{key}' was not found for '{themeName}'.");
        }

        return brush;
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        DemoInfo.Visibility = Visibility.Visible;
        DemoInfoTitle.Text = "Modern 管理端界面演示";
        DemoInfoMessage.Text = "此程序用于体验 WinUI 3 视觉方向。设备、规则和事件均为演示内容；没有网络连接、配对、策略持久化或网页拦截功能。";
    }

    private void Unimplemented_Click(object sender, RoutedEventArgs e)
    {
        DemoInfo.Visibility = Visibility.Visible;
        DemoInfoTitle.Text = "此功能尚未实现";
        DemoInfoMessage.Text = "远程配对、身份验证和设备登记还没有实现。本演示不会创建虚假的设备连接。";
    }
}
