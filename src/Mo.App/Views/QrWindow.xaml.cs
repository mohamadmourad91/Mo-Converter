using System.Windows;
using Microsoft.Win32;
using Mo.App.ViewModels;
using Mo.Engine;
using Mo.Engine.Images;

namespace Mo.App.Views;

/// <summary>Type a link or text and get a QR code to save (PNG for screens, SVG for print) or copy.</summary>
public partial class QrWindow : Window
{
    public QrWindow()
    {
        InitializeComponent();
        DataContext = Model;
        Loaded += (_, _) => Input.Focus();
    }

    public QrViewModel Model { get; } = new();

    /// <summary>Called with a status message after saving or copying (shown as a toast by the app).</summary>
    public event Action<string>? Done;

    private void OnSavePng(object sender, RoutedEventArgs e)
    {
        if (Model.Png is not { } png || Ask("png") is not { } path) return;
        File.WriteAllBytes(path, QrCodes.Png(Model.Text, 20));
        Done?.Invoke(Localizer.Current.T("result.saved", System.IO.Path.GetFileName(path)));
    }

    private void OnSaveSvg(object sender, RoutedEventArgs e)
    {
        if (Model.Png is null || Ask("svg") is not { } path) return;
        File.WriteAllText(path, QrCodes.Svg(Model.Text));
        Done?.Invoke(Localizer.Current.T("result.saved", System.IO.Path.GetFileName(path)));
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (Model.Png is not { } png) return;
        Clipboard.SetImage(ToolWindowSupport.Picture(png));
        Done?.Invoke(Localizer.Current.T("qr.copied"));
    }

    private string? Ask(string extension)
    {
        var dialog = new SaveFileDialog
        {
            FileName = "QR-Code." + extension,
            DefaultExt = extension,
            Filter = $"{extension.ToUpperInvariant()}|*.{extension}",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }
}
