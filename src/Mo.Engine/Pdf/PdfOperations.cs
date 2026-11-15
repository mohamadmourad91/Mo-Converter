using ImageMagick;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace Mo.Engine.Pdf;

/// <summary>
/// Convert wheel: every page becomes a PNG or JPG. Rendering uses the PDF engine built into Windows
/// (Windows.Data.Pdf), so no extra library is shipped. One page → one file; more pages → a folder.
/// </summary>
internal sealed class PdfToImagesOperation(string extension, string badge, string glyph, int order) : PdfOperation
{
    /// <summary>Print-like sharpness without huge files.</summary>
    public const double Dpi = 150;

    public override string Id => "pdf.to-" + extension;
    public override WheelKind Wheel => WheelKind.Convert;
    public override string? Badge => badge;
    public override string Glyph => glyph;
    public override int Order => order;

    protected override async Task<IReadOnlyList<string>> ProcessFileAsync(OperationContext context, string source, int index, CancellationToken cancellation)
    {
        string? folder = null;
        var written = new List<string>();
        try
        {
            await PdfRenderer.RenderAsync(source, Dpi, async (i, pages, _, png) =>
            {
                if (pages > 1) folder ??= context.Output.FolderFor(source, "pages");
                var target = folder is null
                    ? context.Output.PathFor(source, extension)
                    : Path.Combine(folder, PdfSupport.PageName(source, i + 1, pages, extension));

                await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
                    await png.CopyToAsync(output, cancellation);

                written.Add(target);
                context.Report((index + (i + 1.0) / pages) / context.Input.Count);
            }, cancellation, jpeg: extension == "jpg");
        }
        catch
        {
            foreach (var path in written) PdfSupport.TryDelete(path);
            throw;
        }
        return written;
    }
}
/// <summary>Convert wheel: the text of every page, in reading order, as a UTF-8 text file.</summary>
internal sealed class PdfToTextOperation : PdfOperation
{
    public override string Id => "pdf.to-txt";
    public override WheelKind Wheel => WheelKind.Convert;
    public override string? Badge => "TXT";
    public override string Glyph => "\uE8A5";
    public override int Order => 30;

    protected override async Task<IReadOnlyList<string>> ProcessFileAsync(OperationContext context, string source, int index, CancellationToken cancellation)
    {
        var parts = new List<string>();
        try
        {
            using var document = PigDocument.Open(source);
            foreach (var page in document.GetPages())
            {
                cancellation.ThrowIfCancellationRequested();
                parts.Add(ContentOrderTextExtractor.GetText(page).Trim());
            }
        }
        catch (Exception e) when (e is not OperationCanceledException and not OperationException)
        {
            throw new OperationException(
                e.Message.Contains("password", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase)
                    ? "error.pdf-protected" : "error.pdf-unreadable",
                Path.GetFileName(source));
        }

        // A scanned PDF holds pictures of text only; reading those needs OCR (a later phase).
        if (parts.All(string.IsNullOrWhiteSpace)) throw new OperationException("error.pdf-no-text", Path.GetFileName(source));

        var target = context.Output.PathFor(source, "txt");
        // A form feed between pages, like printers expect; BOM so every editor detects UTF-8 (Arabic, umlauts).
        await File.WriteAllTextAsync(target, string.Join(Environment.NewLine + "\f" + Environment.NewLine, parts) + Environment.NewLine,
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellation);
        return [target];
    }
}

/// <summary>Tools wheel: all dragged PDFs become one, in the order given.</summary>
internal sealed class PdfMergeOperation : IOperation
{
    public string Id => "pdf.merge";
    public WheelKind Wheel => WheelKind.Tools;
    public string TitleKey => "op." + Id;
    public string? Badge => null;
    public string Glyph => "\uE8F1";
    public int Order => 10;

    public bool Accepts(InputSet input) => input.Count >= 2 && PdfSupport.AllPdf(input);

    public Task<OperationResult> RunAsync(OperationContext context, CancellationToken cancellation) =>
        Task.Run(() =>
        {
            var paths = context.Input.Paths;
            using var merged = new PdfDocument();
            for (var i = 0; i < paths.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                context.Report(0.9 * i / paths.Count, Path.GetFileName(paths[i]));
                using var part = PdfSupport.Open(paths[i], PdfDocumentOpenMode.Import);
                foreach (var page in part.Pages) merged.AddPage(page);
            }
            cancellation.ThrowIfCancellationRequested();
            var target = context.Output.PathFor(paths[0], "pdf", "merged");
            PdfSupport.Save(merged, target, paths[0]);
            context.Report(1);
            return OperationResult.Of(target);
        }, cancellation);
}

/// <summary>Tools wheel: every page becomes its own PDF, collected in a folder.</summary>
internal sealed class PdfSplitOperation : PdfOperation
{
    public override string Id => "pdf.split";
    public override WheelKind Wheel => WheelKind.Tools;
    public override string Glyph => "\uE8F4";
    public override int Order => 20;

    protected override Task<IReadOnlyList<string>> ProcessFileAsync(OperationContext context, string source, int index, CancellationToken cancellation)
    {
        using var document = PdfSupport.Open(source, PdfDocumentOpenMode.Import);
        var count = document.PageCount;
        if (count < 2) throw new OperationException("error.pdf-single-page", Path.GetFileName(source));

        var folder = context.Output.FolderFor(source, "pages");
        var written = new List<string>();
        for (var i = 0; i < count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            using var single = new PdfDocument();
            single.AddPage(document.Pages[i]);
            var target = Path.Combine(folder, PdfSupport.PageName(source, i + 1, count, "pdf"));
            PdfSupport.Save(single, target, source);
            written.Add(target);
            context.Report((index + (i + 1.0) / count) / context.Input.Count);
        }
        return Task.FromResult<IReadOnlyList<string>>(written);
    }
}

/// <summary>Tools wheel: turns every page clockwise (option "degrees", default 90).</summary>
internal sealed class PdfRotateOperation : PdfOperation
{
    public override string Id => "pdf.rotate";
    public override WheelKind Wheel => WheelKind.Tools;
    public override string Glyph => "\uE7AD";
    public override int Order => 30;

    protected override Task<IReadOnlyList<string>> ProcessFileAsync(OperationContext context, string source, int index, CancellationToken cancellation)
    {
        var degrees = context.Option("degrees", 90);
        if (degrees % 90 != 0) degrees = 90;
        using var document = PdfSupport.Open(source, PdfDocumentOpenMode.Modify);
        foreach (var page in document.Pages)
            page.Rotate = (((page.Rotate + degrees) % 360) + 360) % 360;
        var target = context.Output.PathFor(source, "pdf", "rotated");
        PdfSupport.Save(document, target, source);
        return Task.FromResult<IReadOnlyList<string>>([target]);
    }
}

/// <summary>Tools wheel: removes title, author, dates, producer and XMP metadata.</summary>
internal sealed class PdfStripMetadataOperation : PdfOperation
{
    public override string Id => "pdf.strip-metadata";
    public override WheelKind Wheel => WheelKind.Tools;
    public override string Glyph => "\uE894";
    public override int Order => 50;

    protected override Task<IReadOnlyList<string>> ProcessFileAsync(OperationContext context, string source, int index, CancellationToken cancellation)
    {
        using var document = PdfSupport.Open(source, PdfDocumentOpenMode.Modify);
        document.Info.Elements.Clear();
        document.Internals.Catalog.Elements.Remove("/Metadata");
        foreach (var page in document.Pages) page.Elements.Remove("/Metadata");
        var target = context.Output.PathFor(source, "pdf", "clean");
        PdfSupport.Save(document, target, source);
        return Task.FromResult<IReadOnlyList<string>>([target]);
    }
}

/// <summary>
/// Tools wheel: makes PDFs smaller. Photos (JPEG streams in RGB or grey) are re-encoded at quality 70 and
/// at most 2000 px on the long side; content streams are compressed. The result is never bigger than the original.
/// </summary>
internal sealed class PdfCompressOperation : PdfOperation
{
    private const uint MaxSide = 2000;

    public override string Id => "pdf.compress";
    public override WheelKind Wheel => WheelKind.Tools;
    public override string Glyph => "\uE9A6";
    public override int Order => 40;

    protected override Task<IReadOnlyList<string>> ProcessFileAsync(OperationContext context, string source, int index, CancellationToken cancellation)
    {
        var level = context.Option("level", CompressionLevel.Balanced);
        var quality = level switch { CompressionLevel.Light => 82u, CompressionLevel.Strong => 55u, _ => 70u };
        var maxSide = level == CompressionLevel.Strong ? 1400u : MaxSide;

        var original = new FileInfo(source).Length;
        var target = context.Output.PathFor(source, "pdf", "compressed");
        using var document = PdfSupport.Open(source, PdfDocumentOpenMode.Modify);
        foreach (var item in document.Internals.GetAllObjects())
        {
            cancellation.ThrowIfCancellationRequested();
            if (item is PdfDictionary { Stream: not null } image && IsPlainJpeg(image)) Recompress(image, quality, maxSide);
        }

        document.Options.CompressContentStreams = true;
        document.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
        document.Options.NoCompression = false;

        using var buffer = new MemoryStream();
        try
        {
            document.Save(buffer, false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new OperationException("error.pdf-write-failed", Path.GetFileName(source));
        }

        if (buffer.Length < original) File.WriteAllBytes(target, buffer.ToArray());
        else File.Copy(source, target);
        return Task.FromResult<IReadOnlyList<string>>([target]);
    }

    /// <summary>Only JPEGs we can re-encode without changing their colours: RGB or grey, no decode array, 8 bits.</summary>
    private static bool IsPlainJpeg(PdfDictionary image)
    {
        var e = image.Elements;
        if (e.GetName("/Subtype") != "/Image") return false;
        var filter = e["/Filter"];
        var isJpeg = filter is PdfName { Value: "/DCTDecode" } ||
                     filter is PdfArray { Elements.Count: 1 } array && array.Elements[0] is PdfName { Value: "/DCTDecode" };
        if (!isJpeg || e.ContainsKey("/Decode") || e.ContainsKey("/DecodeParms")) return false;
        if (e.GetInteger("/BitsPerComponent") is not (0 or 8)) return false;
        var space = e.GetName("/ColorSpace");
        return space is "/DeviceRGB" or "/DeviceGray";
    }

    private static void Recompress(PdfDictionary image, uint quality, uint maxSide)
    {
        var bytes = image.Stream.Value;
        try
        {
            using var picture = new MagickImage(bytes);
            if (picture.Width > maxSide || picture.Height > maxSide) picture.Resize(new MagickGeometry(maxSide, maxSide));
            picture.Strip();
            picture.Quality = quality;
            picture.Settings.Interlace = Interlace.NoInterlace;
            var smaller = picture.ToByteArray(MagickFormat.Jpeg);
            if (smaller.Length >= bytes.Length) return;

            image.Stream.Value = smaller;
            image.Elements.SetInteger("/Length", smaller.Length);
            image.Elements.SetInteger("/Width", (int)picture.Width);
            image.Elements.SetInteger("/Height", (int)picture.Height);
        }
        catch (MagickException)
        {
            // Leave a picture we cannot read exactly as it was.
        }
    }
}

/// <summary>Registers every PDF operation.</summary>
public static class PdfModule
{
    public static void Register(OperationRegistry registry)
    {
        registry
            .Add(new PdfExportOperation("docx", "DOCX", "\uE8A5", 1))
            .Add(new PdfExportOperation("xlsx", "XLSX", "\uE80A", 2))
            .Add(new PdfExportOperation("pptx", "PPTX", "\uE7F4", 3))
            .Add(new PdfExportOperation("md", "MD", "\uE943", 40))
            .Add(new PdfExportOperation("epub", "EPUB", "\uE82D", 50))
            .Add(new PdfToImagesOperation("png", "PNG", "\uEB9F", 10))
            .Add(new PdfToImagesOperation("jpg", "JPG", "\uE722", 20))
            .Add(new PdfToTextOperation())
            .Add(new PdfMergeOperation())
            .Add(new PdfSplitOperation())
            .Add(new PdfRotateOperation())
            .Add(new PdfCompressOperation())
            .Add(new PdfStripMetadataOperation())
            .Add(new Mo.Engine.Images.PdfOcrOperation())
            .Add(new PdfOrganizeOperation());
    }
}
