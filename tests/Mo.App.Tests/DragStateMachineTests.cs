using Mo.App.Shell.Logic;
using Mo.Engine;

namespace Mo.App.Tests;

public class DragStateMachineTests
{
    private readonly DragStateMachine _m = new(dragThreshold: 10);
    private long _now = 1000;

    private DragCommand? Step(bool down, int x = 0, int y = 0, WheelKind? wheel = null, bool esc = false, int advance = 20, bool own = false)
    {
        _now += advance;
        return _m.Update(new InputSnapshot(down, x, y, wheel, esc, _now, own));
    }

    /// <summary>Press at (100,100) and move far enough to count as a drag.</summary>
    private void StartDrag()
    {
        Step(true, 100, 100);
        Step(true, 130, 100);
    }

    [Fact]
    public void A_click_never_shows_anything()
    {
        Assert.Null(Step(true, 100, 100, WheelKind.Convert));
        Assert.Null(Step(true, 104, 103, WheelKind.Convert));
        Assert.Null(Step(false, 104, 103, WheelKind.Convert));
        Assert.Equal(DragPhase.Idle, _m.Phase);
    }

    [Fact]
    public void Dragging_without_a_key_shows_nothing()
    {
        StartDrag();
        Assert.Equal(DragPhase.Dragging, _m.Phase);
        Assert.Null(Step(true, 200, 200));
        Assert.Null(Step(false, 200, 200));
        Assert.Equal(DragPhase.Idle, _m.Phase);
    }

    [Fact]
    public void Holding_a_key_while_dragging_arms_the_overlay_at_the_cursor()
    {
        StartDrag();
        var command = Step(true, 150, 120, WheelKind.Tools);
        Assert.Equal(new DragCommand(DragCommandKind.Show, WheelKind.Tools, 150, 120), command);
        Assert.Equal(DragPhase.Armed, _m.Phase);
    }

    [Fact]
    public void Key_held_before_the_drag_shows_as_soon_as_the_threshold_is_passed()
    {
        Step(true, 100, 100, WheelKind.Convert);
        var command = Step(true, 115, 100, WheelKind.Convert);
        Assert.Equal(DragCommandKind.Show, command?.Kind);
    }

    [Fact]
    public void Armed_without_file_drag_gives_up_after_moving_and_stays_quiet_until_release()
    {
        StartDrag();
        Step(true, 130, 100, WheelKind.Convert);
        Assert.Null(Step(true, 150, 100, WheelKind.Convert, advance: 100));
        Assert.Equal(DragCommand.Hide, Step(true, 160, 100, WheelKind.Convert, advance: 500));
        Assert.Equal(DragPhase.Suppressed, _m.Phase);
        Assert.Null(Step(true, 200, 100, WheelKind.Convert));
        Step(false);
        Assert.Equal(DragPhase.Idle, _m.Phase);
    }

    [Fact]
    public void Armed_waits_while_the_cursor_rests_but_not_forever()
    {
        StartDrag();
        Step(true, 130, 100, WheelKind.Convert);
        Assert.Null(Step(true, 130, 100, WheelKind.Convert, advance: 1000));
        Assert.Equal(DragCommand.Hide, Step(true, 130, 100, WheelKind.Convert, advance: 2000));
    }

    [Fact]
    public void Releasing_the_key_before_files_arrive_hides_and_can_rearm()
    {
        StartDrag();
        Step(true, 130, 100, WheelKind.Convert);
        Assert.Equal(DragCommand.Hide, Step(true, 140, 100));
        Assert.Equal(DragPhase.Dragging, _m.Phase);
        Assert.Equal(DragCommandKind.Show, Step(true, 150, 100, WheelKind.Actions)?.Kind);
    }

    [Fact]
    public void File_drag_opens_the_wheel_and_other_keys_switch_it()
    {
        StartDrag();
        Step(true, 130, 100, WheelKind.Convert);
        _m.FileDragEntered();
        Assert.Equal(DragPhase.Open, _m.Phase);

        Assert.Equal(new DragCommand(DragCommandKind.Switch, WheelKind.Actions), Step(true, 135, 100, WheelKind.Actions));
        Assert.Equal(WheelKind.Actions, _m.Wheel);
        // Letting go of the key keeps the wheel open so the user can aim freely.
        Assert.Null(Step(true, 140, 100, null, advance: 5000));
        Assert.Equal(DragPhase.Open, _m.Phase);
    }

    [Fact]
    public void Drop_on_the_wheel_ends_the_gesture()
    {
        StartDrag();
        Step(true, 130, 100, WheelKind.Convert);
        _m.FileDragEntered();
        Step(false, 130, 100, WheelKind.Convert);
        Assert.Equal(DragPhase.Releasing, _m.Phase);
        Assert.Equal(DragCommand.Hide, _m.Dropped());
        Assert.Equal(DragPhase.Suppressed, _m.Phase);
        Step(false);
        Assert.Equal(DragPhase.Idle, _m.Phase);
    }

    [Fact]
    public void Release_without_drop_hides_after_the_grace_period()
    {
        StartDrag();
        Step(true, 130, 100, WheelKind.Convert);
        _m.FileDragEntered();
        Assert.Null(Step(false));
        Assert.Null(Step(false, advance: 100));
        Assert.Equal(DragCommand.Hide, Step(false, advance: 300));
        Assert.Equal(DragPhase.Idle, _m.Phase);
    }

    [Fact]
    public void Escape_cancels_an_open_wheel()
    {
        StartDrag();
        Step(true, 130, 100, WheelKind.Convert);
        _m.FileDragEntered();
        Assert.Equal(DragCommand.Hide, Step(true, 130, 100, WheelKind.Convert, esc: true));
        Assert.Equal(DragPhase.Suppressed, _m.Phase);
    }

    [Fact]
    public void Non_file_drags_are_left_alone()
    {
        StartDrag();
        Step(true, 130, 100, WheelKind.Convert);
        Assert.Equal(DragCommand.Hide, _m.NotAFileDrag());
        Assert.Equal(DragPhase.Suppressed, _m.Phase);
        Assert.Null(Step(true, 200, 100, WheelKind.Tools));
    }

    [Fact]
    public void Drags_that_start_on_our_own_windows_are_ignored()
    {
        Step(true, 100, 100, own: true);
        Assert.Equal(DragPhase.Suppressed, _m.Phase);
        Assert.Null(Step(true, 200, 100, WheelKind.Convert));
    }

    [Fact]
    public void Overlay_events_outside_a_gesture_do_nothing()
    {
        Assert.Null(_m.Dropped());
        Assert.Null(_m.NotAFileDrag());
        _m.FileDragEntered();
        Assert.Equal(DragPhase.Idle, _m.Phase);
    }
}

public class WheelKeyResolverTests
{
    private static WheelKind? Resolve(bool shift, bool ctrl, bool alt, WheelKind? current = null) =>
        WheelKeyResolver.Resolve(shift, ctrl, alt, ModifierKey.Shift, ModifierKey.Ctrl, ModifierKey.Alt, current);

    [Fact]
    public void Default_keys_map_to_their_wheels()
    {
        Assert.Null(Resolve(false, false, false));
        Assert.Equal(WheelKind.Convert, Resolve(true, false, false));
        Assert.Equal(WheelKind.Tools, Resolve(false, true, false));
        Assert.Equal(WheelKind.Actions, Resolve(false, false, true));
    }

    [Fact]
    public void While_two_keys_overlap_the_current_wheel_stays()
    {
        Assert.Equal(WheelKind.Convert, Resolve(true, true, false, WheelKind.Convert));
        Assert.Equal(WheelKind.Tools, Resolve(true, true, false, WheelKind.Actions));
    }

    [Fact]
    public void Custom_key_assignment_is_respected()
    {
        Assert.Equal(WheelKind.Convert,
            WheelKeyResolver.Resolve(false, false, true, ModifierKey.Alt, ModifierKey.Shift, ModifierKey.Ctrl));
    }
}
