using System.Security;
using System.Text;
using System.Windows.Threading;
using DragConvert.Core.Conversion;
using DragConvert.Native.Shell;
using Serilog;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace DragConvert.App.Notifications;

/// <summary>A notification button: its label, and the verb and argument passed back when it's clicked.</summary>
internal sealed record NotificationAction(string Label, string Verb, string Argument = "");

/// <summary>
/// Windows toast notifications for results, progress, errors and confirmations. Button clicks come back through
/// <see cref="ActionInvoked"/> while the app is running. The unpackaged build registers its own AppUserModelID.
/// </summary>
internal sealed class NotificationService
{
    private const string Group = "dragconvert";
    private static readonly ILogger Logger = Log.ForContext<NotificationService>();

    private readonly Dispatcher _dispatcher;
    private readonly ToastNotifier? _notifier;

    // Toasts stay referenced until they're dismissed or clicked, so their event handlers stay alive.
    private readonly HashSet<ToastNotification> _live = [];

    public NotificationService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        try
        {
            ShellIntegration.RegisterAppIdentity(AppPaths.AppUserModelId, "DragConvert", iconPath: null);
            _notifier = ToastNotificationManager.CreateToastNotifier(AppPaths.AppUserModelId);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Notifications unavailable");
        }
    }

    /// <summary>Raised on the UI thread with the verb and argument of the clicked button (or "show" for the toast body).</summary>
    public event Action<string, string>? ActionInvoked;

    public void Show(NotificationText text, IReadOnlyList<NotificationAction>? actions = null, string? tag = null)
    {
        Logger.Information("Notification: {Title} | {Body}", text.Title, text.Body.Replace(Environment.NewLine, " | ", StringComparison.Ordinal));
        Send(Build(text, actions ?? [], progressTitle: null), tag);
    }

    /// <summary>A notification with a progress bar that <see cref="UpdateProgress"/> keeps current. Replace it by showing a result with the same tag.</summary>
    public void ShowProgress(string tag, string title, string progressTitle, IReadOnlyList<NotificationAction> actions)
    {
        ToastNotification toast = Build(new NotificationText(title, string.Empty), actions, progressTitle);
        toast.Data = ProgressData(0, "Starting…", 1);
        Send(toast, tag);
    }

    public void UpdateProgress(string tag, double value, string status, uint sequence)
    {
        try
        {
            _notifier?.Update(ProgressData(value, status, sequence), tag, Group);
        }
        catch (Exception ex)
        {
            Logger.Debug(ex, "Progress update failed");
        }
    }

    /// <summary>Asks a yes/no question in a notification that stays until answered. Dismissing it counts as no.</summary>
    public Task<bool> ConfirmAsync(NotificationText text, string confirmLabel)
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_notifier is null)
        {
            answer.SetResult(true);
            return answer.Task;
        }

        ToastNotification toast = Build(text, [new NotificationAction(confirmLabel, "confirm"), new NotificationAction("Cancel", "cancel")], progressTitle: null, reminder: true);
        toast.Activated += (_, args) => answer.TrySetResult(args is ToastActivatedEventArgs a && a.Arguments.StartsWith("confirm", StringComparison.Ordinal));
        toast.Dismissed += (_, _) => answer.TrySetResult(false);
        toast.Failed += (_, _) => answer.TrySetResult(false);
        Send(toast, tag: null, raiseActions: false);
        return answer.Task;
    }

    private static NotificationData ProgressData(double value, string status, uint sequence) => new(
        new Dictionary<string, string>
        {
            ["progressValue"] = value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            ["progressValueString"] = $"{value * 100:0}%",
            ["progressStatus"] = status,
        },
        sequence);

    private static ToastNotification Build(NotificationText text, IReadOnlyList<NotificationAction> actions, string? progressTitle, bool reminder = false)
    {
        var xml = new StringBuilder();
        xml.Append(reminder ? "<toast launch=\"show\" scenario=\"reminder\">" : "<toast launch=\"show\">");
        xml.Append("<visual><binding template=\"ToastGeneric\">");
        xml.Append("<text>").Append(Escape(text.Title)).Append("</text>");
        foreach (string line in text.Body.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).Take(2))
        {
            xml.Append("<text>").Append(Escape(line)).Append("</text>");
        }

        if (progressTitle is not null)
        {
            xml.Append("<progress title=\"").Append(Escape(progressTitle))
                .Append("\" value=\"{progressValue}\" valueStringOverride=\"{progressValueString}\" status=\"{progressStatus}\"/>");
        }

        xml.Append("</binding></visual>");
        if (actions.Count > 0)
        {
            xml.Append("<actions>");
            foreach (NotificationAction action in actions.Take(5))
            {
                xml.Append("<action activationType=\"foreground\" content=\"").Append(Escape(action.Label))
                    .Append("\" arguments=\"").Append(Escape($"{action.Verb}|{action.Argument}")).Append("\"/>");
            }

            xml.Append("</actions>");
        }

        xml.Append("</toast>");
        var document = new XmlDocument();
        document.LoadXml(xml.ToString());
        return new ToastNotification(document);
    }

    private static string Escape(string value) => SecurityElement.Escape(value) ?? string.Empty;

    private void Send(ToastNotification toast, string? tag, bool raiseActions = true)
    {
        if (_notifier is null)
        {
            return;
        }

        if (tag is not null)
        {
            toast.Tag = tag;
            toast.Group = Group;
        }

        if (raiseActions)
        {
            toast.Activated += (_, args) =>
            {
                string arguments = (args as ToastActivatedEventArgs)?.Arguments ?? "show";
                int bar = arguments.IndexOf('|', StringComparison.Ordinal);
                string verb = bar < 0 ? arguments : arguments[..bar];
                string argument = bar < 0 ? string.Empty : arguments[(bar + 1)..];
                _dispatcher.InvokeAsync(() => ActionInvoked?.Invoke(verb, argument));
                Forget(toast);
            };
        }

        toast.Dismissed += (_, _) => Forget(toast);
        toast.Failed += (_, e) =>
        {
            Logger.Warning(e.ErrorCode, "Notification failed");
            Forget(toast);
        };

        lock (_live)
        {
            _live.Add(toast);
        }

        try
        {
            _notifier.Show(toast);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Showing a notification failed");
            Forget(toast);
        }
    }

    private void Forget(ToastNotification toast)
    {
        lock (_live)
        {
            _live.Remove(toast);
        }
    }
}
