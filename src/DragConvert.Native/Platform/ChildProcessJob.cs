using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.JobObjects;

namespace DragConvert.Native.Platform;

/// <summary>
/// A Windows job object that kills every process assigned to it when DragConvert exits, even if it crashes, so no
/// ffmpeg.exe keeps running orphaned in the background.
/// </summary>
public sealed class ChildProcessJob : IDisposable
{
    private readonly SafeHandle _job;

    public unsafe ChildProcessJob()
    {
        _job = PInvoke.CreateJobObject(null, (string?)null);
        if (_job.IsInvalid)
        {
            throw new InvalidOperationException($"CreateJobObject failed (error {Marshal.GetLastPInvokeError()}).");
        }

        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        if (!PInvoke.SetInformationJobObject((HANDLE)_job.DangerousGetHandle(), JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation, &limits, (uint)sizeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION)))
        {
            throw new InvalidOperationException($"SetInformationJobObject failed (error {Marshal.GetLastPInvokeError()}).");
        }
    }

    /// <summary>Ties a started process's lifetime to this app. Returns false if Windows refused (it then runs unattached).</summary>
    public bool Assign(Process process)
    {
        try
        {
            return PInvoke.AssignProcessToJobObject(_job, process.SafeHandle);
        }
        catch (InvalidOperationException)
        {
            return false; // already exited
        }
    }

    public void Dispose() => _job.Dispose();
}
