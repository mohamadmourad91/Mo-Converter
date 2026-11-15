namespace Mo.App.ViewModels;

public enum ToastKind { Working, Success, Error, Info }

/// <summary>State of the small status card shown at the top of the screen.</summary>
public sealed class ToastViewModel : ObservableObject
{
    private ToastKind _kind;
    private string _message = "";
    private double _progress;
    private bool _hasProgress;

    public ToastKind Kind
    {
        get => _kind;
        set
        {
            if (!Set(ref _kind, value)) return;
            OnPropertyChanged(nameof(Glyph));
            OnPropertyChanged(nameof(ShowProgress));
        }
    }

    public string Message { get => _message; set => Set(ref _message, value); }

    /// <summary>0..1 when the operation reports progress.</summary>
    public double Progress { get => _progress; set => Set(ref _progress, value); }

    /// <summary>False until the first progress report arrives; the bar is indeterminate until then.</summary>
    public bool HasProgress
    {
        get => _hasProgress;
        set { if (Set(ref _hasProgress, value)) OnPropertyChanged(nameof(IsIndeterminate)); }
    }

    public bool IsIndeterminate => !HasProgress;

    public bool ShowProgress => Kind == ToastKind.Working;

    /// <summary>Segoe Fluent Icons glyph for the kind.</summary>
    public string Glyph => Kind switch
    {
        ToastKind.Working => "\uE895",  // Sync
        ToastKind.Success => "\uE73E",  // CheckMark
        ToastKind.Error => "\uE783",    // Error
        _ => "\uE946",                  // Info
    };

    public string AppName => Mo.Engine.Localizer.Current.T("app.name");
}
