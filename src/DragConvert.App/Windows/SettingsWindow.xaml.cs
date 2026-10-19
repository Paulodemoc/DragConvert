using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using DragConvert.App.Conversion;
using DragConvert.App.Theme;
using DragConvert.Core.Input;
using DragConvert.Core.Settings;
using Microsoft.Win32;

namespace DragConvert.App.Windows;

/// <summary>Settings are saved as soon as they change; <see cref="SettingsChanged"/> lets the app apply them live.</summary>
internal sealed partial class SettingsWindow : Window
{
    private bool _loading;

    public SettingsWindow()
    {
        InitializeComponent();
        Icon = AppIcon.Window();
        VersionText.Text = $"DragConvert {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}";
    }

    public event Action<AppSettings>? SettingsChanged;

    public AppSettings Current { get; private set; } = new();

    public void Present(AppSettings settings)
    {
        _loading = true;
        Current = settings;
        Select(TriggerBox, settings.Trigger.ToString());
        Select(RendererBox, settings.Renderer);
        SameFolderRadio.IsChecked = settings.Output == OutputLocation.SameFolder;
        FixedFolderRadio.IsChecked = settings.Output == OutputLocation.FixedFolder;
        FolderBox.Text = settings.OutputFolder ?? string.Empty;
        StartupCheck.IsChecked = settings.StartWithWindows;
        SendToCheck.IsChecked = settings.SendToMenu;
        _loading = false;

        Show();
        Activate();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private static void Select(ComboBox box, string tag) =>
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => string.Equals(i.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            ?? box.Items[0];

    private static string SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || !IsInitialized)
        {
            return;
        }

        bool fixedFolder = FixedFolderRadio.IsChecked == true;
        if (fixedFolder && string.IsNullOrWhiteSpace(FolderBox.Text) && !BrowseForFolder())
        {
            _loading = true;
            SameFolderRadio.IsChecked = true;
            _loading = false;
            fixedFolder = false;
        }

        Current = Current with
        {
            Trigger = Enum.TryParse(SelectedTag(TriggerBox), out Modifiers trigger) ? trigger : Current.Trigger,
            Renderer = SelectedTag(RendererBox) is { Length: > 0 } renderer ? renderer : Current.Renderer,
            Output = fixedFolder ? OutputLocation.FixedFolder : OutputLocation.SameFolder,
            OutputFolder = string.IsNullOrWhiteSpace(FolderBox.Text) ? null : FolderBox.Text,
            StartWithWindows = StartupCheck.IsChecked == true,
            SendToMenu = SendToCheck.IsChecked == true,
        };
        SettingsChanged?.Invoke(Current);
    }

    private bool BrowseForFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Save converted files to", Multiselect = false };
        if (!string.IsNullOrWhiteSpace(FolderBox.Text))
        {
            dialog.InitialDirectory = FolderBox.Text;
        }

        if (dialog.ShowDialog(this) != true)
        {
            return false;
        }

        FolderBox.Text = dialog.FolderName;
        return true;
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        if (BrowseForFolder())
        {
            _loading = true;
            FixedFolderRadio.IsChecked = true;
            _loading = false;
            OnChanged(sender, e);
        }
    }

    private void OnLogsClick(object sender, RoutedEventArgs e) => Shell.Open(AppPaths.LogFolder);

    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();
}
