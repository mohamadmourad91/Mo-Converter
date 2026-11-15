using System.Diagnostics;
using System.Windows.Threading;
using Mo.App.Shell.Logic;
using Mo.Engine;

namespace Mo.App.Shell;

/// <summary>
/// Notices "the user is dragging something and holds a wheel key" anywhere on the desktop.
///
/// How: a 20 ms timer polls the global button/key state (GetAsyncKeyState) and the cursor position and feeds
/// them into <see cref="DragStateMachine"/>. Polling was chosen over a low-level mouse hook (WH_MOUSE_LL)
/// because a hook runs on our UI thread for every mouse event in the system: if the UI thread is ever busy,
/// the whole desktop's mouse stutters and Windows silently removes the hook. Polling costs a few
/// microseconds per tick and can never slow down other apps.
///
/// The watcher cannot tell a file drag from a rubber-band selection or a window move; the overlay settles
/// that by waiting for an OLE DragEnter with file data (see <see cref="WheelController"/>).
/// </summary>
internal sealed class DragWatcher : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Func<AppSettings> _settings;

    public DragWatcher(Func<AppSettings> settings)
    {
        _settings = settings;
        // Our threshold is twice the system drag distance so plain clicks never arm the overlay;
        // Explorer has already started its OLE drag by then.
        var threshold = Math.Max(Native.GetSystemMetrics(Native.SM_CXDRAG), Native.GetSystemMetrics(Native.SM_CYDRAG)) * 2;
        Machine = new DragStateMachine(Math.Max(threshold, 8));
        // Asking for 20 ms gave a tick every 31 ms (the system timer runs in 15.6 ms steps), and arming needs
        // two ticks. 10 ms lands on every step. The idle tick returns after one GetAsyncKeyState call.
        _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(10) };
        _timer.Tick += (_, _) => Poll();
    }

    public DragStateMachine Machine { get; }

    /// <summary>Raised on the UI thread with each command the state machine issues.</summary>
    public event Action<DragCommand>? Command;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    /// <summary>Forwards a command produced outside the polling loop (overlay events).</summary>
    public void Raise(DragCommand? command)
    {
        if (command is { } c) Command?.Invoke(c);
    }

    private void Poll()
    {
        var down = Native.PrimaryButtonDown();
        // Idle and nothing pressed is by far the common case: skip the remaining calls.
        if (!down && Machine.Phase == DragPhase.Idle) return;

        var cursor = ScreenPlacement.Cursor();
        var settings = _settings();
        var wheel = WheelKeyResolver.Resolve(
            Native.IsDown(Native.VK_SHIFT), Native.IsDown(Native.VK_CONTROL), Native.IsDown(Native.VK_MENU),
            settings.ConvertKey, settings.ToolsKey, settings.ActionsKey,
            Machine.OverlayActive ? Machine.Wheel : null);

        // Only checked at the moment of the press: a drag that starts on our own window (settings, toast) is ignored.
        var overOwn = Machine.Phase == DragPhase.Idle && down && Native.IsOwnWindowAt(cursor);

        var snapshot = new InputSnapshot(down, cursor.X, cursor.Y, wheel, Native.IsDown(Native.VK_ESCAPE), _clock.ElapsedMilliseconds, overOwn);
        Raise(Machine.Update(snapshot));
    }

    public void Dispose() => _timer.Stop();
}
