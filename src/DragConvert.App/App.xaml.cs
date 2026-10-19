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
using DragConvert.Native.Platform;
using DragConvert.Native.Shell;
using DragConvert.Providers.Processes;
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
    private ChildProcessJob? _childJob;
    private DispatcherTimer? _idleTrim;
    private long _lastTrimTick;

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
            if (options.Smoke)
            {
                // Handing off would "pass" without testing anything: fail instead.
                Shutdown(2);
                return;
            }

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

        // Child processes (ffmpeg) die with the app, even if it crashes.
        try
        {
            _childJob = new ChildProcessJob();
            ProcessRunner.ProcessStarted = p => _childJob.Assign(p);
        }
        catch (InvalidOperationException ex)
        {
            Log.Warning(ex, "Job object unavailable; child processes won't be tied to the app");
        }

        // Temp outputs left behind by a crash are removed now.
        var journal = new TempJournal(Path.Combine(AppPaths.DataFolder, "temp-journal.txt"));
        int leftovers = journal.CleanUp();
        if (leftovers > 0)
        {
            Log.Information("Removed {Count} temp file(s) left by a previous run", leftovers);
        }

        string? ffmpeg = FfmpegLocator.Find();
        Log.Information("FFmpeg: {Path}", ffmpeg ?? "not found (audio and video conversions unavailable)");
        IReadOnlyList<IConversionProvider> converters = AllProviders.Converters(ffmpeg);
        var catalog = new FormatCatalog(converters);
        var tools = new ToolCatalog(AllProviders.Tools(ffmpeg));
        _coordinator = new ConversionCoordinator(catalog, tools, _notifications, () => _settings, Dispatcher, journal);
        if (!options.Smoke)
        {
            _ = ProbeH264Async(converters, catalog);
        }
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

        // Light/dark/high-contrast switches: rebuild the overlay with the new theme (debounced: Windows sends bursts).
        var themeDebounce = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (s, _) =>
        {
            ((DispatcherTimer)s!).Stop();
            Log.Information("Windows theme changed; rebuilding the overlay");
            _overlay?.Rebuild(options.Renderer ?? RendererFor(_settings));
        }, Dispatcher);
        themeDebounce.Stop();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, args) =>
        {
            if (args.Category is Microsoft.Win32.UserPreferenceCategory.General or Microsoft.Win32.UserPreferenceCategory.Color or Microsoft.Win32.UserPreferenceCategory.VisualStyle or Microsoft.Win32.UserPreferenceCategory.Accessibility)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    themeDebounce.Stop();
                    themeDebounce.Start();
                });
            }
        };

        // Idle memory: hand unused pages back to Windows when nothing is happening.
        _idleTrim = new DispatcherTimer(TimeSpan.FromSeconds(20), DispatcherPriority.ApplicationIdle, (_, _) => TrimIfIdle(), Dispatcher);

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

        // --smoke: start fully, convert a tiny image, then exit once the dispatcher is idle. Used by CI to prove the app
        // (including a single-file release build, whose native libraries are extracted at run time) starts and works.
        if (options.Smoke)
        {
            _ = Dispatcher.InvokeAsync(async () => Shutdown(await SmokeConvertAsync(catalog) ? 0 : 1), DispatcherPriority.ApplicationIdle);
        }
    }

    private static async Task<bool> SmokeConvertAsync(FormatCatalog catalog)
    {
        string folder = Path.Combine(Path.GetTempPath(), "DragConvert-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            // A 2×2, 24-bit BMP: 54-byte header, then two rows of 6 pixel bytes padded to 8.
            byte[] bmp = new byte[70];
            "BM"u8.CopyTo(bmp);
            BitConverter.TryWriteBytes(bmp.AsSpan(2), bmp.Length);
            BitConverter.TryWriteBytes(bmp.AsSpan(10), 54);
            BitConverter.TryWriteBytes(bmp.AsSpan(14), 40);
            BitConverter.TryWriteBytes(bmp.AsSpan(18), 2);
            BitConverter.TryWriteBytes(bmp.AsSpan(22), 2);
            BitConverter.TryWriteBytes(bmp.AsSpan(26), (short)1);
            BitConverter.TryWriteBytes(bmp.AsSpan(28), (short)24);
            bmp.AsSpan(54).Fill(0x80);
            string input = Path.Combine(folder, "smoke.bmp");
            string output = Path.Combine(folder, "smoke.png");
            await File.WriteAllBytesAsync(input, bmp);

            IConversionProvider provider = catalog.FindProvider(input, MediaFormats.Png) ?? throw new InvalidOperationException("No BMP → PNG provider");
            await provider.ConvertAsync(new ConversionJob(input, output, MediaFormats.Png, new Dictionary<string, string>()), null, CancellationToken.None);
            bool ok = new FileInfo(output).Length > 0;
            Log.Information("Smoke conversion BMP → PNG: {Result}", ok ? "ok" : "empty output");
            return ok;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Smoke conversion failed");
            return false;
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_instance is not null)
        {
            Log.Information("DragConvert exiting with code {Code}", e.ApplicationExitCode);
            _idleTrim?.Stop();
            _overlay?.Dispose();
            _coordinator?.Dispose();
            _childJob?.Dispose();
            _trayIcon?.Dispose();
            _instance.Dispose();
            Log.CloseAndFlush();
        }

        base.OnExit(e);
    }

    /// <summary>
    /// The overlay style from settings; unknown values (such as the retired "dwm") fall back to Glass. High contrast
    /// always uses the ring, whose palette switches to the system's high-contrast colours.
    /// </summary>
    private static OverlayRenderer RendererFor(AppSettings settings) =>
        SystemParameters.HighContrast ? OverlayRenderer.Layered
        : Enum.TryParse(settings.Renderer, ignoreCase: true, out OverlayRenderer r) && Enum.IsDefined(r) ? r : OverlayRenderer.Glass;

    /// <summary>On Windows "N" editions without Media Foundation, MP4 (h264_mf) can't encode: hide it.</summary>
    private static async Task ProbeH264Async(IReadOnlyList<IConversionProvider> converters, FormatCatalog catalog)
    {
        if (converters.OfType<FfmpegProvider>().FirstOrDefault() is not { IsAvailable: true } ffmpeg)
        {
            return;
        }

        try
        {
            if (!await ffmpeg.ProbeH264Async())
            {
                catalog.Refresh();
                Log.Warning("H.264 encoding (h264_mf) unavailable on this Windows; MP4 output hidden");
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        {
            Log.Warning(ex, "H.264 probe failed");
        }
    }

    private void TrimIfIdle()
    {
        if (_overlay?.IsActive == true || _coordinator?.IsBusy == true || Environment.TickCount64 - _lastTrimTick < 60_000)
        {
            return;
        }

        using var process = Process.GetCurrentProcess();
        long before = process.WorkingSet64;
        SessionState.TrimWorkingSet();
        _lastTrimTick = Environment.TickCount64;
        process.Refresh();
        Log.Debug("Idle trim: working set {Before:F0} MB -> {After:F0} MB", before / 1048576.0, process.WorkingSet64 / 1048576.0);
    }

    private static ModifierChord ChordFor(AppSettings settings) => ModifierChord.ForTrigger(settings.Trigger);

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void ConfigureLogging(bool console)
    {
        const string template = "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";
        // Daily files, also rolled at 10 MB, 7 kept. Debug detail in Debug builds or with --console.
        var config = new LoggerConfiguration()
            .WriteTo.File(
                Path.Combine(AppPaths.LogFolder, "dragconvert-.log"),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 7,
                outputTemplate: template);
#if DEBUG
        config = config.MinimumLevel.Debug();
#else
        config = console ? config.MinimumLevel.Debug() : config.MinimumLevel.Information();
#endif
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
