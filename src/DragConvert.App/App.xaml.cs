using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DragConvert.App.Conversion;
using DragConvert.App.Gallery;
using DragConvert.App.Instance;
using DragConvert.App.Notifications;
using DragConvert.App.Overlay;
using DragConvert.App.Theme;
using DragConvert.App.Windows;
using DragConvert.Core.Conversion;
using DragConvert.Core.Input;
using DragConvert.Core.Instance;
using DragConvert.Core.Settings;
using DragConvert.Native.Shell;
using DragConvert.Native.Windowing;
using DragConvert.Core.Tools;
using DragConvert.Providers.Windows;
using DragConvert.Providers.Ffmpeg;
using H.NotifyIcon;
using Serilog;

namespace DragConvert.App;

public partial class App : Application
{
    private readonly SettingsStore _settingsStore = new(AppPaths.SettingsFile);
    private AppSettings _settings = new();
    private SingleInstance? _instance;
    private TaskbarIcon? _trayIcon;
    private NotificationService? _notifications;
    private ConversionCoordinator? _coordinator;
    private OverlayController? _overlay;
    private PickerWindow? _picker;
    private SettingsWindow? _settingsWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        long startTimestamp = Stopwatch.GetTimestamp();
        base.OnStartup(e);
        AppOptions options = AppOptions.Parse(e.Args);

        // Developer tools: render the UI with fake data instead of starting the tray app.
        if (options.SnapshotDirectory is { } snapshotDirectory)
        {
            foreach (string file in UiGallery.Snapshot(snapshotDirectory))
            {
                Console.WriteLine(file);
            }

            Shutdown(0);
            return;
        }

        if (options.PreviewOverlay is { } previewRenderer)
        {
            ConfigureLogging(console: false);
            DispatcherUnhandledException += (_, args) => Log.Fatal(args.Exception, "Unhandled exception in overlay preview");
            UiGallery.PreviewOverlay(previewRenderer, TimeSpan.FromSeconds(8), () => Shutdown(0));
            return;
        }

        if (options.Gallery)
        {
            Window gallery = UiGallery.CreateWindow();
            gallery.Closed += (_, _) => Shutdown(0);
            gallery.Show();
            return;
        }

        // Later launches (Send To, shortcuts) hand their files to the running tray process and exit.
        _instance = SingleInstance.TryAcquire();
        if (_instance is null)
        {
            SingleInstance.Send(options.Paths.Count > 0 ? InstanceMessage.Open(options.Paths) : InstanceMessage.Show());
            Shutdown(0);
            return;
        }

        if (options.Console)
        {
            OverlayWindowInterop.AllocateConsole();
        }

        ConfigureLogging(options.Console);
        DispatcherUnhandledException += (_, args) => Log.Fatal(args.Exception, "Unhandled exception on the UI thread");
        _settings = _settingsStore.Load();
        OverlayRenderer renderer = options.Renderer ?? RendererFor(_settings);
        Log.Information("DragConvert starting; args {Args}; renderer {Renderer}; settings {@Settings}", e.Args, renderer, _settings);

        _notifications = new NotificationService(Dispatcher);
        _notifications.ActionInvoked += (verb, _) =>
        {
            if (verb == "show")
            {
                _picker?.ShowEmpty();
            }
        };

        string? ffmpeg = FfmpegLocator.Find();
        Log.Information("FFmpeg: {Path}", ffmpeg ?? "not found (audio and video conversions unavailable)");
        var catalog = new FormatCatalog(AllProviders.Converters(ffmpeg));
        var tools = new ToolCatalog(AllProviders.Tools(ffmpeg));
        _coordinator = new ConversionCoordinator(catalog, tools, _notifications, () => _settings, Dispatcher);
        _picker = new PickerWindow(_coordinator);

        _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
        _trayIcon.Icon = AppIcon.Tray();
        // Efficiency mode lowers process priority, which would hurt the overlay's show latency.
        _trayIcon.ForceCreate(enablesEfficiencyMode: false);
        _trayIcon.TrayLeftMouseUp += (_, _) => _picker.ShowEmpty();

        if (IsElevated())
        {
            // UIPI blocks drag-and-drop from normal-integrity apps (Explorer, browsers) into an elevated process.
            Log.Warning("Running elevated (as administrator): drops from normal apps such as Explorer will be blocked by Windows");
            _notifications.Show(new NotificationText(
                "DragConvert is running as administrator",
                "Windows blocks drops from normal apps like Explorer. Restart DragConvert without administrator rights."));
        }

        _overlay = new OverlayController(Dispatcher, renderer, _coordinator, ChordFor(_settings));
        _overlay.Start();

        if (!options.Smoke)
        {
            ApplyShellIntegration(_settings);
        }

        _instance.Listen(message => Dispatcher.InvokeAsync(() => OnInstanceMessage(message)));
        if (options.Paths.Count > 0)
        {
            _picker.ShowFor(options.Paths);
        }

        Log.Information("Startup finished in {Ms:F0} ms", Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds);

        // --smoke: start fully, then exit once the dispatcher is idle. Used by CI to prove the app starts and exits cleanly.
        if (options.Smoke)
        {
            Dispatcher.BeginInvoke(() => Shutdown(0), DispatcherPriority.ApplicationIdle);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_instance is not null)
        {
            Log.Information("DragConvert exiting with code {Code}", e.ApplicationExitCode);
            _overlay?.Dispose();
            _coordinator?.Dispose();
            _trayIcon?.Dispose();
            _instance.Dispose();
            Log.CloseAndFlush();
        }

        base.OnExit(e);
    }

    /// <summary>The overlay style from settings; unknown values (such as the retired "dwm") fall back to Glass.</summary>
    private static OverlayRenderer RendererFor(AppSettings settings) =>
        Enum.TryParse(settings.Renderer, ignoreCase: true, out OverlayRenderer r) && Enum.IsDefined(r) ? r : OverlayRenderer.Glass;

    private static ModifierChord ChordFor(AppSettings settings) => ModifierChord.Default with { Trigger = settings.Trigger };

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
            .WriteTo.File(Path.Combine(AppPaths.LogFolder, "dragconvert-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7, outputTemplate: template);
        if (console)
        {
            config = config.WriteTo.Console(outputTemplate: template);
        }

        Log.Logger = config.CreateLogger();
    }

    /// <summary>Send To shortcut and autostart, per the settings. Failures are logged, never fatal.</summary>
    private static void ApplyShellIntegration(AppSettings settings)
    {
        try
        {
            if (settings.SendToMenu)
            {
                ShellIntegration.CreateShortcut(AppPaths.SendToShortcut, AppPaths.ExecutablePath, "--send-to", "Convert with DragConvert", AppPaths.ExecutablePath);
            }
            else if (File.Exists(AppPaths.SendToShortcut))
            {
                File.Delete(AppPaths.SendToShortcut);
            }

            ShellIntegration.SetStartWithWindows("DragConvert", settings.StartWithWindows ? AppPaths.ExecutablePath : null);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Applying shell integration failed");
        }
    }

    private void OnInstanceMessage(InstanceMessage message)
    {
        Log.Information("Message from another launch: {Command} with {Count} path(s)", message.Command, message.Paths.Count);
        if (message.Command == InstanceMessage.OpenCommand && message.Paths.Count > 0)
        {
            _picker?.ShowFor(message.Paths);
        }
        else
        {
            _picker?.ShowEmpty();
        }
    }

    private void OnSettingsChanged(AppSettings settings)
    {
        bool shellChanged = settings.SendToMenu != _settings.SendToMenu || settings.StartWithWindows != _settings.StartWithWindows;
        _settings = settings;
        try
        {
            _settingsStore.Save(settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error(ex, "Saving settings failed");
        }

        if (_overlay is not null)
        {
            _overlay.Chord = ChordFor(settings);
            _overlay.SwitchRenderer(RendererFor(settings));
        }

        if (shellChanged)
        {
            ApplyShellIntegration(settings);
        }

        Log.Information("Settings changed: {@Settings}", settings);
    }

    private void OnOpenClick(object sender, RoutedEventArgs e) => _picker?.ShowEmpty();

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.SettingsChanged += OnSettingsChanged;
        }

        _settingsWindow.Present(_settings);
    }

    private void OnPauseClick(object sender, RoutedEventArgs e)
    {
        if (_overlay is not null && sender is MenuItem item)
        {
            _overlay.Paused = item.IsChecked;
            Log.Information("Drag overlay {State}", item.IsChecked ? "paused" : "resumed");
        }
    }

    private void OnOpenLogFolderClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.LogFolder);
        Shell.Open(AppPaths.LogFolder);
    }

    private void OnQuitClick(object sender, RoutedEventArgs e) => Shutdown(0);
}
