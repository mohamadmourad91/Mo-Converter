using System.Windows.Threading;
using Mo.App.Shell;
using Mo.App.ViewModels;

namespace Mo.App.Services;

/// <summary>Shows one status card at a time; the newest message replaces the previous one.</summary>
public sealed class ToastService
{
    private static readonly TimeSpan ShortStay = TimeSpan.FromSeconds(3.5);
    private static readonly TimeSpan LongStay = TimeSpan.FromSeconds(6);

    private readonly ToastViewModel _model = new();
    private readonly DispatcherTimer _hideTimer = new();
    private ToastWindow? _window;
    private int _owner;

    public ToastService() => _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); _window?.FadeOut(); };

    /// <summary>Shows a "working" card that stays until <see cref="Finish"/>. Returns a token for later updates.</summary>
    public int BeginWork(string message)
    {
        _hideTimer.Stop();
        _model.Kind = ToastKind.Working;
        _model.Message = message;
        _model.HasProgress = false;
        _model.Progress = 0;
        Present();
        return ++_owner;
    }

    /// <summary>Updates progress, unless a newer message has taken over the card.</summary>
    public void Report(int token, double fraction, string? detail = null)
    {
        if (token != _owner || _model.Kind != ToastKind.Working) return;
        _model.HasProgress = true;
        _model.Progress = Math.Clamp(fraction, 0, 1);
        if (!string.IsNullOrWhiteSpace(detail)) _model.Message = detail;
    }

    /// <summary>Shows a final message that hides itself after a few seconds.</summary>
    public void Show(ToastKind kind, string message)
    {
        _owner++;
        _model.Kind = kind;
        _model.Message = message;
        Present();
        _hideTimer.Stop();
        _hideTimer.Interval = kind == ToastKind.Error ? LongStay : ShortStay;
        _hideTimer.Start();
    }

    /// <summary>Builds the card window ahead of the first message (see <see cref="ToastWindow.Prewarm"/>).</summary>
    public void Prewarm()
    {
        if (_window is not null) return;
        _window = new ToastWindow(_model);
        _window.Prewarm();
    }

    private void Present()
    {
        _window ??= new ToastWindow(_model);
        _window.ShowCard();
    }
}
