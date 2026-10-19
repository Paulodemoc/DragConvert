using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace DragConvert.Native.Input;

/// <summary>A drag transition seen by the hook, with screen coordinates and a <see cref="Stopwatch"/> timestamp.</summary>
public readonly record struct DragSignal(DragTransition Transition, int X, int Y, long Timestamp);

/// <summary>
/// Runs a WH_MOUSE_LL hook on a dedicated thread with its own message loop.
/// The callback only feeds <see cref="DragDetector"/> and calls the sink on transitions; the sink must just post
/// to another thread. Windows silently removes hooks whose callbacks run past the system timeout.
/// </summary>
public sealed class MouseHookThread : IDisposable
{
    private readonly Action<DragSignal> _sink;
    private readonly DragDetector _detector;
    private readonly HOOKPROC _hookProc;
    private readonly ManualResetEventSlim _ready = new();
    private Thread? _thread;
    private uint _threadId;
    private long _lastEventTick = Environment.TickCount64;

    public MouseHookThread(Action<DragSignal> sink)
    {
        _sink = sink;
        _detector = new DragDetector(DragMetrics.ThresholdX, DragMetrics.ThresholdY);
        // Held in a field so the GC never collects the delegate while Windows still calls it.
        _hookProc = HookProc;
    }

    public bool IsInstalled { get; private set; }

    /// <summary>Environment.TickCount64 of the last mouse event the hook saw; a stale value means Windows dropped it.</summary>
    public long LastEventTick => Volatile.Read(ref _lastEventTick);

    /// <summary>Win32 error code if installing the hook failed, otherwise 0.</summary>
    public int InstallError { get; private set; }

    public void Start()
    {
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "DragConvert mouse hook",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
        _ready.Wait();
    }

    public void Dispose()
    {
        if (_thread is { IsAlive: true })
        {
            PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, default, default);
            _thread.Join(TimeSpan.FromSeconds(2));
        }

        _ready.Dispose();
    }

    private void Run()
    {
        _threadId = PInvoke.GetCurrentThreadId();
        using var module = PInvoke.GetModuleHandle((string?)null);
        using var hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, _hookProc, module, 0);
        IsInstalled = !hook.IsInvalid;
        InstallError = IsInstalled ? 0 : System.Runtime.InteropServices.Marshal.GetLastPInvokeError();
        _ready.Set();
        if (!IsInstalled)
        {
            return;
        }

        // Low-level hooks are called from this thread's message loop; nothing needs dispatching.
        while (PInvoke.GetMessage(out _, HWND.Null, 0, 0) > 0)
        {
        }
    }

    private unsafe LRESULT HookProc(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code >= 0)
        {
            Volatile.Write(ref _lastEventTick, Environment.TickCount64);
            var info = (MSLLHOOKSTRUCT*)lParam.Value;
            DragTransition transition = (uint)wParam.Value switch
            {
                PInvoke.WM_LBUTTONDOWN => Down(info->pt.X, info->pt.Y),
                PInvoke.WM_MOUSEMOVE => _detector.Move(info->pt.X, info->pt.Y),
                PInvoke.WM_LBUTTONUP => _detector.ButtonUp(),
                _ => DragTransition.None,
            };

            if (transition != DragTransition.None)
            {
                try
                {
                    _sink(new DragSignal(transition, info->pt.X, info->pt.Y, Stopwatch.GetTimestamp()));
                }
                catch (Exception)
                {
                    // Never let an exception escape into the hook chain.
                }
            }
        }

        return PInvoke.CallNextHookEx(null, code, wParam, lParam);
    }

    private DragTransition Down(int x, int y)
    {
        _detector.ButtonDown(x, y);
        return DragTransition.None;
    }
}
