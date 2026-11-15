using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Mo.App;
using Mo.App.Services;
using Mo.App.Services.Demo;
using Mo.App.Shell;
using Mo.App.Shell.Logic;
using Mo.App.ViewModels;
using Mo.App.Views;
using Mo.Engine;

namespace Mo.Shots;

/// <summary>
/// Renders the wheels, status cards, settings window and update window in every language and theme
/// as transparent, double-resolution PNGs. Windows are opened far off-screen, so nothing appears on the desktop.
///
///   Mo.Shots &lt;output folder&gt; [scale]
/// </summary>
public static class Program
{
    private static double _scale = 2;
    private static string _out = "";

    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["--perf", ..]) return Perf.Run();
        if (args is ["--perf-png", .. var pictures]) return Perf.Png(pictures);
        if (args.Length < 1)
        {
            Console.WriteLine("Usage: Mo.Shots <output folder> [scale]");
            return 2;
        }
        _out = Directory.CreateDirectory(args[0]).FullName;
        if (args.Length > 1) _scale = double.Parse(args[1], CultureInfo.InvariantCulture);

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // Awaited work (PDF thumbnails) must come back to this thread.
        System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var samples = Samples();
        var shared = Load("Themes/Shared.xaml");

        foreach (var dark in new[] { false, true })
        {
            app.Resources.MergedDictionaries.Clear();
            app.Resources.MergedDictionaries.Add(Load(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml"));
            app.Resources.MergedDictionaries.Add(shared);
            var theme = dark ? "dark" : "light";

            foreach (var language in Localizer.Languages)
            {
                Localizer.Current = new Localizer(language, typeof(Mo.App.App).Assembly);
                var tag = $"{language}-{theme}";
                Wheels(tag);
                Toasts(tag);
                SettingsShot(tag, dark);
                UpdateShot(tag, dark);
                EditorShot(tag, dark, samples.Picture);
                PagesShot(tag, dark, samples.Pdf);
                QrShot(tag, dark);
                ShelfShot(tag, dark, samples.Picture, samples.Pdf);
                WelcomeShot(tag, dark);
                Console.WriteLine($"rendered {tag}");
            }
        }
        return 0;
    }

    // ---------- wheels ----------

    private static void Wheels(string tag)
    {
        var registry = OperationRegistry.CreateDefault();
        DemoOperations.Register(registry);
        var one = new InputSet([@"C:\Demo\Sonnenuntergang.jpg"]);
        var three = new InputSet([@"C:\Demo\Strand.jpg", @"C:\Demo\Berge.png", @"C:\Demo\Stadt.heic"]);

        Wheel(registry, WheelKind.Convert, one, "image.to-png", $"wheel-convert-{tag}.png");
        Wheel(registry, WheelKind.Convert, one, null, $"wheel-convert-idle-{tag}.png");
        Wheel(registry, WheelKind.Tools, one, "image.compress", $"wheel-tools-{tag}.png");
        Wheel(registry, WheelKind.Actions, one, "app.copy-path", $"wheel-actions-{tag}.png");
        Wheel(registry, WheelKind.Convert, three, "image.to-pdf", $"wheel-convert-3files-{tag}.png");

        var pdf = new InputSet([@"C:\Demo\Quartalsbericht.pdf"]);
        var pdfs = new InputSet([@"C:\Demo\Teil 1.pdf", @"C:\Demo\Teil 2.pdf"]);
        Wheel(registry, WheelKind.Convert, pdf, "pdf.to-png", $"wheel-pdf-convert-{tag}.png");
        Wheel(registry, WheelKind.Tools, pdf, "pdf.compress", $"wheel-pdf-tools-{tag}.png");
        Wheel(registry, WheelKind.Tools, pdfs, "pdf.merge", $"wheel-pdf-merge-{tag}.png");

        // For the promo video: the choices its scenes show.
        Wheel(registry, WheelKind.Convert, one, "image.to-webp", $"wheel-convert-webp-{tag}.png");
        Wheel(registry, WheelKind.Convert, pdf, null, $"wheel-pdf-idle-{tag}.png");
        Wheel(registry, WheelKind.Convert, pdf, "pdf.to-xlsx", $"wheel-pdf-xlsx-{tag}.png");

        var video = new InputSet([@"C:\Demo\Urlaub.mp4"]);
        var song = new InputSet([@"C:\Demo\Podcast.mp3"]);
        Wheel(registry, WheelKind.Convert, video, "video.to-gif", $"wheel-video-convert-{tag}.png");
        Wheel(registry, WheelKind.Tools, video, "video.compress", $"wheel-video-tools-{tag}.png");
        Wheel(registry, WheelKind.Convert, song, "audio.to-m4a", $"wheel-audio-convert-{tag}.png");
        Wheel(registry, WheelKind.Tools, song, "audio.trim-silence", $"wheel-audio-tools-{tag}.png");

        Wheel(registry, WheelKind.Convert, new InputSet([@"C:\Demo\Bewerbung.docx"]), "doc.to-pdf", $"wheel-word-convert-{tag}.png");
        Wheel(registry, WheelKind.Convert, new InputSet([@"C:\Demo\Umsatz.csv"]), "sheet.to-xlsx", $"wheel-csv-convert-{tag}.png");
        Wheel(registry, WheelKind.Tools, new InputSet([@"C:\Demo\Fotos.7z"]), "archive.extract", $"wheel-archive-tools-{tag}.png");
    }

    private static void Wheel(OperationRegistry registry, WheelKind kind, InputSet input, string? highlightId, string file)
    {
        var t = Localizer.Current;
        var all = registry.Match(kind, input).Select(ToPetal).ToList();
        var pages = WheelPager.PageCount(all.Count);
        var petals = WheelPager.Page(all, 0).Select(s => s.IsMore
            ? new WheelPetal(null, t.T("wheel.more"), t.T("wheel.page", 1, pages), null, "", true)
            : s.Item!).ToList();

        var highlight = highlightId is null ? WheelHit.Outside : petals.FindIndex(p => p.Operation?.Id == highlightId);
        var idle = input.IsSingle ? Path.GetFileName(input.Paths[0]) : t.T("wheel.files", input.Count);
        var view = new WheelView
        {
            Petals = petals,
            Caption = t.T(WheelController.WheelKey(kind)).ToUpper(CultureInfo.CurrentUICulture),
            HubText = highlight >= 0 ? petals[highlight].Title : idle,
            HighlightIndex = highlight,
            Clockwise = !t.IsRightToLeft,
            RightToLeftText = t.IsRightToLeft,
            Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 5, Direction = 270, Opacity = 0.30, Color = Colors.Black },
        };
        SaveStandalone(new Border { Padding = new Thickness(40), Child = view }, file);
    }

    /// <summary>Same title/label choice as the wheel controller.</summary>
    private static WheelPetal ToPetal(IOperation operation)
    {
        var t = Localizer.Current;
        var title = t.T(operation.TitleKey);
        var shortKey = operation.TitleKey + ".short";
        var label = t.Has(shortKey, t.Language) || t.Has(shortKey, "en") ? t.T(shortKey) : title;
        return new WheelPetal(operation, title, label, operation.Badge, operation.Glyph);
    }

    // ---------- status cards ----------

    private static void Toasts(string tag)
    {
        var t = Localizer.Current;
        Toast(new ToastViewModel { Kind = ToastKind.Working, Message = t.T("toast.working", t.T("op.image.to-webp")), HasProgress = true, Progress = 0.62 },
            $"toast-working-{tag}.png");
        Toast(new ToastViewModel { Kind = ToastKind.Success, Message = OperationRunner.Describe(OperationResult.Of(@"C:\Demo\Sonnenuntergang.webp")) },
            $"toast-done-{tag}.png");
        Toast(new ToastViewModel { Kind = ToastKind.Success, Message = OperationRunner.Describe(OperationResult.Of("a.pdf", "b.pdf", "c.pdf")) },
            $"toast-done-many-{tag}.png");
        Toast(new ToastViewModel { Kind = ToastKind.Info, Message = t.T("update.downloading", "1.1.0") },
            $"toast-update-{tag}.png");
    }

    private static void Toast(ToastViewModel model, string file)
    {
        var window = new ToastWindow(model);
        var card = (FrameworkElement)window.Content;
        window.Content = null;
        var host = new Grid { FlowDirection = window.FlowDirection, DataContext = model };
        host.Children.Add(card);
        SaveStandalone(host, file);
        window.Close();
    }

    // ---------- windows ----------

    private sealed class NoDialogs : ISettingsDialogs
    {
        public string? PickFolder(string? current) => null;
        public bool AskRestart() => false;
        public void Close() { }
    }

    private static void SettingsShot(string tag, bool dark)
    {
        var window = new SettingsWindow();
        window.DataContext = new SettingsViewModel(new AppSettings(), new NoDialogs(), startWithWindows: true, _ => { }, (_, _) => { });
        window.SizeToContent = SizeToContent.Height;
        window.ApplyTheme(dark);
        SaveWindow(window, dark, $"settings-{tag}.png");
    }

    private static void UpdateShot(string tag, bool dark)
    {
        var window = new UpdateWindow("1.1.0", TimeSpan.FromSeconds(60), () => { }) { AllowClose = true };
        window.ApplyTheme(dark);
        SaveWindow(window, dark, $"update-{tag}.png");
    }

    /// <summary>Opens the window far off-screen so the Fluent styles apply, then renders its client area.</summary>
    private static void SaveWindow(Window window, bool dark, string file)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000;
        window.Top = -32000;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Show();
        Pump();

        var client = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
        var width = client.ActualWidth;
        var height = client.ActualHeight;
        var bitmap = NewBitmap(width, height);

        var background = new DrawingVisual();
        using (var dc = background.RenderOpen())
        {
            var fill = new SolidColorBrush(dark ? Color.FromRgb(0x20, 0x20, 0x24) : Color.FromRgb(0xF3, 0xF3, 0xF6));
            dc.DrawRoundedRectangle(fill, null, new Rect(0, 0, width, height), 10, 10);
        }
        bitmap.Render(background);
        bitmap.Render(client);
        Write(bitmap, file);
        window.Close();
    }

    // ---------- tool windows ----------

    private static (string Picture, string Pdf) Samples()
    {
        var folder = Directory.CreateTempSubdirectory("mo-shots-").FullName;
        var picture = Path.Combine(folder, "Sonnenuntergang.jpg");
        using (var image = new ImageMagick.MagickImage("gradient:#F2A65A-#4B46D8", 1600, 1000))
        {
            using var sun = new ImageMagick.MagickImage("radial-gradient:#FFE9A8-#F2A65A00", 360, 360);
            image.Composite(sun, 1050, 160, ImageMagick.CompositeOperator.Over);
            image.Write(picture, ImageMagick.MagickFormat.Jpeg);
        }
        var pages = new List<string>();
        foreach (var (name, top, bottom) in new[] { ("Seite1", "#4B46D8", "#8C88FF"), ("Seite2", "#0F9ED5", "#9ADFF5"), ("Seite3", "#E97132", "#F8C9A9"), ("Seite4", "#196B24", "#9BD3A3") })
        {
            var page = Path.Combine(folder, name + ".png");
            using var image = new ImageMagick.MagickImage($"gradient:{top}-{bottom}", 600, 840);
            image.Write(page, ImageMagick.MagickFormat.Png);
            pages.Add(page);
        }
        var registry = OperationRegistry.CreateDefault();
        var pdf = registry.Find("image.to-pdf")!.RunAsync(new OperationContext(new InputSet(pages), new OutputPlanner(folder)), CancellationToken.None)
            .GetAwaiter().GetResult().Outputs[0];
        return (picture, pdf);
    }

    private static void EditorShot(string tag, bool dark, string picture)
    {
        var window = new ImageEditorWindow(picture, EditorMode.Hide);
        window.Model.Crop = new Mo.Engine.Images.CropRect(120, 80, 1300, 820);
        window.Model.Redactions.Add(new Mo.Engine.Images.CropRect(200, 600, 420, 160));
        window.Model.Saturation = 25;
        window.RenderPreview();
        ToolWindowSupport.Prepare(window, dark);
        SaveWindow(window, dark, $"editor-{tag}.png");
    }

    private static void PagesShot(string tag, bool dark, string pdf)
    {
        var window = new PdfPagesWindow(pdf);
        ToolWindowSupport.Prepare(window, dark);
        Wait(window.LoadAsync());
        window.Model!.RotateCommand.Execute(window.Model.Pages[1]);
        SaveWindow(window, dark, $"pages-{tag}.png");
    }

    private static void QrShot(string tag, bool dark)
    {
        var window = new QrWindow();
        window.Model.Text = "https://github.com/mohamadmourad91/Mo-Converter";
        ToolWindowSupport.Prepare(window, dark);
        SaveWindow(window, dark, $"qr-{tag}.png");
    }

    private static void ShelfShot(string tag, bool dark, string picture, string pdf)
    {
        var model = new Mo.App.ViewModels.ShelfViewModel();
        model.Add([picture, pdf]);
        var window = new ShelfWindow(model);
        ToolWindowSupport.Prepare(window, dark);
        SaveWindow(window, dark, $"shelf-{tag}.png");
    }

    private static void WelcomeShot(string tag, bool dark)
    {
        var window = new WelcomeWindow(new Mo.Engine.AppSettings());
        ToolWindowSupport.Prepare(window, dark);
        SaveWindow(window, dark, $"welcome-{tag}.png");
    }

    /// <summary>Lets the dispatcher run until the task is done (WinRT rendering resumes on this thread).</summary>
    private static void Wait(Task task)
    {
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }

    // ---------- helpers ----------

    private static void SaveStandalone(FrameworkElement element, string file)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
        var bitmap = NewBitmap(element.ActualWidth, element.ActualHeight);
        bitmap.Render(element);
        Write(bitmap, file);
    }

    private static RenderTargetBitmap NewBitmap(double width, double height) =>
        new((int)Math.Ceiling(width * _scale), (int)Math.Ceiling(height * _scale), 96 * _scale, 96 * _scale, PixelFormats.Pbgra32);

    private static void Write(BitmapSource bitmap, string file)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(_out, file));
        encoder.Save(stream);
    }

    private static void Pump() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    private static ResourceDictionary Load(string path) =>
        (ResourceDictionary)Application.LoadComponent(new Uri($"/Mo Converter;component/{path}", UriKind.Relative));
}
