using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DragConvert.App.Overlay;
using DragConvert.Native.Windowing;
using H.NotifyIcon;
using Serilog;

namespace DragConvert.App;

public partial class App : Application
{
    private static readonly string LogFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DragConvert", "logs");

    private TaskbarIcon? _trayIcon;
    private OverlayController? _overlay;

    protected override void OnStartup(StartupEventArgs e)
    {
        long startTimestamp = Stopwatch.GetTimestamp();
        base.OnStartup(e);
        AppOptions options = AppOptions.Parse(e.Args);

        if (options.Console)
        {
            OverlayWindowInterop.AllocateConsole();
        }

        ConfigureLogging(options.Console);
        DispatcherUnhandledException += (_, args) => Log.Fatal(args.Exception, "Unhandled exception on the UI thread");
        Log.Information("DragConvert starting; args {Args}; renderer {Renderer}", e.Args, options.Renderer);

        _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
        // Efficiency mode lowers process priority, which would hurt the overlay's show latency.
        _trayIcon.ForceCreate(enablesEfficiencyMode: false);
        if (_trayIcon.ContextMenu?.Items.OfType<MenuItem>().FirstOrDefault(i => i.Name == "RendererItem") is { } rendererItem)
        {
            rendererItem.Header = $"Renderer: {options.Renderer.ToString().ToLowerInvariant()}";
        }

        if (IsElevated())
        {
            // UIPI blocks drag-and-drop from normal-integrity apps (Explorer, browsers) into an elevated process.
            Log.Warning("Running elevated (as administrator): drops from normal apps such as Explorer will be blocked by Windows");
            _trayIcon.ShowNotification(
                "DragConvert is running as administrator",
                "Windows blocks drops from normal apps like Explorer. Restart DragConvert without administrator rights.");
        }

        _overlay = new OverlayController(Dispatcher, options.Renderer);
        _overlay.Start();
        Log.Information("Startup finished in {Ms:F0} ms", Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);

        // --smoke: start fully, then exit once the dispatcher is idle. Used by CI to prove the app starts and exits cleanly.
        if (options.Smoke)
        {
            Dispatcher.BeginInvoke(() => Shutdown(0), DispatcherPriority.ApplicationIdle);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("DragConvert exiting with code {Code}", e.ApplicationExitCode);
        _overlay?.Dispose();
        _trayIcon?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void ConfigureLogging(bool console)
    {
        const string template = "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";
        var config = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(Path.Combine(LogFolder, "dragconvert-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7, outputTemplate: template);
        if (console)
        {
            config = config.WriteTo.Console(outputTemplate: template);
        }

        Log.Logger = config.CreateLogger();
    }

    private void OnOpenLogFolderClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(LogFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", LogFolder) { UseShellExecute = true });
    }

    private void OnQuitClick(object sender, RoutedEventArgs e) => Shutdown(0);
}
