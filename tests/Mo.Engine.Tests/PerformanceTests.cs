using System.Diagnostics;
using ImageMagick;
using Mo.Engine.Media;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using Xunit.Abstractions;

namespace Mo.Engine.Tests;

/// <summary>
/// Big inputs must stay fast. Limits are about five times what a normal PC needs, so only a real slowdown
/// (an accidental quadratic loop, files read twice) fails them; the measured times are written to the test output.
/// </summary>
public sealed class PerformanceFixture : IDisposable
{
    public string Folder { get; } = Directory.CreateTempSubdirectory("mo-perf-").FullName;
    public string BigPhoto { get; }
    public string BigPng { get; }
    public string LongPdf { get; }
    public string LongAudio { get; }

    public const int PdfPages = 200;

    public PerformanceFixture()
    {
        // 24 megapixels of noise: the worst case for encoders.
        BigPhoto = Path.Combine(Folder, "photo.jpg");
        using (var image = new MagickImage(MagickColors.SteelBlue, 6000, 4000))
        {
            image.AddNoise(NoiseType.Gaussian);
            image.Quality = 90;
            image.Write(BigPhoto, MagickFormat.Jpeg);
        }

        // The same noise as PNG: rotate, clean and compress write PNG back, the slowest encoder here.
        BigPng = Path.Combine(Folder, "photo.png");
        using (var image = new MagickImage(BigPhoto))
        {
            image.Settings.SetDefine(MagickFormat.Png, "compression-level", "1");
            image.Write(BigPng, MagickFormat.Png);
        }

        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        LongPdf = Path.Combine(Folder, "long.pdf");
        using (var document = new PdfDocument())
        {
            var font = new XFont("Arial", 11);
            for (var p = 1; p <= PdfPages; p++)
            {
                var page = document.AddPage(); page.Size = PdfSharp.PageSize.A4; // not the culture default (Letter on a US PC)
                using var gfx = XGraphics.FromPdfPage(page);
                gfx.DrawString($"Seite {p}", new XFont("Arial", 20, XFontStyleEx.Bold), XBrushes.Black, 72, 80);
                for (var line = 0; line < 40; line++)
                    gfx.DrawString($"Zeile {line}: Mo Converter wandelt Dateien lokal um, ohne sie hochzuladen.", font, XBrushes.Black, 72, 120 + line * 16);
            }
            document.Save(LongPdf);
        }

        LongAudio = Path.Combine(Folder, "long.wav");
        Ffmpeg.RunAsync(["-f", "lavfi", "-i", "sine=frequency=440:sample_rate=44100", "-t", "360", LongAudio], 0, null, CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        try { Directory.Delete(Folder, true); } catch (IOException) { }
    }
}

public sealed class PerformanceTests(PerformanceFixture fixture, ITestOutputHelper output) : IClassFixture<PerformanceFixture>
{
    private static readonly OperationRegistry Registry = OperationRegistry.CreateDefault();

    private async Task<TimeSpan> Time(string id, string file, TimeSpan limit)
    {
        var folder = Directory.CreateDirectory(Path.Combine(fixture.Folder, "out-" + Guid.NewGuid().ToString("N")[..6])).FullName;
        var watch = Stopwatch.StartNew();
        var result = await Registry.Find(id)!.RunAsync(new OperationContext(new InputSet([file]), new OutputPlanner(folder)), CancellationToken.None);
        watch.Stop();
        output.WriteLine($"{id,-22} {watch.Elapsed.TotalSeconds,6:0.0} s  ->  {Path.GetFileName(result.Outputs[0])}");
        Assert.True(watch.Elapsed < limit, $"{id} took {watch.Elapsed.TotalSeconds:0.0} s (limit {limit.TotalSeconds:0} s)");
        return watch.Elapsed;
    }

    [Theory]
    [InlineData("image.to-png", 60)]
    [InlineData("image.to-webp", 60)]
    [InlineData("image.to-avif", 120)]
    [InlineData("image.to-pdf", 60)]
    [InlineData("image.compress", 60)]
    [InlineData("image.rotate", 60)]
    [InlineData("image.strip-metadata", 60)]
    public Task A_24_megapixel_photo(string id, int seconds) => Time(id, fixture.BigPhoto, TimeSpan.FromSeconds(seconds));

    // PNG writing once took about 1 s per megapixel (zlib level 7-9 inherited from the picture's Quality),
    // which the generous limits above never caught. These limits fail that again but leave room for a slow PC.
    [Theory]
    [InlineData("image.to-png", 0)]
    [InlineData("image.rotate", 1)]
    [InlineData("image.strip-metadata", 1)]
    [InlineData("image.to-pdf", 1)]
    [InlineData("image.compress", 1)]
    public Task Writing_png_stays_quick(string id, int pngSource) =>
        Time(id, pngSource == 1 ? fixture.BigPng : fixture.BigPhoto, TimeSpan.FromSeconds(id == "image.compress" ? 40 : 14));

    [Theory]
    [InlineData("pdf.to-txt", 30)]
    [InlineData("pdf.to-docx", 90)]
    [InlineData("pdf.to-xlsx", 90)]
    [InlineData("pdf.to-md", 90)]
    [InlineData("pdf.split", 60)]
    [InlineData("pdf.rotate", 30)]
    [InlineData("pdf.compress", 30)]
    [InlineData("pdf.strip-metadata", 30)]
    public Task A_200_page_pdf(string id, int seconds) => Time(id, fixture.LongPdf, TimeSpan.FromSeconds(seconds));

    [Theory]
    [InlineData("pdf.to-jpg", 60)]
    [InlineData("pdf.to-png", 60)]
    public Task A_200_page_pdf_becomes_images(string id, int seconds) => Time(id, fixture.LongPdf, TimeSpan.FromSeconds(seconds));

    [Theory]
    [InlineData("audio.to-mp3", 60)]
    [InlineData("audio.to-flac", 60)]
    [InlineData("audio.normalize", 90)]
    public Task A_six_minute_recording(string id, int seconds) => Time(id, fixture.LongAudio, TimeSpan.FromSeconds(seconds));
}
