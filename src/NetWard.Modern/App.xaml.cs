using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace NetWard.Modern;

public partial class App : Application
{
    private Window _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var startupWindow = new Window
        {
            Title = "NetWard 网界卫士",
            Content = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "NetWard",
                        FontSize = 24,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "正在载入 Modern 控制台…",
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            }
        };
        _window = startupWindow;
        startupWindow.Activate();

        var timer = startupWindow.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(300);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                var mainWindow = new MainWindow();
                _window = mainWindow;
                mainWindow.Activate();
                startupWindow.Close();
            }
            catch (Exception exception)
            {

                var errorWindow = new Window { Title = "NetWard.Modern 启动失败" };
                errorWindow.Content = new ScrollViewer
                {
                    Padding = new Thickness(16),
                    Content = new TextBlock
                    {
                        Text = exception.ToString(),
                        TextWrapping = TextWrapping.Wrap,
                        IsTextSelectionEnabled = true
                    }
                };
                _window = errorWindow;
                errorWindow.Activate();
            }
        };
        timer.Start();
    }
}