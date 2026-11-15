using Mo.Engine.Office;
using Mo.Engine.Pdf.Export;

namespace Mo.Engine.Docs;

/// <summary>How a conversion is done: inside Mo Converter, or with LibreOffice when it is installed.</summary>
internal enum Route { Managed, Office, MarkdownViaWord }

/// <summary>
/// Convert wheel for documents. Word, Markdown and text are converted by Mo Converter itself; anything that needs a
/// page layout engine (PDF output, old .doc, OpenDocument, RTF) uses LibreOffice and is only offered when it is installed.
/// </summary>
internal sealed class DocumentConvertOperation(string target, string badge, string glyph, int order) : IOperation
{
    public string Id => "doc.to-" + target;
    public WheelKind Wheel => WheelKind.Convert;
    public string TitleKey => "op." + Id;
    public string? Badge => badge;
    public string Glyph => glyph;
    public int Order => order;

    private static readonly string[] Markdown = ["md", "markdown"];
    private static readonly string[] Html = ["html", "htm"];
    private static readonly string[] OfficeOnly = ["doc", "odt", "rtf"];

    /// <summary>The way to turn <paramref name="source"/> into <paramref name="target"/>, or null when it is not possible.</summary>
    internal static Route? RouteFor(string source, string target)
    {
        if (source == target || (Html.Contains(source) && target == "html") || (Markdown.Contains(source) && target == "md")) return null;
        var office = LibreOffice.IsAvailable;
        Route? ifOffice = office ? Route.Office : null;
        return target switch
        {
            "pdf" when Markdown.Contains(source) => office ? Route.MarkdownViaWord : null,
            "pdf" when source is "docx" or "txt" || OfficeOnly.Contains(source) || Html.Contains(source) => ifOffice,
            "docx" when Markdown.Contains(source) || source == "txt" => Route.Managed,
            "docx" when OfficeOnly.Contains(source) || Html.Contains(source) => ifOffice,
            "odt" when source is "docx" or "doc" or "rtf" => ifOffice,
            "md" when source is "docx" or "txt" => Route.Managed,
            "txt" when source == "docx" || Markdown.Contains(source) => Route.Managed,
            "txt" when OfficeOnly.Contains(source) || Html.Contains(source) => ifOffice,
            "html" when source == "docx" || source == "txt" || Markdown.Contains(source) => Route.Managed,
            "html" when OfficeOnly.Contains(source) => ifOffice,
            "epub" when source is "docx" or "txt" || Markdown.Contains(source) => Route.Managed,
            "epub" when OfficeOnly.Contains(source) => ifOffice,
            _ => null,
        };
    }

    public bool Accepts(InputSet input) =>
        input.AllAre(FileCategory.Document) && input.Extensions.All(e => RouteFor(e, target) is not null);

    public Task<OperationResult> RunAsync(OperationContext context, CancellationToken cancellation) =>
        Task.Run(async () =>
        {
            var outputs = new List<string>();
            var paths = context.Input.Paths;
            for (var i = 0; i < paths.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var source = paths[i];
                if (!File.Exists(source)) throw new OperationException("error.file-missing", Path.GetFileName(source));
                context.Report((double)i / paths.Count, Path.GetFileName(source));
                outputs.AddRange(await ConvertAsync(context, source, cancellation));
            }
            context.Report(1);
            return OperationResult.Of([.. outputs]);
        }, cancellation);

    private async Task<IReadOnlyList<string>> ConvertAsync(OperationContext context, string source, CancellationToken cancellation)
    {
        var extension = FileTypes.ExtensionOf(source);
        var route = RouteFor(extension, target) ?? throw new OperationException("error.document-unsupported", Path.GetFileName(source));
        var output = context.Output.PathFor(source, target);
        var written = new List<string> { output };
        try
        {
            switch (route)
            {
                case Route.Office:
                    await LibreOffice.ConvertAsync(source, OfficeFormat(target), output, cancellation);
                    break;

                case Route.MarkdownViaWord:
                    var word = Path.Combine(Directory.CreateTempSubdirectory("mo-md-").FullName, Path.GetFileNameWithoutExtension(source) + ".docx");
                    try
                    {
                        DocxWriter.Write(DocumentReaders.Read(source), word);
                        await LibreOffice.ConvertAsync(word, "pdf", output, cancellation);
                    }
                    finally
                    {
                        try { Directory.Delete(Path.GetDirectoryName(word)!, true); } catch (IOException) { }
                    }
                    break;

                case Route.Managed when target == "html" && Markdown.Contains(extension):
                    // Markdig's own HTML keeps links, emphasis and code formatting.
                    var markdown = TextFiles.Read(source);
                    var body = Markdig.Markdown.ToHtml(markdown, DocumentReaders.Pipeline);
                    var layout = DocumentReaders.Read(source);
                    File.WriteAllText(output, HtmlWriter.Page(Path.GetFileNameWithoutExtension(source), layout.MostlyRightToLeft, body),
                        new System.Text.UTF8Encoding(false));
                    break;

                case Route.Managed:
                    var document = DocumentReaders.Read(source);
                    if (!document.AllBlocks.Any()) throw new OperationException("error.document-empty", Path.GetFileName(source));
                    switch (target)
                    {
                        case "docx": DocxWriter.Write(document, output); break;
                        case "md": written = [.. MarkdownWriter.Write(document, output)]; break;
                        case "txt": PlainTextWriter.Write(document, output); break;
                        case "html": HtmlWriter.Write(document, output, Path.GetFileNameWithoutExtension(source)); break;
                        case "epub": EpubWriter.Write(document, output, Path.GetFileNameWithoutExtension(source)); break;
                        default: throw new NotSupportedException(target);
                    }
                    break;
            }
        }
        catch (Exception e) when (e is not OperationException and not OperationCanceledException)
        {
            foreach (var path in written) TryDelete(path);
            throw new OperationException("error.document-failed", Path.GetFileName(source));
        }
        catch
        {
            foreach (var path in written) TryDelete(path);
            throw;
        }
        return written;
    }

    internal static string OfficeFormat(string target) => target switch
    {
        "txt" => "txt:Text (encoded):UTF8",
        // Named filter: plain "docx" has no export filter when the source opens as a web page (HTML).
        "docx" => "docx:MS Word 2007 XML",
        "html" => "html:XHTML Writer File:UTF8",
        "csv" => "csv:Text - txt - csv (StarCalc):44,34,76,1",
        _ => target,
    };

    internal static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
