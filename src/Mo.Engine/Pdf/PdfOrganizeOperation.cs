using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Mo.Engine.Pdf;

/// <summary>One page of the new document: which page of the original (0-based) and how far to turn it (0, 90, 180, 270).</summary>
public sealed record PdfPageChoice(int Index, int Rotation = 0);

/// <summary>
/// Tools wheel: opens the page arranger. The arranger runs this operation with the option "pages": the pages in their
/// new order — pages left out are removed, pages listed twice are duplicated, each with its own turn.
/// </summary>
internal sealed class PdfOrganizeOperation : PdfOperation, IOperation
{
    public override string Id => "pdf.organize";
    public override WheelKind Wheel => WheelKind.Tools;
    public override string Glyph => "\uE8A9";
    public override int Order => 5;
    public string? EditorId => "pdf.pages";

    public override bool Accepts(InputSet input) => input.IsSingle && base.Accepts(input);

    protected override Task<IReadOnlyList<string>> ProcessFileAsync(OperationContext context, string source, int index, CancellationToken cancellation)
    {
        var pages = context.Option<IReadOnlyList<PdfPageChoice>?>("pages", null) ?? throw new OperationException("error.edit-needs-editor");
        if (pages.Count == 0) throw new OperationException("error.pdf-no-pages", Path.GetFileName(source));

        using var original = PdfSupport.Open(source, PdfDocumentOpenMode.Import);
        using var result = new PdfDocument();
        foreach (var choice in pages)
        {
            cancellation.ThrowIfCancellationRequested();
            if (choice.Index < 0 || choice.Index >= original.PageCount) throw new OperationException("error.pdf-no-pages", Path.GetFileName(source));
            var page = result.AddPage(original.Pages[choice.Index]);
            page.Rotate = (((page.Rotate + choice.Rotation) % 360) + 360) % 360;
        }
        var target = context.Output.PathFor(source, "pdf", "arranged");
        PdfSupport.Save(result, target, source);
        return Task.FromResult<IReadOnlyList<string>>([target]);
    }
}
