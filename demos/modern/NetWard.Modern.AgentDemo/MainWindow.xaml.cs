using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NetWard.Modern.AgentDemo;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = "NetWard 被控端 · Modern Demo";
    }

    private void Details_Click(object sender, RoutedEventArgs e)
    {
        AgentInfo.Visibility = Visibility.Visible;
        AgentInfoTitle.Text = "功能尚未实现";
        AgentInfoMessage.Text = "该演示仅展示 WinUI 3 界面方向。配对、设备身份、策略接收、访问过滤和日志同步均未实现。";
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
            _ => throw new System.InvalidOperationException($"Unknown current theme: {RootGrid.RequestedTheme}")
        };
        RootGrid.RequestedTheme = theme;
        ThemeButton.Content = theme switch
        {
            ElementTheme.Light => "主题：浅色",
            ElementTheme.Dark => "主题：深色",
            ElementTheme.Default => "主题：跟随系统",
            _ => throw new System.InvalidOperationException($"Unknown theme: {theme}")
        };
    }
}
