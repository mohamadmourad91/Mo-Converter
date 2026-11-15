using ImageMagick;
using Mo.Engine.Office;
using Mo.Engine.Pdf;
using Mo.Engine.Pdf.Layout;
using System.Runtime.InteropServices.WindowsRuntime;
using QRCoder;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using ZXing;
using ZXing.Common;

namespace Mo.Engine.Images;

/// <summary>
/// Text recognition with the OCR built into Windows. Every installed recognition language is tried and the
/// result that reads most like real text in that language wins, so Arabic and German pages both work.
/// </summary>
public static class TextRecognition
{
    public static IReadOnlyList<string> Languages =>
        OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag).ToList();

    public static async Task<string> RecognizeAsync(byte[] picture, CancellationToken cancellation)
    {
        var engines = OcrEngine.AvailableRecognizerLanguages.Select(OcrEngine.TryCreateFromLanguage).Where(e => e is not null).ToList();
        if (engines.Count == 0) throw new OperationException("error.ocr-unavailable");

        using var bitmap = await Decode(picture);
        var best = "";
        var bestScore = double.MinValue;
        foreach (var engine in engines)
        {
            cancellation.ThrowIfCancellationRequested();
            var result = await engine!.RecognizeAsync(bitmap);
            var rightToLeft = engine.RecognizerLanguage.LayoutDirection == Windows.Globalization.LanguageLayoutDirection.Rtl;
            var text = string.Join("\n", result.Lines.Select(l => l.Text));
            var score = Score(text, rightToLeft);
            if (score > bestScore) { bestScore = score; best = text; }
        }
        return best.Trim();
    }

    /// <summary>Letters of the engine's own script count; stray symbols (typical for a wrong language) count against it.</summary>
    internal static double Score(string text, bool rightToLeft)
    {
        double score = 0;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c)) continue;
            if (char.IsDigit(c)) score += 0.5;
            else if (Script.IsRightToLeftLetter(c)) score += rightToLeft ? 1 : -1;
            else if (char.IsLetter(c)) score += rightToLeft ? -0.5 : 1;
            else score -= 0.5;
        }
        return score;
    }

    /// <summary>Windows OCR takes at most 10 000 px per side; bigger pictures are scaled down first.</summary>
    private static async Task<SoftwareBitmap> Decode(byte[] picture)
    {
        byte[] png;
        using (var image = new MagickImage(picture))
        {
            var max = (uint)OcrEngine.MaxImageDimension;
            if (image.Width > max || image.Height > max) image.Resize(new MagickGeometry(max, max));
            // Only handed to the Windows decoder: the fastest level is enough.
            ImageSupport.FastPng(image);
            png = image.ToByteArray(MagickFormat.Png);
        }
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(png.AsBuffer());
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }
}

/// <summary>Tools wheel: the text in a picture (photo of a page, screenshot) as a text file.</summary>
internal sealed class ImageOcrOperation : ImageOperation
{
    public override string Id => "image.ocr";
    public override WheelKind Wheel => WheelKind.Tools;
    public override string Glyph => "\uE8D2";
    public override int Order => 50;

    public override Task<OperationResult> RunAsync(OperationContext context, CancellationToken cancellation) =>
        Task.Run(async () =>
        {
            var outputs = new List<string>();
            var paths = context.Input.Paths;
            for (var i = 0; i < paths.Count; i++)
            {
                context.Report((double)i / paths.Count, Path.GetFileName(paths[i]));
                byte[] picture;
                try { picture = await File.ReadAllBytesAsync(paths[i], cancellation); }
                catch (FileNotFoundException) { throw new OperationException("error.file-missing", Path.GetFileName(paths[i])); }
                string text;
                try { text = await TextRecognition.RecognizeAsync(picture, cancellation); }
                catch (MagickException) { throw new OperationException("error.image-unreadable", Path.GetFileName(paths[i])); }
                if (text.Length == 0) throw new OperationException("error.ocr-nothing", Path.GetFileName(paths[i]));
                var target = context.Output.PathFor(paths[i], "txt");
                TextFiles.Write(target, text + Environment.NewLine);
                outputs.Add(target);
            }
            context.Report(1);
            return OperationResult.Of([.. outputs]);
        }, cancellation);

    protected override string ProcessFile(OperationContext context, string source, CancellationToken cancellation) => throw new NotSupportedException();
}

/// <summary>Tools wheel: scanned PDF pages read with OCR into a text file (pages separated by form feeds).</summary>
internal sealed class PdfOcrOperation : PdfOperation
{
    public override string Id => "pdf.ocr";
    public override WheelKind Wheel => WheelKind.Tools;
    public override string Glyph => "\uE8D2";
    public override int Order => 45;

    protected override async Task<IReadOnlyList<string>> ProcessFileAsync(OperationContext context, string source, int index, CancellationToken cancellation)
    {
        var pages = new List<string>();
        await PdfRenderer.RenderAsync(source, 200, async (i, count, _, png) =>
        {
            using var buffer = new MemoryStream();
            await png.CopyToAsync(buffer, cancellation);
            pages.Add(await TextRecognition.RecognizeAsync(buffer.ToArray(), cancellation));
            context.Report((index + (i + 1.0) / count) / context.Input.Count);
        }, cancellation);
        if (pages.All(string.IsNullOrWhiteSpace)) throw new OperationException("error.ocr-nothing", Path.GetFileName(source));
        var target = context.Output.PathFor(source, "txt");
        TextFiles.Write(target, string.Join(Environment.NewLine + "\f" + Environment.NewLine, pages) + Environment.NewLine);
        return [target];
    }
}

/// <summary>QR codes: making them (PNG or SVG) and reading them from pictures.</summary>
public static class QrCodes
{
    /// <summary>A PNG with a quiet zone; <paramref name="pixelsPerModule"/> sets the size (10 → about 330 px for a short link).</summary>
    public static byte[] Png(string text, int pixelsPerModule = 10, bool dark = false)
    {
        using var data = QRCodeGenerator.GenerateQrCode(text, QRCodeGenerator.ECCLevel.M);
        using var code = new PngByteQRCode(data);
        return dark
            ? code.GetGraphic(pixelsPerModule, [255, 255, 255, 255], [0x1B, 0x1D, 0x26, 255])
            : code.GetGraphic(pixelsPerModule);
    }

    /// <summary>A sharp vector version for print.</summary>
    public static string Svg(string text)
    {
        using var data = QRCodeGenerator.GenerateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return new SvgQRCode(data).GetGraphic(10);
    }

    /// <summary>Every QR code (and common barcode) found in the picture; empty when there is none.</summary>
    public static IReadOnlyList<string> Read(string path)
    {
        using var image = new MagickImage(path);
        // Large photos are scaled down: decoding is faster and as reliable.
        if (image.Width > 2000 || image.Height > 2000) image.Resize(new MagickGeometry(2000, 2000));
        image.Alpha(AlphaAction.Remove);
        // Pixel values, not a file export: a 1-bit QR picture would otherwise come out packed and too short.
        var pixels = image.GetPixels().ToByteArray(PixelMapping.RGB)!;
        var source = new RGBLuminanceSource(pixels, (int)image.Width, (int)image.Height, RGBLuminanceSource.BitmapFormat.RGB24);
        var reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions { TryHarder = true, TryInverted = true },
        };
        Result[]? results;
        try
        {
            results = reader.DecodeMultiple(source);
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException)
        {
            // ZXing's multi-code search fails on some pictures; one code is by far the common case.
            results = reader.Decode(source) is { } single ? [single] : null;
        }
        return results?.Select(r => r.Text).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList() ?? [];
    }
}

/// <summary>Tools wheel: two or more pictures side by side in one image (a grid on white, gaps between).</summary>
internal sealed class CollageOperation : IOperation
{
    public string Id => "image.collage";
    public WheelKind Wheel => WheelKind.Tools;
    public string TitleKey => "op." + Id;
    public string? Badge => null;
    public string Glyph => "\uF0E2";
    public int Order => 5;

    public bool Accepts(InputSet input) => input.Count >= 2 && input.Count <= 36 && input.AllAre(FileCategory.Image);

    public Task<OperationResult> RunAsync(OperationContext context, CancellationToken cancellation) =>
        Task.Run(() =>
        {
            var paths = context.Input.Paths;
            const int cell = 800, gap = 24;
            var columns = (int)Math.Ceiling(Math.Sqrt(paths.Count));
            var rows = (int)Math.Ceiling(paths.Count / (double)columns);
            using var canvas = new MagickImage(MagickColors.White, (uint)(columns * cell + (columns + 1) * gap), (uint)(rows * cell + (rows + 1) * gap));
            for (var i = 0; i < paths.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                context.Report(0.9 * i / paths.Count, Path.GetFileName(paths[i]));
                using var frames = ImageSupport.Load(paths[i]);
                using var picture = (MagickImage)frames[0].Clone();
                ImageSupport.ToSrgbIfProfiled(picture);
                picture.Resize(new MagickGeometry(cell, cell)); // fit inside the cell, proportions kept
                var x = gap + i % columns * (cell + gap) + (cell - (int)picture.Width) / 2;
                var y = gap + i / columns * (cell + gap) + (cell - (int)picture.Height) / 2;
                canvas.Composite(picture, x, y, CompositeOperator.Over);
            }
            var target = context.Output.PathFor(paths[0], "jpg", "collage");
            canvas.Quality = 90;
            canvas.Write(target, MagickFormat.Jpeg);
            context.Report(1);
            return OperationResult.Of(target);
        }, cancellation);
}
