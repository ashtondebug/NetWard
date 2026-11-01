using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace NetWard.Modern.Administrator;

public partial class App : Application
{
    private Window _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var startup = new Window
        {
            Title = "NetWard 网界卫士 · Modern 管理端",
            Content = new TextBlock
            {
                Text = "正在载入 Modern 管理端…",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        _window = startup;
        startup.Activate();

        var timer = startup.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(300);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                var main = new MainWindow();
                _window = main;
                main.Activate();
                startup.Close();
            }
            catch (Exception exception)
            {
                var error = new Window
                {
                    Title = "Modern 管理端启动失败",
                    Content = new ScrollViewer
                    {
                        Padding = new Thickness(16),
                        Content = new TextBlock
                        {
                            Text = exception.ToString(),
                            TextWrapping = TextWrapping.Wrap,
                            IsTextSelectionEnabled = true
                        }
                    }
                };
                _window = error;
                error.Activate();
            }
        };
        timer.Start();
    }
}