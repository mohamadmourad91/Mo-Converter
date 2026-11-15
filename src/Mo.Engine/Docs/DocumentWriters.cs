using System.Net;
using System.Text;
using Mo.Engine.Office;
using Mo.Engine.Pdf.Layout;

namespace Mo.Engine.Docs;

/// <summary>Plain text: headings and tables keep their own lines; tables are tab-separated so they paste into Excel.</summary>
public static class PlainTextWriter
{
    public static void Write(LayoutDocument layout, string path)
    {
        var text = new StringBuilder();
        foreach (var block in layout.AllBlocks)
        {
            switch (block)
            {
                case ParagraphBlock { HeadingLevel: > 0 } heading:
                    if (text.Length > 0) text.AppendLine();
                    text.AppendLine(heading.Text).AppendLine();
                    break;
                case ParagraphBlock paragraph:
                    text.AppendLine(paragraph.Text);
                    break;
                case TableBlock table:
                    text.AppendLine();
                    foreach (var row in table.Rows) text.AppendLine(string.Join("\t", row));
                    text.AppendLine();
                    break;
            }
        }
        TextFiles.Write(path, text.ToString().TrimEnd() + Environment.NewLine);
    }
}

/// <summary>A single self-contained web page (pictures embedded), right to left when the text is.</summary>
public static class HtmlWriter
{
    public static void Write(LayoutDocument layout, string path, string fallbackTitle)
    {
        var body = new StringBuilder();
        foreach (var block in layout.AllBlocks)
        {
            switch (block)
            {
                case ParagraphBlock { HeadingLevel: > 0 } heading:
                    body.Append($"<h{heading.HeadingLevel}>{Html(heading.Text)}</h{heading.HeadingLevel}>\n");
                    break;
                case ParagraphBlock paragraph:
                    var dir = paragraph.RightToLeft != layout.MostlyRightToLeft ? (paragraph.RightToLeft ? " dir=\"rtl\"" : " dir=\"ltr\"") : "";
                    body.Append(paragraph.Bold ? $"<p{dir}><b>{Html(paragraph.Text)}</b></p>\n" : $"<p{dir}>{Html(paragraph.Text)}</p>\n");
                    break;
                case TableBlock table:
                    body.Append("<table>\n");
                    for (var r = 0; r < table.Rows.Count; r++)
                    {
                        var tag = r == 0 ? "th" : "td";
                        body.Append("<tr>").Append(string.Concat(table.Rows[r].Select(c => $"<{tag}>{Html(c)}</{tag}>"))).Append("</tr>\n");
                    }
                    body.Append("</table>\n");
                    break;
                case ImageBlock image:
                    body.Append($"<p><img src=\"data:image/png;base64,{Convert.ToBase64String(image.Png)}\" alt=\"\" style=\"width:{image.WidthPt * 4 / 3:0}px\"/></p>\n");
                    break;
            }
        }
        File.WriteAllText(path, Page(layout.Title ?? fallbackTitle, layout.MostlyRightToLeft, body.ToString()), new UTF8Encoding(false));
    }

    /// <summary>The HTML page around a body, shared with the Markdown converter.</summary>
    public static string Page(string title, bool rightToLeft, string body) =>
        "<!DOCTYPE html>\n" +
        $"<html{(rightToLeft ? " dir=\"rtl\" lang=\"ar\"" : "")}>\n<head>\n<meta charset=\"utf-8\">\n" +
        "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n" +
        $"<title>{Html(title)}</title>\n<style>\n" +
        "body{font-family:'Segoe UI',system-ui,sans-serif;line-height:1.6;max-width:50em;margin:2em auto;padding:0 1em;color:#1b1d26}\n" +
        "h1,h2,h3{line-height:1.25}table{border-collapse:collapse;margin:1em 0}th,td{border:1px solid #c8cad3;padding:.3em .6em;text-align:start}\n" +
        "th{background:#f1f2f6}img{max-width:100%;height:auto}code,pre{font-family:Consolas,monospace;background:#f4f5f8}pre{padding:.8em;overflow:auto}\n" +
        $"</style>\n</head>\n<body>\n{body}</body>\n</html>\n";

    public static string Html(string text) => WebUtility.HtmlEncode(text);
}
