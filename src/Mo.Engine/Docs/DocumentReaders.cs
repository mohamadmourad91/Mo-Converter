using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ImageMagick;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Mo.Engine.Office;
using Mo.Engine.Pdf.Layout;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using Lay = Mo.Engine.Pdf.Layout;
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableCell = Markdig.Extensions.Tables.TableCell;
using MdTableRow = Markdig.Extensions.Tables.TableRow;
using WTable = DocumentFormat.OpenXml.Wordprocessing.Table;

namespace Mo.Engine.Docs;

/// <summary>Reads Word, Markdown and text files into the same layout model the PDF reader produces.</summary>
public static class DocumentReaders
{
    public static LayoutDocument Read(string path) => FileTypes.ExtensionOf(path) switch
    {
        "docx" => Docx(path),
        "md" or "markdown" => Markdown(path),
        "txt" => PlainText(path),
        var other => throw new NotSupportedException(other),
    };

    private static LayoutDocument Single(string? title, List<LayoutBlock> blocks) =>
        new(string.IsNullOrWhiteSpace(title) ? null : title.Trim(), [new LayoutPage(1, 595, 842, blocks)]);

    // ---------- plain text ----------

    /// <summary>One paragraph per non-empty line: hard line breaks in text files usually mean something.</summary>
    public static LayoutDocument PlainText(string path)
    {
        var blocks = TextFiles.Lines(TextFiles.Read(path))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => (LayoutBlock)new Lay.ParagraphBlock(l.TrimEnd(), 0, false, Script.IsMostlyRightToLeft(l)))
            .ToList();
        return Single(null, blocks);
    }

    // ---------- Word ----------

    public static LayoutDocument Docx(string path)
    {
        WordprocessingDocument document;
        try
        {
            document = WordprocessingDocument.Open(path, false);
        }
        catch (Exception e) when (e is OpenXmlPackageException or InvalidDataException or IOException or FileFormatException)
        {
            throw new OperationException("error.document-unreadable", Path.GetFileName(path));
        }

        using (document)
        {
            var main = document.MainDocumentPart;
            var body = main?.Document?.Body ?? throw new OperationException("error.document-unreadable", Path.GetFileName(path));
            var headings = HeadingStyles(main);
            var blocks = new List<LayoutBlock>();
            foreach (var element in body.Elements()) AddWordElement(element, main, headings, blocks);
            return Single(document.PackageProperties.Title, blocks);
        }
    }

    private static void AddWordElement(OpenXmlElement element, MainDocumentPart main, Dictionary<string, int> headings, List<LayoutBlock> blocks)
    {
        switch (element)
        {
            case Paragraph paragraph:
                foreach (var picture in Pictures(paragraph, main)) blocks.Add(picture);
                var text = WordText(paragraph);
                if (string.IsNullOrWhiteSpace(text)) return;
                var properties = paragraph.ParagraphProperties;
                var level = properties?.ParagraphStyleId?.Val?.Value is { } style && headings.TryGetValue(style, out var l) ? l
                    : properties?.OutlineLevel?.Val?.Value is { } outline && outline < 9 ? Math.Min(outline + 1, 2) : 0;
                if (properties?.NumberingProperties is not null && level == 0) text = "\u2022 " + text;
                var rightToLeft = properties?.BiDi is not null || Script.IsMostlyRightToLeft(text);
                var bold = level == 0 && paragraph.Descendants<Run>().Any() &&
                           paragraph.Descendants<Run>().Where(r => r.InnerText.Trim().Length > 0).All(r => r.RunProperties?.Bold is not null);
                blocks.Add(new Lay.ParagraphBlock(text.Trim(), level, bold, rightToLeft));
                break;

            case WTable table:
                var rows = table.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>()
                    .Select(r => r.Elements<DocumentFormat.OpenXml.Wordprocessing.TableCell>().Select(c => string.Join(" ", c.Elements<Paragraph>().Select(WordText)).Trim()).ToList())
                    .Where(r => r.Count > 0)
                    .ToList();
                if (rows.Count == 0) return;
                var width = rows.Max(r => r.Count);
                var padded = rows.Select(r => (IReadOnlyList<string>)r.Concat(Enumerable.Repeat("", width - r.Count)).ToList()).ToList();
                var rtl = table.GetFirstChild<TableProperties>()?.BiDiVisual is not null
                          || Script.IsMostlyRightToLeft(string.Concat(padded.SelectMany(r => r)));
                blocks.Add(new TableBlock(padded, rtl));
                break;

            case SdtBlock content:
                // Content controls (forms, tables of contents) wrap ordinary paragraphs.
                foreach (var child in content.SdtContentBlock?.Elements() ?? []) AddWordElement(child, main, headings, blocks);
                break;
        }
    }

    /// <summary>Paragraph text with tabs and line breaks kept as spacing.</summary>
    private static string WordText(Paragraph paragraph)
    {
        var text = new System.Text.StringBuilder();
        foreach (var node in paragraph.Descendants())
        {
            switch (node)
            {
                case Text t: text.Append(t.Text); break;
                case TabChar: text.Append('\t'); break;
                case Break or CarriageReturn: text.Append(' '); break;
                case NoBreakHyphen: text.Append('-'); break;
            }
        }
        return text.ToString();
    }

    /// <summary>Style id → heading level 1 or 2, from the style names ("heading 1", "Title") or their outline level.</summary>
    private static Dictionary<string, int> HeadingStyles(MainDocumentPart main)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Title"] = 1, ["Heading1"] = 1, ["Heading2"] = 2, ["Heading3"] = 2 };
        foreach (var style in main.StyleDefinitionsPart?.Styles?.Elements<Style>() ?? [])
        {
            if (style.StyleId?.Value is not { } id) continue;
            var name = style.StyleName?.Val?.Value ?? "";
            if (name.Equals("Title", StringComparison.OrdinalIgnoreCase)) result[id] = 1;
            else if (name.StartsWith("heading ", StringComparison.OrdinalIgnoreCase) && int.TryParse(name[8..], out var n)) result[id] = Math.Min(n, 2);
            else if (style.StyleParagraphProperties?.OutlineLevel?.Val?.Value is { } outline && outline < 9) result[id] = Math.Min(outline + 1, 2);
        }
        return result;
    }

    private static IEnumerable<ImageBlock> Pictures(Paragraph paragraph, MainDocumentPart main)
    {
        foreach (var blip in paragraph.Descendants<A.Blip>())
        {
            if (blip.Embed?.Value is not { } id || main.GetPartById(id) is not ImagePart part) continue;
            byte[] bytes;
            using (var stream = part.GetStream()) { using var copy = new MemoryStream(); stream.CopyTo(copy); bytes = copy.ToArray(); }
            var png = ToPng(bytes);
            if (png is null) continue;
            var extent = blip.Ancestors<DW.Inline>().FirstOrDefault()?.Extent ?? blip.Ancestors<DW.Anchor>().FirstOrDefault()?.Extent;
            var widthPt = extent?.Cx?.Value is { } cx ? cx / 12700.0 : 300;
            var heightPt = extent?.Cy?.Value is { } cy ? cy / 12700.0 : 200;
            yield return new ImageBlock(png, widthPt, heightPt);
        }
    }

    internal static byte[]? ToPng(byte[] bytes)
    {
        if (bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == (byte)'P') return bytes;
        try
        {
            using var image = new MagickImage(bytes);
            Mo.Engine.Images.ImageSupport.PreparePng(image);
            return image.ToByteArray(MagickFormat.Png);
        }
        catch (MagickException)
        {
            return null; // WMF/EMF drawings and other formats we cannot show
        }
    }

    // ---------- Markdown ----------

    public static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public static LayoutDocument Markdown(string path)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var document = Markdig.Markdown.Parse(TextFiles.Read(path), Pipeline);
        var blocks = new List<LayoutBlock>();
        foreach (var block in document) AddMarkdownBlock(block, folder, blocks, "");
        return Single(null, blocks);
    }

    private static void AddMarkdownBlock(Block block, string folder, List<LayoutBlock> blocks, string prefix)
    {
        switch (block)
        {
            case HeadingBlock heading:
                var title = Inline(heading.Inline);
                blocks.Add(new Lay.ParagraphBlock(title, Math.Min(heading.Level, 2), false, Script.IsMostlyRightToLeft(title)));
                break;

            case Markdig.Syntax.ParagraphBlock paragraph:
                foreach (var image in MarkdownImages(paragraph.Inline, folder)) blocks.Add(image);
                var text = Inline(paragraph.Inline).Trim();
                if (text.Length > 0) blocks.Add(new Lay.ParagraphBlock(prefix + text, 0, false, Script.IsMostlyRightToLeft(text)));
                break;

            case ListBlock list:
                var number = int.TryParse(list.OrderedStart, out var start) ? start : 1;
                foreach (var item in list.OfType<ListItemBlock>())
                {
                    var marker = list.IsOrdered ? $"{number++}. " : "\u2022 ";
                    var first = true;
                    foreach (var child in item)
                    {
                        AddMarkdownBlock(child, folder, blocks, first ? prefix + marker : prefix + "   ");
                        first = false;
                    }
                }
                break;

            case QuoteBlock quote:
                foreach (var child in quote) AddMarkdownBlock(child, folder, blocks, prefix);
                break;

            case CodeBlock code:
                foreach (var line in code.Lines.Lines.Take(code.Lines.Count))
                    blocks.Add(new Lay.ParagraphBlock(prefix + line.ToString(), 0, false, false));
                break;

            case MdTable table:
                var rows = table.OfType<MdTableRow>()
                    .Select(r => (IReadOnlyList<string>)r.OfType<MdTableCell>()
                        .Select(c => string.Join(" ", c.OfType<Markdig.Syntax.ParagraphBlock>().Select(p => Inline(p.Inline))).Trim())
                        .ToList())
                    .ToList();
                if (rows.Count == 0) return;
                var width = rows.Max(r => r.Count);
                var padded = rows.Select(r => (IReadOnlyList<string>)r.Concat(Enumerable.Repeat("", width - r.Count)).ToList()).ToList();
                blocks.Add(new TableBlock(padded, Script.IsMostlyRightToLeft(string.Concat(padded.SelectMany(r => r)))));
                break;

            case ContainerBlock container:
                foreach (var child in container) AddMarkdownBlock(child, folder, blocks, prefix);
                break;
        }
    }

    /// <summary>Plain text of inline Markdown: emphasis and links keep their words, code keeps its content.</summary>
    private static string Inline(ContainerInline? inline)
    {
        if (inline is null) return "";
        var text = new System.Text.StringBuilder();
        foreach (var node in inline)
        {
            switch (node)
            {
                case LiteralInline literal: text.Append(literal.Content.ToString()); break;
                case CodeInline code: text.Append(code.Content); break;
                case LineBreakInline: text.Append(' '); break;
                case AutolinkInline link: text.Append(link.Url); break;
                case LinkInline { IsImage: true }: break;
                case ContainerInline container: text.Append(Inline(container)); break;
            }
        }
        return text.ToString();
    }

    /// <summary>Pictures referenced with a local path next to the Markdown file.</summary>
    private static IEnumerable<ImageBlock> MarkdownImages(ContainerInline? inline, string folder)
    {
        if (inline is null) yield break;
        foreach (var link in inline.Descendants<LinkInline>().Where(l => l.IsImage && !string.IsNullOrEmpty(l.Url)))
        {
            var url = Uri.UnescapeDataString(link.Url!);
            if (url.Contains("://")) continue; // web pictures are not downloaded
            var file = Path.GetFullPath(Path.Combine(folder, url));
            if (!File.Exists(file)) continue;
            var png = ToPng(File.ReadAllBytes(file));
            if (png is null) continue;
            using var image = new MagickImage(png);
            yield return new ImageBlock(png, image.Width * 0.75, image.Height * 0.75);
        }
    }
}
