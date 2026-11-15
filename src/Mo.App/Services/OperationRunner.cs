using System.Media;
using Mo.App.ViewModels;
using Mo.Engine;

namespace Mo.App.Services;

/// <summary>Runs a chosen operation on a background task and reports progress and the outcome in a toast.</summary>
public sealed class OperationRunner(Func<AppSettings> settings, ToastService toasts, IEditorHost editors, Action<IReadOnlyList<string>>? results = null)
{
    /// <summary>Starts the operation, or opens its editor. Must be called on the UI thread; returns immediately.</summary>
    public void Start(IOperation operation, InputSet input)
    {
        if (operation.EditorId is not null)
        {
            editors.Open(operation, input);
            return;
        }
        _ = RunAsync(operation, input, null);
    }

    /// <summary>Runs with the choices made in a tool window (the editor is not opened again).</summary>
    public void Start(IOperation operation, InputSet input, IReadOnlyDictionary<string, object?> choices) =>
        _ = RunAsync(operation, input, choices);

    private async Task RunAsync(IOperation operation, InputSet input, IReadOnlyDictionary<string, object?>? choices)
    {
        var t = Localizer.Current;
        var token = toasts.BeginWork(t.T("toast.working", t.T(operation.TitleKey)));
        // Progress<T> captures the UI SynchronizationContext here, so reports arrive on the UI thread.
        var progress = new Progress<OperationProgress>(p => toasts.Report(token, p.Fraction, p.Detail));
        var current = settings();
        var options = new Dictionary<string, object?>(OptionsFor(current));
        foreach (var (key, value) in choices ?? new Dictionary<string, object?>()) options[key] = value;
        var context = new OperationContext(input, new OutputPlanner(current.OutputFolder), progress, options);

        try
        {
            var result = await Task.Run(() => operation.RunAsync(context, CancellationToken.None));
            toasts.Show(ToastKind.Success, Describe(result));
            results?.Invoke(result.Outputs);
            Play(SystemSounds.Asterisk);
        }
        catch (OperationException ex)
        {
            toasts.Show(ToastKind.Error, t.T(ex.Key, ex.Args));
            Play(SystemSounds.Hand);
        }
        catch (OperationCanceledException)
        {
            toasts.Show(ToastKind.Info, t.T("toast.cancelled"));
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex);
            toasts.Show(ToastKind.Error, t.T("toast.unexpected", ex.Message));
            Play(SystemSounds.Hand);
        }
    }

    /// <summary>Preferences that operations read as options (the compress tool's "level").</summary>
    public static IReadOnlyDictionary<string, object?> OptionsFor(AppSettings settings) =>
        new Dictionary<string, object?> { ["level"] = settings.ImageCompression };

    /// <summary>Localized summary of a result: its own message, the saved file name, or the number of files.</summary>
    public static string Describe(OperationResult result)
    {
        var t = Localizer.Current;
        if (result.MessageKey is { } key)
            // A leading whole number is a count: "2 paths copied" needs the right plural form.
            return result.MessageArgs is [int count, ..] ? t.N(key, count) : t.T(key, result.MessageArgs ?? []);
        return result.Outputs.Count switch
        {
            0 => t.T("toast.done"),
            1 => t.T("result.saved", Path.GetFileName(result.Outputs[0].TrimEnd('\\'))),
            var n => t.N("result.saved-many", n),
        };
    }

    private void Play(SystemSound sound)
    {
        if (settings().PlaySounds) sound.Play();
    }
}
