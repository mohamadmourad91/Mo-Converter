using Mo.Engine;

namespace Mo.App.Shell.Logic;

/// <summary>Where the watcher believes the user is in a drag gesture.</summary>
public enum DragPhase
{
    /// <summary>No button held.</summary>
    Idle,
    /// <summary>Button went down; not moved far enough to be a drag yet.</summary>
    Pressed,
    /// <summary>A drag is under way but no wheel key is held.</summary>
    Dragging,
    /// <summary>The (still invisible) overlay is under the cursor, waiting for a file drag to enter it.</summary>
    Armed,
    /// <summary>Files entered the overlay; the wheel is open.</summary>
    Open,
    /// <summary>The button was released over the open wheel; waiting briefly for the OLE drop to arrive.</summary>
    Releasing,
    /// <summary>This gesture is ignored until the button is released (cancelled, not a file drag, or started on our own window).</summary>
    Suppressed,
}

/// <summary>One poll of the global input state. Coordinates are physical screen pixels.</summary>
public readonly record struct InputSnapshot(
    bool ButtonDown,
    int X,
    int Y,
    WheelKind? Wheel,
    bool Escape,
    long TimeMs,
    bool OverOwnWindow = false);

public enum DragCommandKind { Show, Switch, Hide }

/// <summary>What the overlay should do. X/Y are only meaningful for <see cref="DragCommandKind.Show"/>.</summary>
public readonly record struct DragCommand(DragCommandKind Kind, WheelKind Wheel = default, int X = 0, int Y = 0)
{
    public static DragCommand Hide => new(DragCommandKind.Hide);
}

/// <summary>
/// Pure state machine behind the drag watcher. It is fed a snapshot of the mouse/keyboard every few
/// milliseconds plus two events from the overlay (file drag entered, drop handled), and answers with
/// at most one command per step. It never touches Win32, so every transition is unit-testable.
/// </summary>
public sealed class DragStateMachine
{
    private readonly int _threshold;
    private int _pressX, _pressY, _armX, _armY;
    private long _armedAt, _releasedAt;

    /// <param name="dragThreshold">Pixels the cursor must travel with the button held before it counts as a drag.</param>
    public DragStateMachine(int dragThreshold) => _threshold = Math.Max(1, dragThreshold);

    /// <summary>Give up on an armed overlay after this long once the cursor has moved (it was not a file drag).</summary>
    public int ArmTimeoutMs { get; init; } = 500;

    /// <summary>Give up on an armed overlay after this long even if the cursor did not move.</summary>
    public int ArmHardTimeoutMs { get; init; } = 2500;

    /// <summary>Cursor travel (pixels) since arming that makes <see cref="ArmTimeoutMs"/> apply.</summary>
    public int ArmMoveThreshold { get; init; } = 6;

    /// <summary>How long to wait for the OLE Drop after the button went up over the wheel.</summary>
    public int ReleaseGraceMs { get; init; } = 300;

    public DragPhase Phase { get; private set; } = DragPhase.Idle;

    /// <summary>The wheel currently shown or armed.</summary>
    public WheelKind Wheel { get; private set; }

    public bool OverlayActive => Phase is DragPhase.Armed or DragPhase.Open or DragPhase.Releasing;

    public DragCommand? Update(InputSnapshot s)
    {
        switch (Phase)
        {
            case DragPhase.Idle:
                if (!s.ButtonDown) return null;
                (_pressX, _pressY) = (s.X, s.Y);
                Phase = s.OverOwnWindow ? DragPhase.Suppressed : DragPhase.Pressed;
                return null;

            case DragPhase.Pressed:
                if (!s.ButtonDown) { Phase = DragPhase.Idle; return null; }
                if (Math.Abs(s.X - _pressX) < _threshold && Math.Abs(s.Y - _pressY) < _threshold) return null;
                Phase = DragPhase.Dragging;
                return TryArm(s);

            case DragPhase.Dragging:
                if (!s.ButtonDown) { Phase = DragPhase.Idle; return null; }
                if (s.Escape) { Phase = DragPhase.Suppressed; return null; }
                return TryArm(s);

            case DragPhase.Armed:
                if (!s.ButtonDown) { Phase = DragPhase.Idle; return DragCommand.Hide; }
                if (s.Escape) { Phase = DragPhase.Suppressed; return DragCommand.Hide; }
                if (s.Wheel is null) { Phase = DragPhase.Dragging; return DragCommand.Hide; }
                var elapsed = s.TimeMs - _armedAt;
                var moved = Math.Abs(s.X - _armX) >= ArmMoveThreshold || Math.Abs(s.Y - _armY) >= ArmMoveThreshold;
                if (elapsed >= ArmHardTimeoutMs || (moved && elapsed >= ArmTimeoutMs))
                {
                    Phase = DragPhase.Suppressed;
                    return DragCommand.Hide;
                }
                return SwitchIfChanged(s.Wheel.Value);

            case DragPhase.Open:
                if (!s.ButtonDown) { Phase = DragPhase.Releasing; _releasedAt = s.TimeMs; return null; }
                if (s.Escape) { Phase = DragPhase.Suppressed; return DragCommand.Hide; }
                // Releasing the key keeps the wheel open, so the user can aim without holding it;
                // pressing another wheel key switches wheels.
                return s.Wheel is { } wheel ? SwitchIfChanged(wheel) : null;

            case DragPhase.Releasing:
                if (s.ButtonDown || s.TimeMs - _releasedAt >= ReleaseGraceMs)
                {
                    Phase = DragPhase.Idle;
                    return DragCommand.Hide;
                }
                return null;

            case DragPhase.Suppressed:
                if (!s.ButtonDown) Phase = DragPhase.Idle;
                return null;
        }
        return null;
    }

    /// <summary>The overlay received a drag that carries files.</summary>
    public void FileDragEntered()
    {
        if (Phase == DragPhase.Armed) Phase = DragPhase.Open;
    }

    /// <summary>The overlay received a drag without files (text, browser links…): step aside for this gesture.</summary>
    public DragCommand? NotAFileDrag()
    {
        if (!OverlayActive) return null;
        Phase = DragPhase.Suppressed;
        return DragCommand.Hide;
    }

    /// <summary>The OLE drop arrived on the overlay (on a petal or a cancel zone). The gesture is over.</summary>
    public DragCommand? Dropped()
    {
        if (!OverlayActive) return null;
        Phase = DragPhase.Suppressed;
        return DragCommand.Hide;
    }

    private DragCommand? TryArm(InputSnapshot s)
    {
        if (s.Wheel is not { } wheel) return null;
        Phase = DragPhase.Armed;
        Wheel = wheel;
        (_armX, _armY, _armedAt) = (s.X, s.Y, s.TimeMs);
        return new DragCommand(DragCommandKind.Show, wheel, s.X, s.Y);
    }

    private DragCommand? SwitchIfChanged(WheelKind wheel)
    {
        if (wheel == Wheel) return null;
        Wheel = wheel;
        return new DragCommand(DragCommandKind.Switch, wheel);
    }
}

/// <summary>Turns the held modifier keys into a wheel, using the keys chosen in settings.</summary>
public static class WheelKeyResolver
{
    public static WheelKind? Resolve(bool shift, bool ctrl, bool alt, ModifierKey convertKey, ModifierKey toolsKey,
                                     ModifierKey actionsKey, WheelKind? current = null)
    {
        bool Held(ModifierKey key) => key switch
        {
            ModifierKey.Shift => shift,
            ModifierKey.Ctrl => ctrl,
            ModifierKey.Alt => alt,
            _ => false,
        };

        var held = new List<WheelKind>(3);
        if (Held(convertKey)) held.Add(WheelKind.Convert);
        if (Held(toolsKey)) held.Add(WheelKind.Tools);
        if (Held(actionsKey)) held.Add(WheelKind.Actions);

        return held.Count switch
        {
            0 => null,
            1 => held[0],
            // Several keys at once (e.g. while switching from one to another): keep the current wheel if its key
            // is still among them, otherwise take the newly added one.
            _ => current is { } c && held.Contains(c) ? c : held[^1],
        };
    }
}
