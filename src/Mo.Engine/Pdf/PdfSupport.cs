using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Mo.Engine.Pdf;

/// <summary>Opening PDFs safely and turning library errors into messages the user understands.</summary>
internal static class PdfSupport
{
    public static bool AllPdf(InputSet input) => input.AllAre(FileCategory.Pdf);

    /// <summary>Opens a PDF with PDFsharp. Protected or broken files become a localized <see cref="OperationException"/>.</summary>
    public static PdfDocument Open(string path, PdfDocumentOpenMode mode)
    {
        try
        {
            return PdfReader.Open(path, mode);
        }
        catch (Exception e) when (IsPasswordError(e))
        {
            throw new OperationException("error.pdf-protected", Path.GetFileName(path));
        }
        catch (Exception e) when (e is not OperationException and not OperationCanceledException)
        {
            throw new OperationException("error.pdf-unreadable", Path.GetFileName(path));
        }
    }

    private static bool IsPasswordError(Exception e) =>
        e is PdfReaderException ||
        e.Message.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        e.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase);

    /// <summary>Saves to a new file; a half-written file is removed when saving fails.</summary>
    public static void Save(PdfDocument document, string target, string source)
    {
        try
        {
            document.Save(target);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            TryDelete(target);
            throw new OperationException("error.pdf-write-failed", Path.GetFileName(source));
        }
    }

    public static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>"report-p007.pdf": numbered with enough digits for the page count so files sort correctly.</summary>
    public static string PageName(string source, int pageNumber, int pageCount, string extension)
    {
        var digits = Math.Max(2, pageCount.ToString().Length);
        return $"{Path.GetFileNameWithoutExtension(source)}-p{pageNumber.ToString().PadLeft(digits, '0')}.{extension}";
    }
}

/// <summary>Base for operations that handle each PDF on its own.</summary>
internal abstract class PdfOperation : IOperation
{
    public abstract string Id { get; }
    public abstract WheelKind Wheel { get; }
    public string TitleKey => "op." + Id;
    public virtual string? Badge => null;
    public abstract string Glyph { get; }
    public abstract int Order { get; }

    public virtual bool Accepts(InputSet input) => PdfSupport.AllPdf(input);

    /// <summary>Handles one file and returns the paths written.</summary>
    protected abstract Task<IReadOnlyList<string>> ProcessFileAsync(OperationContext context, string source, int index, CancellationToken cancellation);

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
                outputs.AddRange(await ProcessFileAsync(context, paths[i], i, cancellation));
            }
            context.Report(1);
            return OperationResult.Of([.. outputs]);
        }, cancellation);
}
