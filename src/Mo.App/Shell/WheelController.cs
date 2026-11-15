using System.Globalization;
using System.Windows.Threading;
using Mo.App.Services;
using Mo.App.Shell.Logic;
using Mo.Engine;

namespace Mo.App.Shell;

/// <summary>
/// Glue between the drag watcher, the overlay window and the operation registry:
/// watcher says "show / switch / hide", overlay says "files entered / not files / dropped",
/// and the controller decides which petals to show and what to run.
/// </summary>
internal sealed class WheelController : IDisposable
{
    private readonly DragWatcher _watcher;
    private readonly OperationRegistry _registry;
    private readonly OperationRunner _runner;
    private readonly WheelOverlay _overlay = new();
    private InputSet? _input;

    public WheelController(DragWatcher watcher, OperationRegistry registry, OperationRunner runner)
    {
        _watcher = watcher;
        _registry = registry;
        _runner = runner;
        _watcher.Command += OnCommand;
        _overlay.FilesEntered += OnFilesEntered;
        _overlay.NotAFileDrag += () => _watcher.Raise(_watcher.Machine.NotAFileDrag());
        _overlay.Dropped += OnDropped;
    }

    private void OnCommand(DragCommand command)
    {
        switch (command.Kind)
        {
            case DragCommandKind.Show:
                _input = null;
                _overlay.Arm(new Native.POINT(command.X, command.Y));
                break;
            case DragCommandKind.Switch:
                if (_input is not null) Present(command.Wheel);
                break;
            case DragCommandKind.Hide:
                _input = null;
                _overlay.Retract();
                break;
        }
    }

    /// <summary>
    /// Runs every wheel's matching once and draws the wheel off-screen, so the first drag after a start
    /// is as quick as later ones. Does nothing while a gesture is under way.
    /// </summary>
    public void Prewarm()
    {
        if (_watcher.Machine.Phase != DragPhase.Idle) return;
        var sample = new InputSet([@"C:\Mo Converter\prewarm.jpg"]);
        foreach (var wheel in Enum.GetValues<WheelKind>()) Match(wheel, sample);
        _overlay.Prewarm(Match(WheelKind.Convert, sample).Select(ToPetal).ToList());
        // Hide again after one render, unless a real drag has taken the overlay over in the meantime.
        _overlay.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            if (!_watcher.Machine.OverlayActive) _overlay.Retract();
        });
    }

    private void OnFilesEntered(string[] paths)
    {
        if (_watcher.Machine.Phase != DragPhase.Armed) return;
        _watcher.Machine.FileDragEntered();
        _input = new InputSet(paths);
        Present(_watcher.Machine.Wheel);
    }

    /// <summary>Shows the petals of a wheel for the current files, or steps aside when nothing fits.</summary>
    private void Present(WheelKind wheel)
    {
        if (_input is null) return;
        var operations = Match(wheel, _input);
        if (operations.Count == 0)
        {
            // Nothing to offer: hide so the normal drop underneath keeps working. Another wheel key may still bring it back.
            _overlay.Retract();
            return;
        }

        if (!_overlay.IsVisible) _overlay.Arm(ScreenPlacement.Cursor());
        var t = Localizer.Current;
        var caption = t.T(WheelKey(wheel)).ToUpper(CultureInfo.CurrentUICulture);
        var idle = _input.IsSingle ? Path.GetFileName(_input.Paths[0].TrimEnd('\\')) : t.N("wheel.files", _input.Count);
        _overlay.OpenWheel(caption, idle, operations.Select(ToPetal).ToList());
    }

    private IReadOnlyList<IOperation> Match(WheelKind wheel, InputSet input)
    {
        try
        {
            return _registry.Match(wheel, input);
        }
        catch (Exception ex)
        {
            // A faulty Accepts() must not break dragging for the whole desktop.
            CrashLog.Write(ex);
            return [];
        }
    }

    private void OnDropped(WheelPetal? petal)
    {
        var input = _input;
        _watcher.Raise(_watcher.Machine.Dropped());
        if (petal?.Operation is not { } operation || input is null) return;
        // Let the OLE Drop call return to the drag source first; then start the work. Normal, not Background:
        // Background waited behind the wheel's closing render and every other pending item.
        _overlay.Dispatcher.BeginInvoke(DispatcherPriority.Normal, () => _runner.Start(operation, input));
    }

    private static WheelPetal ToPetal(IOperation operation)
    {
        var t = Localizer.Current;
        var title = t.T(operation.TitleKey);
        var shortKey = operation.TitleKey + ".short";
        var label = t.Has(shortKey, t.Language) || t.Has(shortKey, "en") ? t.T(shortKey) : title;
        return new WheelPetal(operation, title, label, operation.Badge, operation.Glyph);
    }

    public static string WheelKey(WheelKind wheel) => wheel switch
    {
        WheelKind.Convert => "wheel.convert",
        WheelKind.Tools => "wheel.tools",
        _ => "wheel.actions",
    };

    public void Dispose()
    {
        _watcher.Command -= OnCommand;
        _overlay.Retract();
    }
}
