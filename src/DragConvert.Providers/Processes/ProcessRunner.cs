using System.Diagnostics;

namespace DragConvert.Providers.Processes;

public sealed record ProcessResult(int ExitCode, IReadOnlyList<string> StandardErrorTail);

/// <summary>
/// Runs a command-line engine as a child process with a timeout, cancellation and stderr capture.
/// Cancelling or timing out kills the whole process tree.
/// </summary>
public static class ProcessRunner
{
    private const int StderrTailLines = 40;

    public static async Task<ProcessResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        Action<string>? onStandardOutputLine = null,
        Action<string>? onStandardErrorLine = null,
        CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var stderrTail = new Queue<string>(StderrTailLines);
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onStandardOutputLine?.Invoke(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            onStandardErrorLine?.Invoke(e.Data);
            lock (stderrTail)
            {
                if (stderrTail.Count == StderrTailLines)
                {
                    stderrTail.Dequeue();
                }

                stderrTail.Enqueue(e.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Could not start {Path.GetFileName(executable)}.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"{Path.GetFileNameWithoutExtension(executable)} did not finish within {timeout.TotalMinutes:F0} minutes.");
        }

        lock (stderrTail)
        {
            return new ProcessResult(process.ExitCode, stderrTail.ToArray());
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }
}
