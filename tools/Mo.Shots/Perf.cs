using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Mo.App.Services;
using Mo.App.Services.Demo;
using Mo.App.Shell;
using Mo.App.Shell.Logic;
using Mo.Engine;

namespace Mo.Shots;

/// <summary>
/// Times the steps between "the user drags a file" and "the result is there", each one cold (first time in
/// this process) and warm. Needs no mouse: the overlay is armed in a screen corner and retracted at once.
///
///   Mo.Shots --perf
/// </summary>
internal static class Perf
{
    public static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        app.Resources.MergedDictionaries.Add(Load("Themes/Light.xaml"));
        app.Resources.MergedDictionaries.Add(Load("Themes/Shared.xaml"));
        Localizer.Current = new Localizer("en", typeof(Mo.App.App).Assembly);

        var folder = Directory.CreateTempSubdirectory("mo-perf-").FullName;
        var jpg = Path.Combine(folder, "photo.jpg");
        WriteSample(jpg, 4000, 3000);
        var docx = Path.Combine(folder, "letter.docx");
        File.WriteAllBytes(docx, []);

        Console.WriteLine("--- wheel logic (UI thread, inside the OLE DragEnter call) ---");
        OperationRegistry registry = null!;
        Once("registry: create", () => { registry = OperationRegistry.CreateDefault(); DemoOperations.Register(registry); });
        Once("LibreOffice.IsAvailable (first call)", () => _ = Mo.Engine.Office.LibreOffice.IsAvailable);
        InputSet input = null!;
        Once("InputSet (1 file), cold", () => input = new InputSet([jpg]));
        Warm("InputSet (1 file), warm", () => input = new InputSet([jpg]));
        var many = Enumerable.Repeat(jpg, 200).ToArray();
        Warm("InputSet (200 files), warm", () => _ = new InputSet(many), 20);
        Once("Match convert (jpg), cold", () => registry.Match(WheelKind.Convert, input));
        Once("Match tools (jpg), cold", () => registry.Match(WheelKind.Tools, input));
        Warm("Match convert (jpg), warm", () => registry.Match(WheelKind.Convert, input));
        var word = new InputSet([docx]);
        Once("Match convert (docx), cold", () => registry.Match(WheelKind.Convert, word));
        Warm("Match convert (docx), warm", () => registry.Match(WheelKind.Convert, word));

        Console.WriteLine("--- overlay window ---");
        WheelOverlay overlay = null!;
        Once("WheelOverlay: construct", () => overlay = new WheelOverlay());
        var corner = new Native.POINT(0, 0);
        var petals = registry.Match(WheelKind.Convert, input).Select(o =>
            new WheelPetal(o, Localizer.Current.T(o.TitleKey), Localizer.Current.T(o.TitleKey), o.Badge, o.Glyph)).ToList();
        Console.WriteLine($"    ({petals.Count} petals)");
        if (Environment.GetEnvironmentVariable("MO_PERF_NO_PREWARM") is null)
            Once("Prewarm (what the app does after start)", () => { overlay.Prewarm(petals); Pump(); overlay.Retract(); Pump(); });
        Once("Arm (Show + position), first drag", () => { overlay.Arm(corner); Pump(); });
        overlay.Retract();
        Pump();
        Once("Arm (Show + position), warm", () => { overlay.Arm(corner); Pump(); });

        Once("OpenWheel + layout/render, first drag", () => { overlay.OpenWheel("CONVERT", "photo.jpg", petals); Pump(); });
        overlay.Retract();
        Pump();
        overlay.Arm(corner);
        Pump();
        Once("OpenWheel + layout/render, warm", () => { overlay.OpenWheel("CONVERT", "photo.jpg", petals); Pump(); });
        overlay.Retract();
        Pump();

        Console.WriteLine("--- wheel redraw while the cursor moves over petals ---");
        var page = WheelPager.Page(petals, 0).Select(s => s.IsMore ? new WheelPetal(null, "More", "1/2", null, "\uE712", true) : s.Item!).ToList();
        var view = new WheelView { Petals = page, Caption = "CONVERT", HubText = "photo.jpg" };
        view.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        view.Arrange(new Rect(view.DesiredSize));
        view.UpdateLayout();
        var index = 0;
        Once("highlight change + OnRender, cold", () => { view.HighlightIndex = 0; view.HubText = page[0].Title; view.UpdateLayout(); });
        Warm("highlight change + OnRender, warm", () =>
        {
            index = (index + 1) % page.Count;
            view.HighlightIndex = index;
            view.HubText = page[index].Title;
            view.UpdateLayout();
        }, 300);

        Console.WriteLine("--- watcher timer ---");
        var ticks = 0;
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => ticks++;
        var frame = new DispatcherFrame();
        var stop = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        stop.Tick += (_, _) => { stop.Stop(); frame.Continue = false; };
        timer.Start();
        stop.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        Console.WriteLine($"{"10 ms DispatcherTimer: real interval",-46} {3000.0 / Math.Max(1, ticks),8:F1} ms  ({ticks} ticks in 3 s)");

        Console.WriteLine("--- after the drop ---");
        var toasts = new ToastService();
        var token = 0;
        if (Environment.GetEnvironmentVariable("MO_PERF_NO_PREWARM") is null)
            Once("toast: Prewarm", () => { toasts.Prewarm(); Pump(); });
        Once("toast: BeginWork + show, first drop", () => { token = toasts.BeginWork("Working"); Pump(); });
        Once("toast: Report, warm", () => { toasts.Report(token, 0.5); Pump(); });
        Once("Magick: load native library", () => _ = ImageMagick.MagickNET.Version);
        var planner = new OutputPlanner(folder);
        foreach (var id in new[] { "image.to-png", "image.to-png", "image.to-webp", "image.to-webp" })
        {
            var operation = registry.Find(id)!;
            Once($"{id} (12 MP jpg)", () => operation.RunAsync(new OperationContext(input, planner), CancellationToken.None).GetAwaiter().GetResult());
        }

        Console.WriteLine("--- every convert and tool operation on one 12 MP picture (jpg source, then png source) ---");
        var png = Path.Combine(folder, "photo-source.png");
        using (var image = new ImageMagick.MagickImage(jpg))
        {
            image.Quality = 15;
            image.Write(png, ImageMagick.MagickFormat.Png);
        }
        foreach (var source in new[] { jpg, png })
        {
            var set = new InputSet([source]);
            var sweep = new OutputPlanner(Directory.CreateDirectory(Path.Combine(folder, "sweep-" + Path.GetExtension(source).TrimStart('.'))).FullName);
            foreach (var operation in registry.Match(WheelKind.Convert, set).Concat(registry.Match(WheelKind.Tools, set)))
            {
                if (operation.EditorId is not null || !operation.Id.StartsWith("image.", StringComparison.Ordinal)) continue;
                try
                {
                    Once($"{Path.GetExtension(source)} -> {operation.Id}", () =>
                        operation.RunAsync(new OperationContext(set, sweep), CancellationToken.None).GetAwaiter().GetResult());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{Path.GetExtension(source)} -> {operation.Id}: failed ({ex.GetType().Name})");
                }
            }
        }

        Console.WriteLine("--- PNG encoder settings (same 12 MP picture; quality = zlib level * 10 + filter) ---");
        Once("decode jpg only", () => { using var image = new ImageMagick.MagickImage(jpg); });
        foreach (var quality in new uint[] { 0, 75, 35, 31, 15, 11 })
        {
            using var image = new ImageMagick.MagickImage(jpg);
            var label = quality == 0 ? $"as loaded (quality {image.Quality})" : $"quality {quality}";
            if (quality != 0) image.Quality = quality;
            var target = Path.Combine(folder, $"q{quality}.png");
            Once($"write png, {label}", () => image.Write(target, ImageMagick.MagickFormat.Png));
            Console.WriteLine($"    {new FileInfo(target).Length / 1048576.0:F1} MB");
        }

        try { Directory.Delete(folder, true); } catch (IOException) { }
        return 0;
    }

    /// <summary>
    ///   Mo.Shots --perf-png &lt;picture&gt;...
    /// Writes each picture as PNG with several zlib levels and filters and prints time and size,
    /// to choose the settings on real pictures instead of a synthetic one.
    /// </summary>
    public static int Png(IEnumerable<string> files)
    {
        var folder = Directory.CreateTempSubdirectory("mo-perf-png-").FullName;
        foreach (var file in files)
        {
            using var source = new ImageMagick.MagickImage(file);
            Console.WriteLine($"--- {Path.GetFileName(file)} ({source.Width}x{source.Height}) ---");
            foreach (var (level, filter, strategy) in new (string, string, string?)[]
                     { ("9", "5", "1"), ("7", "5", null), ("6", "5", null), ("4", "5", null), ("3", "5", null), ("2", "5", null), ("1", "5", null) })
            {
                using var image = (ImageMagick.MagickImage)source.Clone();
                image.Settings.SetDefine(ImageMagick.MagickFormat.Png, "compression-level", level);
                image.Settings.SetDefine(ImageMagick.MagickFormat.Png, "compression-filter", filter);
                if (strategy is not null) image.Settings.SetDefine(ImageMagick.MagickFormat.Png, "compression-strategy", strategy);
                var target = Path.Combine(folder, "out.png");
                var watch = Stopwatch.StartNew();
                image.Write(target, ImageMagick.MagickFormat.Png);
                watch.Stop();
                Console.WriteLine($"level {level} filter {filter} strategy {strategy ?? "-"}  {watch.Elapsed.TotalMilliseconds,8:F0} ms  {new FileInfo(target).Length / 1024.0,9:F0} KB");
            }
        }
        try { Directory.Delete(folder, true); } catch (IOException) { }
        return 0;
    }

    private static void Once(string name, Action action)
    {
        var watch = Stopwatch.StartNew();
        action();
        watch.Stop();
        Console.WriteLine($"{name,-46} {watch.Elapsed.TotalMilliseconds,8:F1} ms");
    }

    private static void Warm(string name, Action action, int runs = 200)
    {
        action();
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < runs; i++) action();
        watch.Stop();
        Console.WriteLine($"{name,-46} {watch.Elapsed.TotalMilliseconds / runs,8:F3} ms  (avg of {runs})");
    }

    private static void WriteSample(string path, int width, int height)
    {
        // Written with WPF's own encoder so the image library stays cold for the measurement.
        var pixels = new byte[width * height * 3];
        var random = new Random(7);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 3;
                pixels[i] = (byte)(x * 255 / width);
                pixels[i + 1] = (byte)(y * 255 / height);
                pixels[i + 2] = (byte)random.Next(96, 160);
            }
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Rgb24, null, pixels, width * 3);
        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void Pump() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    private static ResourceDictionary Load(string path) =>
        (ResourceDictionary)Application.LoadComponent(new Uri($"/Mo Converter;component/{path}", UriKind.Relative));
}
