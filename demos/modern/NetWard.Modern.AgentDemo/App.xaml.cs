using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NetWard.Modern.AgentDemo;

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
            Title = "NetWard 被控端",
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
                        Text = "正在准备被控端界面…",
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            }
        };
        _window = startupWindow;
        startupWindow.Activate();

        var startupTimer = startupWindow.DispatcherQueue.CreateTimer();
        startupTimer.Interval = System.TimeSpan.FromMilliseconds(350);
        startupTimer.IsRepeating = false;
        startupTimer.Tick += (_, _) =>
        {
            startupTimer.Stop();
            var mainWindow = new MainWindow();
            _window = mainWindow;
            mainWindow.Activate();
            startupWindow.Close();
        };
        startupTimer.Start();
    }
}
