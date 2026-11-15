using Mo.Engine.Pdf.Export;
using Mo.Engine.Pdf.Layout;

namespace Mo.Engine.Pdf;

/// <summary>
/// Convert wheel: PDF to Word, Excel, PowerPoint, Markdown or EPUB. Word, Excel, Markdown and EPUB are built from
/// the page layout (headings, paragraphs, detected tables, pictures); PowerPoint shows every page as a slide picture.
/// </summary>
internal sealed class PdfExportOperation(string extension, string badge, string glyph, int order) : PdfOperation
{
    public override string Id => "pdf.to-" + extension;
    public override WheelKind Wheel => WheelKind.Convert;
    public override string? Badge => badge;
    public override string Glyph => glyph;
    public override int Order => order;

    protected override async Task<IReadOnlyList<string>> ProcessFileAsync(OperationContext context, string source, int index, CancellationToken cancellation)
    {
        if (extension == "pptx") return [await Slides(context, source, index, cancellation)];

        var layout = PdfLayoutReader.Read(source, cancellation);
        if (!layout.AllBlocks.Any()) throw new OperationException("error.pdf-no-text", Path.GetFileName(source));
        if (extension == "xlsx" && !layout.HasText) throw new OperationException("error.pdf-no-text", Path.GetFileName(source));
        cancellation.ThrowIfCancellationRequested();

        var target = context.Output.PathFor(source, extension);
        var written = new List<string> { target };
        try
        {
            var t = Localizer.Current;
            switch (extension)
            {
                case "docx":
                    DocxWriter.Write(layout, target);
                    break;
                case "xlsx":
                    XlsxWriter.Write(layout, target, n => t.T("export.table", n), t.T("export.text"));
                    break;
                case "md":
                    written = [.. MarkdownWriter.Write(layout, target)];
                    break;
                case "epub":
                    EpubWriter.Write(layout, target, Path.GetFileNameWithoutExtension(source));
                    break;
                default:
                    throw new NotSupportedException(extension);
            }
        }
        catch (Exception e) when (e is not OperationException and not OperationCanceledException)
        {
            foreach (var path in written) PdfSupport.TryDelete(path);
            throw new OperationException("error.pdf-write-failed", Path.GetFileName(source));
        }
        context.Report((index + 1.0) / context.Input.Count);
        return written;
    }

    private static async Task<string> Slides(OperationContext context, string source, int index, CancellationToken cancellation)
    {
        var pages = new List<SlideImage>();
        await PdfRenderer.RenderAsync(source, 150, async (i, count, size, png) =>
        {
            using var buffer = new MemoryStream();
            await png.CopyToAsync(buffer, cancellation);
            pages.Add(new SlideImage(buffer.ToArray(), size.Width, size.Height));
            context.Report((index + 0.9 * (i + 1.0) / count) / context.Input.Count);
        }, cancellation);
        if (pages.Count == 0) throw new OperationException("error.pdf-unreadable", Path.GetFileName(source));

        var target = context.Output.PathFor(source, "pptx");
        try
        {
            PptxWriter.Write(pages, target, null);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            PdfSupport.TryDelete(target);
            throw new OperationException("error.pdf-write-failed", Path.GetFileName(source));
        }
        return target;
    }
}
