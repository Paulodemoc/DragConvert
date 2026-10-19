using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DragConvert.App.Conversion;
using DragConvert.App.Theme;
using DragConvert.Core.Conversion;
using Serilog;

namespace DragConvert.App.Windows;

internal sealed record TargetChoice(MediaFormat Target, string Label, string? Detail)
{
    public string Glyph => OverlayTheme.Glyph(Target.Category);

    public string AccessibleName => Detail is null ? $"Convert to {Label}" : $"Convert to {Label}, {Detail} files";
}

/// <summary>
/// The tray window: a drop zone, the picker for files handed over by Send To or a drop (keyboard operable), and the
/// recent conversions list. Closing hides it; the tray process keeps running.
/// </summary>
internal sealed partial class PickerWindow : Window
{
    private static readonly ILogger Logger = Log.ForContext<PickerWindow>();

    private readonly ConversionCoordinator _coordinator;
    private IReadOnlyList<string> _paths = [];
    private DropSet? _set;
    private CancellationTokenSource? _loading;

    public PickerWindow(ConversionCoordinator coordinator)
    {
        _coordinator = coordinator;
        InitializeComponent();
        Icon = AppIcon.Window();
        RecentList.ItemsSource = coordinator.Recent;
        coordinator.Recent.CollectionChanged += (_, _) => UpdateRecentVisibility();
        UpdateRecentVisibility();
    }

    /// <summary>Shows the window near the tray, empty (drop zone and recent list).</summary>
    public void ShowEmpty()
    {
        Clear();
        Present();
    }

    /// <summary>Shows the window with the formats these files can become.</summary>
    public async void ShowFor(IReadOnlyList<string> paths)
    {
        Present();
        _loading?.Cancel();
        _loading = new CancellationTokenSource();
        CancellationToken token = _loading.Token;
        _paths = paths;
        DropTitle.Text = "Reading files…";
        DropDetail.Text = string.Empty;
        TargetsPanel.Visibility = Visibility.Collapsed;

        try
        {
            DropSet set = await ConversionCoordinator.ExpandAsync(paths, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            _set = set;
            IReadOnlyList<TargetOption> options = _coordinator.Catalog.TargetOptionsFor(set.Files);
            DropTitle.Text = set.Files.Count == 1 ? Path.GetFileName(set.Files[0]) : $"{set.Files.Count:N0} files";
            DropDetail.Text = set.FolderCount > 0 ? $"From {set.FolderCount} folder{(set.FolderCount == 1 ? string.Empty : "s")}" : string.Empty;

            if (options.Count == 0)
            {
                DropDetail.Text = set.Files.Count == 0 ? "There are no files here." : "None of these can be converted.";
                return;
            }

            Targets.ItemsSource = options
                .Select(o => new TargetChoice(o.Target, o.Target.DisplayName, o.AppliesToAll ? null : $"{o.FileCount} of {o.TotalFiles}"))
                .ToList();
            TargetsPanel.Visibility = Visibility.Visible;

            // Keyboard: focus the first format so Enter converts and Tab/arrows move between them.
            _ = Dispatcher.BeginInvoke(
                () => (Targets.ItemContainerGenerator.ContainerFromIndex(0) as UIElement)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warning(ex, "Reading files for the picker failed");
            DropTitle.Text = "Couldn't read the files";
            DropDetail.Text = ex.Message;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Keep the window for next time; the tray icon brings it back.
        e.Cancel = true;
        Hide();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Hide();
        }
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            e.Effects = DragDropEffects.Copy;
            ShowFor(paths);
        }

        e.Handled = true;
    }

    private void Present()
    {
        if (!IsVisible)
        {
            // Bottom-right of the work area, next to the notification area.
            Rect work = SystemParameters.WorkArea;
            Show();
            UpdateLayout();
            Left = work.Right - ActualWidth - 12;
            Top = work.Bottom - ActualHeight - 12;
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private void Clear()
    {
        _loading?.Cancel();
        _paths = [];
        _set = null;
        Targets.ItemsSource = null;
        TargetsPanel.Visibility = Visibility.Collapsed;
        DropTitle.Text = "Drop files or folders here";
        DropDetail.Text = "Or drag with Ctrl+Shift held anywhere to convert in place.";
    }

    private void UpdateRecentVisibility() =>
        RecentEmpty.Visibility = _coordinator.Recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnTargetClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TargetChoice choice })
        {
            _ = _coordinator.ConvertAsync(_paths, choice.Target, _set);
            Clear();
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e) => Clear();

    private void OnOpenRecentClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RecentConversion recent } && File.Exists(recent.OutputPath))
        {
            Shell.Open(recent.OutputPath);
        }
    }

    private void OnRevealRecentClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RecentConversion recent } && File.Exists(recent.OutputPath))
        {
            Shell.Reveal(recent.OutputPath);
        }
    }
}
