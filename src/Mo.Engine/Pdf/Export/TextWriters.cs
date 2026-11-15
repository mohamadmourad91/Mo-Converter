using System.IO.Compression;
using ZipLevel = System.IO.Compression.CompressionLevel;
using System.Net;
using System.Text;
using Mo.Engine.Pdf.Layout;

namespace Mo.Engine.Pdf.Export;

/// <summary>Markdown: headings with #, tables as pipe tables, pictures saved next to the file in a folder.</summary>
public static class MarkdownWriter
{
    /// <returns>Every file written (the .md first, then pictures).</returns>
    public static IReadOnlyList<string> Write(LayoutDocument layout, string path)
    {
        var written = new List<string> { path };
        var imageFolderName = Path.GetFileNameWithoutExtension(path) + "-images";
        var imageFolder = Path.Combine(Path.GetDirectoryName(path)!, imageFolderName);
        var text = new StringBuilder();
        if (layout.Title is { } title && !layout.AllBlocks.OfType<ParagraphBlock>().Any(p => p.HeadingLevel == 1 && p.Text == title))
            text.Append("# ").Append(title).Append("\n\n");

        var imageNumber = 0;
        for (var p = 0; p < layout.Pages.Count; p++)
        {
            if (p > 0) text.Append("---\n\n");
            foreach (var block in layout.Pages[p].Blocks)
            {
                switch (block)
                {
                    case ParagraphBlock paragraph:
                        if (paragraph.HeadingLevel > 0) text.Append(new string('#', paragraph.HeadingLevel)).Append(' ').Append(Escape(paragraph.Text));
                        else if (paragraph.Bold) text.Append("**").Append(Escape(paragraph.Text)).Append("**");
                        else text.Append(Escape(paragraph.Text));
                        text.Append("\n\n");
                        break;
                    case TableBlock table:
                        AppendTable(text, table);
                        break;
                    case ImageBlock image:
                        Directory.CreateDirectory(imageFolder);
                        var name = $"image-{++imageNumber:D2}.png";
                        var imagePath = Path.Combine(imageFolder, name);
                        File.WriteAllBytes(imagePath, image.Png);
                        written.Add(imagePath);
                        text.Append($"![]({Uri.EscapeDataString(imageFolderName)}/{name})\n\n");
                        break;
                }
            }
        }
        File.WriteAllText(path, text.ToString().TrimEnd() + "\n", new UTF8Encoding(false));
        return written;
    }

    private static void AppendTable(StringBuilder text, TableBlock table)
    {
        for (var r = 0; r < table.Rows.Count; r++)
        {
            text.Append("| ").Append(string.Join(" | ", table.Rows[r].Select(Cell))).Append(" |\n");
            if (r == 0) text.Append('|').Append(string.Concat(Enumerable.Repeat(" --- |", table.ColumnCount))).Append('\n');
        }
        text.Append('\n');
    }

    private static string Cell(string value) => value.Replace("|", "\\|").Replace("\n", " ");

    /// <summary>Stops plain text from turning into Markdown syntax (a line starting with "# " or "1. ").</summary>
    private static string Escape(string value)
    {
        if (value.Length > 0 && "#>-+*".Contains(value[0])) return "\\" + value;
        return value.Replace("*", "\\*").Replace("_", "\\_");
    }
}

/// <summary>EPUB 3 e-book: one chapter per PDF page, a table of contents from the headings, pictures included.</summary>
public static class EpubWriter
{
    public static void Write(LayoutDocument layout, string path, string fallbackTitle)
    {
        var title = layout.Title ?? fallbackTitle;
        var rightToLeft = layout.MostlyRightToLeft;
        var language = rightToLeft ? "ar" : "und";
        var id = "urn:uuid:" + Guid.NewGuid();

        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        // The mimetype entry must come first and be stored uncompressed.
        Add(zip, "mimetype", "application/epub+zip", ZipLevel.NoCompression);
        Add(zip, "META-INF/container.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">" +
            "<rootfiles><rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/></rootfiles></container>");

        var manifest = new StringBuilder();
        var spine = new StringBuilder();
        var toc = new StringBuilder();
        var imageNumber = 0;
        for (var p = 0; p < layout.Pages.Count; p++)
        {
            var file = $"page-{p + 1:D3}.xhtml";
            var body = new StringBuilder();
            foreach (var block in layout.Pages[p].Blocks)
            {
                switch (block)
                {
                    case ParagraphBlock paragraph when paragraph.HeadingLevel > 0:
                        var anchor = $"h{p + 1}-{toc.Length}";
                        body.Append($"<h{paragraph.HeadingLevel} id=\"{anchor}\">{Html(paragraph.Text)}</h{paragraph.HeadingLevel}>\n");
                        toc.Append($"<li><a href=\"{file}#{anchor}\">{Html(paragraph.Text)}</a></li>");
                        break;
                    case ParagraphBlock paragraph:
                        body.Append(paragraph.Bold ? $"<p><b>{Html(paragraph.Text)}</b></p>\n" : $"<p>{Html(paragraph.Text)}</p>\n");
                        break;
                    case TableBlock table:
                        body.Append("<table>");
                        for (var r = 0; r < table.Rows.Count; r++)
                        {
                            var tag = r == 0 ? "th" : "td";
                            body.Append("<tr>").Append(string.Concat(table.Rows[r].Select(c => $"<{tag}>{Html(c)}</{tag}>"))).Append("</tr>");
                        }
                        body.Append("</table>\n");
                        break;
                    case ImageBlock image:
                        var imageFile = $"images/image-{++imageNumber:D3}.png";
                        var entry = zip.CreateEntry("OEBPS/" + imageFile, ZipLevel.NoCompression);
                        using (var output = entry.Open()) output.Write(image.Png);
                        manifest.Append($"<item id=\"img{imageNumber}\" href=\"{imageFile}\" media-type=\"image/png\"/>");
                        body.Append($"<p><img src=\"{imageFile}\" alt=\"\"/></p>\n");
                        break;
                }
            }
            Add(zip, "OEBPS/" + file, Page(title, language, rightToLeft, body.ToString()));
            manifest.Append($"<item id=\"p{p + 1}\" href=\"{file}\" media-type=\"application/xhtml+xml\"/>");
            spine.Append($"<itemref idref=\"p{p + 1}\"/>");
        }

        if (toc.Length == 0)
            for (var p = 0; p < layout.Pages.Count; p++) toc.Append($"<li><a href=\"page-{p + 1:D3}.xhtml\">{p + 1}</a></li>");
        Add(zip, "OEBPS/nav.xhtml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><!DOCTYPE html><html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\" " +
            $"xml:lang=\"{language}\" lang=\"{language}\"{(rightToLeft ? " dir=\"rtl\"" : "")}><head><title>{Html(title)}</title></head><body>" +
            $"<nav epub:type=\"toc\" id=\"toc\"><h1>{Html(title)}</h1><ol>{toc}</ol></nav></body></html>");
        Add(zip, "OEBPS/style.css",
            "body{font-family:serif;line-height:1.5}table{border-collapse:collapse;margin:1em 0}th,td{border:1px solid #888;padding:.2em .5em}img{max-width:100%}");

        Add(zip, "OEBPS/content.opf",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"bookid\">" +
            "<metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">" +
            $"<dc:identifier id=\"bookid\">{id}</dc:identifier><dc:title>{Html(title)}</dc:title><dc:language>{language}</dc:language>" +
            $"<meta property=\"dcterms:modified\">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</meta></metadata>" +
            "<manifest><item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>" +
            $"<item id=\"css\" href=\"style.css\" media-type=\"text/css\"/>{manifest}</manifest>" +
            $"<spine{(rightToLeft ? " page-progression-direction=\"rtl\"" : "")}>{spine}</spine></package>");
    }

    private static string Page(string title, string language, bool rightToLeft, string body) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?><!DOCTYPE html><html xmlns=\"http://www.w3.org/1999/xhtml\" " +
        $"xml:lang=\"{language}\" lang=\"{language}\"{(rightToLeft ? " dir=\"rtl\"" : "")}><head><title>{Html(title)}</title>" +
        $"<link rel=\"stylesheet\" type=\"text/css\" href=\"style.css\"/></head><body>\n{body}</body></html>";

    private static string Html(string text) => WebUtility.HtmlEncode(text);

    private static void Add(ZipArchive zip, string name, string content, ZipLevel level = ZipLevel.Optimal)
    {
        var entry = zip.CreateEntry(name, level);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
