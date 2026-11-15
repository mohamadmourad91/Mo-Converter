using ImageMagick;
using UglyToad.PdfPig.Content;
using PigDocument = UglyToad.PdfPig.PdfDocument;

namespace Mo.Engine.Pdf.Layout;

/// <summary>
/// Reads a PDF into <see cref="LayoutDocument"/>: words are grouped into lines; lines whose words fall into the
/// same columns over several rows become tables; the remaining lines become headings (by font size) and
/// paragraphs; pictures are kept. Works on PDFs with real text; scanned pages only yield their pictures.
/// </summary>
public static class PdfLayoutReader
{
    public static LayoutDocument Read(string path, CancellationToken cancellation = default)
    {
        PigDocument document;
        try
        {
            document = PigDocument.Open(path);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            var key = e.Message.Contains("password", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase)
                ? "error.pdf-protected" : "error.pdf-unreadable";
            throw new OperationException(key, Path.GetFileName(path));
        }

        using (document)
        {
            try
            {
                var pages = new List<(Page Page, List<Line> Lines)>();
                foreach (var page in document.GetPages())
                {
                    cancellation.ThrowIfCancellationRequested();
                    pages.Add((page, Lines(page)));
                }

                var bodySize = BodySize(pages.SelectMany(p => p.Lines));
                var result = pages.Select(p => new LayoutPage(p.Page.Number, p.Page.Width, p.Page.Height, Blocks(p.Page, p.Lines, bodySize))).ToList();
                var title = document.Information.Title;
                return new LayoutDocument(string.IsNullOrWhiteSpace(title) ? null : title.Trim(), result);
            }
            catch (Exception e) when (e is not OperationCanceledException and not OperationException)
            {
                throw new OperationException("error.pdf-unreadable", Path.GetFileName(path));
            }
        }
    }

    // ---------- lines ----------

    private sealed class Line
    {
        public required List<Word> Words { get; init; }
        public double Baseline { get; set; }
        public double Size { get; set; }
        public double Top => Words.Max(w => w.BoundingBox.Top);
        public double Left => Words.Min(w => w.BoundingBox.Left);
        public bool RightToLeft { get; set; }
        public bool Bold { get; set; }

        /// <summary>Words in reading order: right-to-left lines are read from the rightmost word.</summary>
        public string Text => Join(Words, RightToLeft);
    }

    private static string Join(IEnumerable<Word> words, bool rightToLeft)
    {
        var ordered = words.OrderBy(w => w.BoundingBox.Left);
        return string.Join(" ", (rightToLeft ? ordered.Reverse() : ordered).Select(w => w.Text));
    }

    private static double SizeOf(Word word)
    {
        var size = word.Letters.Count == 0 ? 0 : word.Letters.Average(l => l.PointSize);
        return size > 1 ? size : Math.Max(word.BoundingBox.Height, 1);
    }

    private static List<Line> Lines(Page page)
    {
        var words = page.GetWords()
            .Where(w => !string.IsNullOrWhiteSpace(w.Text) && w.Letters.Count > 0)
            .Select(w => (Word: w, Baseline: w.Letters[0].StartBaseLine.Y, Size: SizeOf(w)))
            .OrderByDescending(w => w.Baseline)
            .ThenBy(w => w.Word.BoundingBox.Left);

        var lines = new List<Line>();
        foreach (var (word, baseline, size) in words)
        {
            var line = lines.LastOrDefault(l => Math.Abs(l.Baseline - baseline) <= 0.35 * Math.Max(size, l.Size));
            if (line is null)
            {
                lines.Add(new Line { Words = [word], Baseline = baseline, Size = size });
            }
            else
            {
                line.Words.Add(word);
                line.Size = Math.Max(line.Size, size);
            }
        }

        foreach (var line in lines)
        {
            line.Words.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
            line.RightToLeft = Script.IsMostlyRightToLeft(string.Concat(line.Words.Select(w => w.Text)));
            line.Bold = line.Words.All(w => (w.FontName ?? "").Contains("Bold", StringComparison.OrdinalIgnoreCase));
        }
        return lines.OrderByDescending(l => l.Baseline).ToList();
    }

    private static double BodySize(IEnumerable<Line> lines)
    {
        // The size carrying the most characters is the body text size.
        var weighted = lines
            .GroupBy(l => Math.Round(l.Size * 2) / 2)
            .Select(g => (Size: g.Key, Chars: g.Sum(l => l.Text.Length)))
            .OrderByDescending(g => g.Chars)
            .FirstOrDefault();
        return weighted.Chars > 0 ? weighted.Size : 11;
    }

    // ---------- blocks ----------

    private static List<LayoutBlock> Blocks(Page page, List<Line> lines, double bodySize)
    {
        var placed = new List<(double Top, LayoutBlock Block)>();
        var loose = new List<Line>();
        var i = 0;
        while (i < lines.Count)
        {
            var found = FindTable(lines, i);
            if (found is { } table)
            {
                placed.AddRange(Paragraphs(loose, bodySize));
                loose.Clear();
                if (table.Block is not null) placed.Add((lines[i].Top, table.Block));
                else placed.AddRange(Paragraphs(table.ColumnLines!, bodySize));
                i += table.Rows;
            }
            else
            {
                loose.Add(lines[i]);
                i++;
            }
        }
        placed.AddRange(Paragraphs(loose, bodySize));
        placed.AddRange(Images(page));
        return placed.OrderByDescending(p => p.Top).Select(p => p.Block).ToList();
    }

    /// <summary>
    /// Looks for a table starting at line <paramref name="start"/>. Columns are found across all rows at once:
    /// a vertical strip that no word of any row touches is a column boundary (a "gutter"). That also finds tight
    /// tables whose columns are only two word-spaces apart, which a per-line gap rule cannot tell from text.
    /// Returns the longest run of lines that forms a valid table, or null.
    /// </summary>
    private static (int Rows, TableBlock? Block, List<Line>? ColumnLines)? FindTable(List<Line> lines, int start)
    {
        var first = lines[start];
        var rows = new List<Line> { first };
        (int Rows, TableBlock? Block, List<Line>? ColumnLines)? best = null;

        for (var k = start + 1; k < lines.Count; k++)
        {
            var line = lines[k];
            var previous = rows[^1];
            var size = Math.Max(previous.Size, line.Size);
            if (previous.Baseline - line.Baseline > 2.2 * size) break;                 // a gap: the table has ended
            if (Math.Abs(line.Size - first.Size) > 0.25 * first.Size) break;          // headings are not rows

            var candidate = new List<Line>(rows) { line };
            var gutters = Gutters(candidate);
            if (gutters.Count == 0) break;                                             // running text fills every gap
            rows = candidate;
            if (BuildTable(rows, gutters) is { } table) best = (rows.Count, table.Block, table.ColumnLines);
        }
        return best;
    }

    /// <summary>Gaps in the union of all word boxes, at least 0.4 em wide (a word space is about 0.25 em).</summary>
    private static List<(double Left, double Right)> Gutters(List<Line> rows)
    {
        var size = rows.Average(r => r.Size);
        var spans = rows.SelectMany(r => r.Words).Select(w => (L: w.BoundingBox.Left, R: w.BoundingBox.Right)).OrderBy(s => s.L).ToList();
        var gutters = new List<(double Left, double Right)>();
        var reach = spans[0].R;
        foreach (var (l, r) in spans.Skip(1))
        {
            if (l - reach >= Math.Max(0.4 * size, 3)) gutters.Add((reach, l));
            reach = Math.Max(reach, r);
        }
        return gutters;
    }

    private static (TableBlock? Block, List<Line>? ColumnLines)? BuildTable(List<Line> rows, List<(double Left, double Right)> gutters)
    {
        var columns = gutters.Count + 1;
        int ColumnOf(Word w)
        {
            var center = (w.BoundingBox.Left + w.BoundingBox.Right) / 2;
            return gutters.Count(g => (g.Left + g.Right) / 2 < center);
        }

        var cells = rows.Select(r =>
        {
            var byColumn = new List<Word>[columns];
            for (var c = 0; c < columns; c++) byColumn[c] = [];
            foreach (var w in r.Words) byColumn[ColumnOf(w)].Add(w);
            return byColumn;
        }).ToList();

        // Every column must be used by at least half of the rows, and at least two rows must span two columns.
        for (var c = 0; c < columns; c++)
            if (cells.Count(row => row[c].Count > 0) * 2 < rows.Count) return null;
        if (cells.Count(row => row.Count(col => col.Count > 0) >= 2) < 2) return null;
        // Two lines can line up by chance; ask for three columns or a clearly wide gap then.
        var size = rows.Average(r => r.Size);
        if (rows.Count == 2 && columns < 3 && gutters.Max(g => g.Right - g.Left) < size) return null;

        var averageWords = cells.SelectMany(row => row).Where(col => col.Count > 0).Average(col => col.Count);
        if (columns == 2 && averageWords > 6)
        {
            // Two columns of running text (newspaper layout): read the left column first, then the right one.
            var columnLines = Enumerable.Range(0, columns)
                .SelectMany(c => rows.Select((r, i) => (Row: r, Words: cells[i][c]))
                    .Where(x => x.Words.Count > 0)
                    .Select(x => new Line { Words = x.Words, Baseline = x.Row.Baseline, Size = x.Row.Size, RightToLeft = x.Row.RightToLeft, Bold = x.Row.Bold }))
                .ToList();
            return (null, columnLines);
        }

        var rightToLeft = rows.Count(r => r.RightToLeft) * 2 > rows.Count;
        var table = cells.Select(row =>
        {
            var texts = row.Select(col => Join(col, rightToLeft)).ToList();
            if (rightToLeft) texts.Reverse(); // the first column of a right-to-left table is the rightmost one
            return (IReadOnlyList<string>)texts;
        }).ToList();
        return (new TableBlock(table, rightToLeft), null);
    }
    private static IEnumerable<(double Top, LayoutBlock Block)> Paragraphs(List<Line> lines, double bodySize)
    {
        var result = new List<(double Top, LayoutBlock Block)>();
        Line? previous = null;
        double top = 0, left = 0;
        int level = 0;
        bool bold = false, rtl = false;
        var text = new System.Text.StringBuilder();

        void Flush()
        {
            if (text.Length > 0) result.Add((top, new ParagraphBlock(text.ToString().Trim(), level, bold && level == 0, rtl)));
            text.Clear();
        }

        foreach (var line in lines)
        {
            var lineLevel = line.Size >= bodySize * 1.6 ? 1 : line.Size >= bodySize * 1.25 ? 2 : 0;
            var continues = previous is not null
                && lineLevel == level
                && line.RightToLeft == rtl
                && line.Bold == bold
                && Math.Abs(line.Size - previous.Size) <= 0.15 * previous.Size
                && previous.Baseline - line.Baseline <= 1.75 * Math.Max(line.Size, previous.Size)
                && line.Left <= left + 1.5 * line.Size; // an indented first line starts a new paragraph

            if (!continues)
            {
                Flush();
                top = line.Top;
                left = line.Left;
                level = lineLevel;
                bold = line.Bold;
                rtl = line.RightToLeft;
                text.Append(line.Text);
            }
            else if (text.Length > 0 && text[^1] == '-' && line.Text.Length > 0 && char.IsLower(line.Text[0]))
            {
                // Word broken at the line end: "Konver-" + "tierung".
                text.Length--;
                text.Append(line.Text);
            }
            else
            {
                text.Append(' ').Append(line.Text);
            }
            previous = line;
        }
        Flush();
        return result;
    }

    private static IEnumerable<(double Top, LayoutBlock Block)> Images(Page page)
    {
        var result = new List<(double Top, LayoutBlock Block)>();
        foreach (var image in page.GetImages())
        {
            var bounds = image.BoundingBox;
            if (bounds.Width < 20 || bounds.Height < 20) continue; // bullets, rules, tracking pixels
            var png = ToPng(image);
            if (png is not null) result.Add((bounds.Top, new ImageBlock(png, bounds.Width, bounds.Height)));
        }
        return result;
    }

    private static byte[]? ToPng(IPdfImage image)
    {
        try
        {
            if (image.TryGetPng(out var png)) return png;
            // JPEG and other encoded streams that PdfPig cannot turn into PNG itself.
            using var picture = new MagickImage(image.RawMemory.ToArray());
            Mo.Engine.Images.ImageSupport.PreparePng(picture);
            return picture.ToByteArray(MagickFormat.Png);
        }
        catch (Exception e) when (e is MagickException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            return null;
        }
    }
}
