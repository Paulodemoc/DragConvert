using System.Windows;
using System.Windows.Threading;
using H.NotifyIcon;

namespace DragConvert.App;

public partial class App : Application
{
    private TaskbarIcon? _trayIcon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
        // Efficiency mode lowers process priority, which would hurt the overlay's show latency.
        _trayIcon.ForceCreate(enablesEfficiencyMode: false);

        // --smoke: start fully, then exit once the dispatcher is idle. Used by CI to prove the app starts and exits cleanly.
        if (e.Args.Contains("--smoke", StringComparer.OrdinalIgnoreCase))
        {
            Dispatcher.BeginInvoke(() => Shutdown(0), DispatcherPriority.ApplicationIdle);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        base.OnExit(e);
    }

    private void OnQuitClick(object sender, RoutedEventArgs e) => Shutdown(0);
}
