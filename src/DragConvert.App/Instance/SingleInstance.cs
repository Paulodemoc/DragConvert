using System.IO;
using System.IO.Pipes;
using DragConvert.Core.Instance;
using Serilog;

namespace DragConvert.App.Instance;

/// <summary>
/// One tray process per user session. Later launches (Send To, shortcuts) hand their files to it over a named pipe
/// and exit, so the exe doubles as the forwarder the design calls for.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private static readonly ILogger Logger = Log.ForContext<SingleInstance>();

    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    private static string PipeName => AppPaths.InstanceName;

    /// <summary>Returns the instance if this process is the first; null if another is already running.</summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, $@"Local\{AppPaths.InstanceName}", out bool createdNew);
        if (createdNew)
        {
            return new SingleInstance(mutex);
        }

        mutex.Dispose();
        return null;
    }

    /// <summary>Sends a message to the running instance. Waits briefly for its pipe in case it is still starting.</summary>
    public static bool Send(InstanceMessage message)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(TimeSpan.FromSeconds(5));
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(message.Serialize());
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Listens for messages from later launches; <paramref name="onMessage"/> runs on a background thread.</summary>
    public void Listen(Action<InstanceMessage> onMessage) => _ = Task.Run(async () =>
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_stop.Token);
                using var reader = new StreamReader(server);
                if (InstanceMessage.Parse(await reader.ReadLineAsync(_stop.Token)) is { } message)
                {
                    onMessage(message);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException ex)
            {
                Logger.Warning(ex, "Instance pipe error");
            }
        }
    });

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
