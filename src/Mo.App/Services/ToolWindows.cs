using System.Windows;
using Mo.App.ViewModels;
using Mo.App.Views;
using Mo.Engine;
using Mo.Engine.Media;

namespace Mo.App.Services;

/// <summary>
/// Opens the tool window an operation asks for (<see cref="IOperation.EditorId"/>) and, when the user saves,
/// runs the operation with the choices made there.
/// </summary>
public sealed class ToolWindows(Func<OperationRunner> runner, OperationRegistry registry, ToastService toasts, Func<bool> dark) : IEditorHost
{
    public void Open(IOperation operation, InputSet input)
    {
        var path = input.Paths[0];
        try
        {
            switch (operation.EditorId)
            {
                case "image.editor":
                    var editor = new ImageEditorWindow(path, operation.Id == "image.crop" ? EditorMode.Crop : EditorMode.Hide);
                    var edit = registry.Find("image.edit") ?? operation;
                    editor.Saved += changes => runner().Start(edit, input, new Dictionary<string, object?> { ["edit"] = changes });
                    Show(editor);
                    break;

                case "video.trim":
                    var duration = Ffmpeg.Probe(path).Duration;
                    var trim = new VideoTrimWindow(path, duration);
                    trim.Saved += options => runner().Start(operation, input, options);
                    Show(trim);
                    break;

                case "pdf.pages":
                    var pages = new PdfPagesWindow(path);
                    pages.Saved += choices => runner().Start(operation, input, new Dictionary<string, object?> { ["pages"] = choices });
                    Show(pages);
                    _ = LoadPagesAsync(pages, path);
                    break;

                default:
                    toasts.Show(ToastKind.Info, Localizer.Current.T("toast.coming-soon", Localizer.Current.T(operation.TitleKey)));
                    break;
            }
        }
        catch (OperationException e)
        {
            toasts.Show(ToastKind.Error, Localizer.Current.T(e.Key, e.Args));
        }
        catch (Exception e) when (e is ImageMagick.MagickException or IOException or UnauthorizedAccessException)
        {
            CrashLog.Write(e);
            toasts.Show(ToastKind.Error, Localizer.Current.T("error.image-unreadable", System.IO.Path.GetFileName(path)));
        }
    }

    private async Task LoadPagesAsync(PdfPagesWindow window, string path)
    {
        try
        {
            await window.LoadAsync();
        }
        catch (OperationException e)
        {
            window.Close();
            toasts.Show(ToastKind.Error, Localizer.Current.T(e.Key, e.Args));
        }
    }

    /// <summary>The QR window from the tray menu.</summary>
    public void OpenQr()
    {
        var window = new QrWindow();
        window.Done += message => toasts.Show(ToastKind.Success, message);
        Show(window);
    }

    private void Show(Window window)
    {
        ToolWindowSupport.Prepare(window, dark());
        window.Show();
        window.Activate();
    }
}
