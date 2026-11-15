using Mo.App.ViewModels;
using Mo.Engine;

namespace Mo.App.Services;

/// <summary>
/// Opens the tool window for operations that need user input before running (<see cref="IOperation.EditorId"/>).
/// Later phases register real editors; until then <see cref="ComingSoonEditorHost"/> answers.
/// </summary>
public interface IEditorHost
{
    /// <summary>Opens the editor for <paramref name="operation"/> with the dropped files.</summary>
    void Open(IOperation operation, InputSet input);
}

/// <summary>Placeholder: tells the user the tool window is not available yet.</summary>
public sealed class ComingSoonEditorHost(ToastService toasts) : IEditorHost
{
    public void Open(IOperation operation, InputSet input) =>
        toasts.Show(ToastKind.Info, Localizer.Current.T("toast.coming-soon", Localizer.Current.T(operation.TitleKey)));
}
