namespace Mo.Engine.Pdf.Layout;

/// <summary>What a PDF page contains, in reading order, independent of any output format.</summary>
public abstract record LayoutBlock;

/// <summary>A paragraph or heading (level 1 or 2; 0 is body text).</summary>
public sealed record ParagraphBlock(string Text, int HeadingLevel, bool Bold, bool RightToLeft) : LayoutBlock;

/// <summary>A table as rows of cell texts, all rows with the same number of cells, in reading order.</summary>
public sealed record TableBlock(IReadOnlyList<IReadOnlyList<string>> Rows, bool RightToLeft) : LayoutBlock
{
    public int ColumnCount => Rows.Count == 0 ? 0 : Rows[0].Count;
}

/// <summary>A picture as PNG, with its size on the page in points.</summary>
public sealed record ImageBlock(byte[] Png, double WidthPt, double HeightPt) : LayoutBlock;

public sealed record LayoutPage(int Number, double WidthPt, double HeightPt, IReadOnlyList<LayoutBlock> Blocks);

public sealed record LayoutDocument(string? Title, IReadOnlyList<LayoutPage> Pages)
{
    public IEnumerable<LayoutBlock> AllBlocks => Pages.SelectMany(p => p.Blocks);

    public IEnumerable<TableBlock> Tables => AllBlocks.OfType<TableBlock>();

    public bool HasText => AllBlocks.Any(b => b is ParagraphBlock or TableBlock);

    /// <summary>True when most of the text is Arabic or Hebrew: outputs then read right to left.</summary>
    public bool MostlyRightToLeft
    {
        get
        {
            var text = string.Concat(AllBlocks.Select(b => b switch
            {
                ParagraphBlock p => p.Text,
                TableBlock t => string.Concat(t.Rows.SelectMany(r => r)),
                _ => "",
            }));
            return Script.IsMostlyRightToLeft(text);
        }
    }
}

/// <summary>Right-to-left script detection.</summary>
public static class Script
{
    public static bool IsRightToLeftLetter(char c) =>
        c is >= '\u0590' and <= '\u08FF' or >= '\uFB1D' and <= '\uFDFF' or >= '\uFE70' and <= '\uFEFF';

    /// <summary>More right-to-left letters than left-to-right letters.</summary>
    public static bool IsMostlyRightToLeft(string text)
    {
        int rtl = 0, ltr = 0;
        foreach (var c in text)
        {
            if (IsRightToLeftLetter(c)) rtl++;
            else if (char.IsLetter(c)) ltr++;
        }
        return rtl > ltr;
    }
}
