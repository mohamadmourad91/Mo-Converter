using Mo.Engine.Docs;
using Mo.Engine.Office;
using Mo.Engine.Pdf;
using Mo.Engine.Pdf.Export;
using Mo.Engine.Pdf.Layout;

namespace Mo.Engine.Spreadsheets;

/// <summary>Shared loop: one file after another, outputs collected, a half-written result removed on failure.</summary>
internal abstract class FileByFileOperation : IOperation
{
    public abstract string Id { get; }
    public abstract WheelKind Wheel { get; }
    public string TitleKey => "op." + Id;
    public abstract string? Badge { get; }
    public abstract string Glyph { get; }
    public abstract int Order { get; }
    public abstract bool Accepts(InputSet input);

    protected abstract Task<IReadOnlyList<string>> ProcessAsync(OperationContext context, string source, CancellationToken cancellation);

    public Task<OperationResult> RunAsync(OperationContext context, CancellationToken cancellation) =>
        Task.Run(async () =>
        {
            var outputs = new List<string>();
            var paths = context.Input.Paths;
            for (var i = 0; i < paths.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!File.Exists(paths[i])) throw new OperationException("error.file-missing", Path.GetFileName(paths[i]));
                context.Report((double)i / paths.Count, Path.GetFileName(paths[i]));
                outputs.AddRange(await ProcessAsync(context, paths[i], cancellation));
            }
            context.Report(1);
            return OperationResult.Of([.. outputs]);
        }, cancellation);

    protected static async Task<string> Guarded(string source, string target, Func<Task> work)
    {
        try
        {
            await work();
            return target;
        }
        catch (Exception e) when (e is not OperationException and not OperationCanceledException)
        {
            DocumentConvertOperation.TryDelete(target);
            throw new OperationException("error.document-failed", Path.GetFileName(source));
        }
        catch
        {
            DocumentConvertOperation.TryDelete(target);
            throw;
        }
    }

    /// <summary>Runs <paramref name="work"/> with a temporary folder that is always removed afterwards.</summary>
    protected static async Task WithTemp(Func<string, Task> work)
    {
        var folder = Directory.CreateTempSubdirectory("mo-sheet-").FullName;
        try { await work(folder); }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } }
    }
}

/// <summary>Convert wheel: CSV/TSV to Excel (numbers become real numbers); old .xls and .ods via LibreOffice.</summary>
internal sealed class ToXlsxOperation : FileByFileOperation
{
    public override string Id => "sheet.to-xlsx";
    public override WheelKind Wheel => WheelKind.Convert;
    public override string? Badge => "XLSX";
    public override string Glyph => "\uE80A";
    public override int Order => 10;

    public override bool Accepts(InputSet input) =>
        input.AllAre(FileCategory.Spreadsheet) && input.Extensions.All(e => e is "csv" or "tsv" || (e is "xls" or "ods" && LibreOffice.IsAvailable));

    protected override async Task<IReadOnlyList<string>> ProcessAsync(OperationContext context, string source, CancellationToken cancellation)
    {
        var target = context.Output.PathFor(source, "xlsx");
        var extension = FileTypes.ExtensionOf(source);
        await Guarded(source, target, async () =>
        {
            if (extension is "xls" or "ods")
            {
                await LibreOffice.ConvertAsync(source, "xlsx", target, cancellation);
                return;
            }
            var text = TextFiles.Read(source);
            var rows = Csv.Parse(text, extension == "tsv" ? '\t' : Csv.DetectSeparator(text));
            if (rows.Count == 0) throw new OperationException("error.document-empty", Path.GetFileName(source));
            var width = rows.Max(r => r.Count);
            var table = new TableBlock(rows.Select(r => (IReadOnlyList<string>)r.Concat(Enumerable.Repeat("", width - r.Count)).ToList()).ToList(),
                Script.IsMostlyRightToLeft(text));
            var layout = new LayoutDocument(null, [new LayoutPage(1, 595, 842, [table])]);
            var name = Path.GetFileNameWithoutExtension(source);
            XlsxWriter.Write(layout, target, _ => name, name);
        });
        return [target];
    }
}

/// <summary>
/// Convert wheel: Excel to CSV in the separator of the user's region (what Excel itself expects when opening it).
/// A workbook with several sheets becomes a folder with one CSV per sheet.
/// </summary>
internal sealed class ToCsvOperation : FileByFileOperation
{
    public override string Id => "sheet.to-csv";
    public override WheelKind Wheel => WheelKind.Convert;
    public override string? Badge => "CSV";
    public override string Glyph => "\uE80A";
    public override int Order => 20;

    public override bool Accepts(InputSet input) =>
        input.AllAre(FileCategory.Spreadsheet) && input.Extensions.All(e => e is "xlsx" or "tsv" || (e is "xls" or "ods" && LibreOffice.IsAvailable));

    protected override async Task<IReadOnlyList<string>> ProcessAsync(OperationContext context, string source, CancellationToken cancellation)
    {
        var extension = FileTypes.ExtensionOf(source);
        var separator = Csv.RegionalSeparator();
        if (extension == "tsv")
        {
            var target = context.Output.PathFor(source, "csv");
            await Guarded(source, target, () =>
            {
                TextFiles.Write(target, Csv.Write(Csv.Parse(TextFiles.Read(source), '\t'), separator));
                return Task.CompletedTask;
            });
            return [target];
        }

        List<SheetData> sheets = [];
        if (extension == "xlsx") sheets = XlsxReader.Read(source);
        else await WithTemp(async folder =>
        {
            var xlsx = Path.Combine(folder, "converted.xlsx");
            await LibreOffice.ConvertAsync(source, "xlsx", xlsx, cancellation);
            sheets = XlsxReader.Read(xlsx);
        });

        var filled = sheets.Where(s => s.Rows.Count > 0).ToList();
        if (filled.Count == 0) throw new OperationException("error.document-empty", Path.GetFileName(source));
        if (filled.Count == 1)
        {
            var target = context.Output.PathFor(source, "csv");
            await Guarded(source, target, () => { TextFiles.Write(target, Csv.Write(filled[0].Rows, separator)); return Task.CompletedTask; });
            return [target];
        }

        var outputFolder = context.Output.FolderFor(source, "sheets");
        var written = new List<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in filled)
        {
            var safe = string.Concat(sheet.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
            if (safe.Length == 0) safe = "Sheet";
            var name = safe;
            for (var n = 2; !names.Add(name); n++) name = $"{safe} ({n})";
            var path = Path.Combine(outputFolder, name + ".csv");
            TextFiles.Write(path, Csv.Write(sheet.Rows, separator));
            written.Add(path);
        }
        return written;
    }
}

/// <summary>Convert wheel: an office file through LibreOffice ("pdf", "ods", "pptx", ...).</summary>
internal sealed class OfficeConvertOperation(string id, string format, string extension, string badge, string glyph, int order,
    FileCategory category, params string[] sources) : FileByFileOperation
{
    public override string Id => id;
    public override WheelKind Wheel => WheelKind.Convert;
    public override string? Badge => badge;
    public override string Glyph => glyph;
    public override int Order => order;

    public override bool Accepts(InputSet input) =>
        LibreOffice.IsAvailable && input.AllAre(category) && input.Extensions.All(sources.Contains);

    protected override async Task<IReadOnlyList<string>> ProcessAsync(OperationContext context, string source, CancellationToken cancellation)
    {
        var target = context.Output.PathFor(source, extension);
        await Guarded(source, target, () => LibreOffice.ConvertAsync(source, format, target, cancellation));
        return [target];
    }
}

/// <summary>Convert wheel: every slide as a PNG picture (LibreOffice makes a PDF, Windows renders its pages).</summary>
internal sealed class SlidesToImagesOperation : FileByFileOperation
{
    public override string Id => "slides.to-png";
    public override WheelKind Wheel => WheelKind.Convert;
    public override string? Badge => "PNG";
    public override string Glyph => "\uEB9F";
    public override int Order => 20;

    public override bool Accepts(InputSet input) =>
        LibreOffice.IsAvailable && input.AllAre(FileCategory.Presentation);

    protected override async Task<IReadOnlyList<string>> ProcessAsync(OperationContext context, string source, CancellationToken cancellation)
    {
        var written = new List<string>();
        await WithTemp(async folder =>
        {
            var pdf = Path.Combine(folder, "slides.pdf");
            await LibreOffice.ConvertAsync(source, "pdf", pdf, cancellation);
            string? outputFolder = null;
            try
            {
                await PdfRenderer.RenderAsync(pdf, 150, async (i, count, _, png) =>
                {
                    if (count > 1) outputFolder ??= context.Output.FolderFor(source, "slides");
                    var target = outputFolder is null
                        ? context.Output.PathFor(source, "png")
                        : Path.Combine(outputFolder, PdfSupport.PageName(source, i + 1, count, "png"));
                    await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
                    await png.CopyToAsync(output, cancellation);
                    written.Add(target);
                }, cancellation);
            }
            catch
            {
                foreach (var path in written) DocumentConvertOperation.TryDelete(path);
                throw;
            }
        });
        return written;
    }
}
