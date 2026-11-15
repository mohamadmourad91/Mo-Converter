using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Mo.Engine.Pdf.Layout;

namespace Mo.Engine.Pdf.Export;

/// <summary>
/// Excel workbook: one sheet per detected table, numbers as real numbers, the first row bold and frozen.
/// A PDF without tables gets one sheet with its text, one paragraph per row, so the result is never empty.
/// </summary>
public static class XlsxWriter
{
    public static void Write(LayoutDocument layout, string path, Func<int, string> tableSheetName, string textSheetName)
    {
        var rightToLeft = layout.MostlyRightToLeft;
        var sheets = layout.Tables.Select((t, i) => (Name: tableSheetName(i + 1), Rows: t.Rows, Header: true)).ToList();
        if (sheets.Count == 0)
        {
            var rows = layout.AllBlocks.OfType<ParagraphBlock>().Select(p => (IReadOnlyList<string>)[p.Text]).ToList();
            sheets.Add((textSheetName, rows, false));
        }

        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new Workbook();
        AddStyles(workbookPart);
        var sheetList = workbookPart.Workbook.AppendChild(new Sheets());

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var s = 0; s < sheets.Count; s++)
        {
            var (name, rows, header) = sheets[s];
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = Sheet(rows, header, rightToLeft);
            sheetList.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = (uint)(s + 1),
                Name = UniqueName(name, used),
            });
        }

        if (layout.Title is { } title) document.PackageProperties.Title = title;
        workbookPart.Workbook.Save();
    }

    private static Worksheet Sheet(IReadOnlyList<IReadOnlyList<string>> rows, bool header, bool rightToLeft)
    {
        var view = new SheetView { WorkbookViewId = 0U };
        if (rightToLeft) view.RightToLeft = true;
        if (header && rows.Count > 1)
            view.Append(new Pane { VerticalSplit = 1D, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen });

        var worksheet = new Worksheet(new SheetViews(view));

        var columnCount = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
        if (columnCount > 0)
        {
            var columns = new Columns();
            for (var c = 0; c < columnCount; c++)
            {
                var longest = rows.Where(r => c < r.Count).Select(r => r[c].Length).DefaultIfEmpty(0).Max();
                var width = header ? Math.Clamp(longest + 2, 8, 60) : Math.Clamp(longest, 30, 120);
                columns.Append(new Column { Min = (uint)(c + 1), Max = (uint)(c + 1), Width = width, CustomWidth = true });
            }
            worksheet.Append(columns);
        }

        var data = new SheetData();
        for (var r = 0; r < rows.Count; r++)
        {
            var row = new Row { RowIndex = (uint)(r + 1) };
            for (var c = 0; c < rows[r].Count; c++)
            {
                var text = rows[r][c];
                if (text.Length == 0) continue;
                var reference = ColumnName(c) + (r + 1);
                var bold = header && r == 0;
                Cell cell;
                if (!bold && NumberText.TryParse(text, out var number, out var shape))
                {
                    // Shown as in the PDF: "1.234,50" keeps grouping and two decimals, in the reader's own separators.
                    var style = shape.Decimals == 2 ? 4U : shape.Grouped && shape.Decimals == 0 ? 3U : 0U;
                    cell = new Cell { CellReference = reference, DataType = CellValues.Number, CellValue = new CellValue(number), StyleIndex = style };
                    row.Append(cell);
                    continue;
                }
                else
                {
                    cell = new Cell
                    {
                        CellReference = reference,
                        DataType = CellValues.InlineString,
                        InlineString = new InlineString(new Text(text) { Space = SpaceProcessingModeValues.Preserve }),
                    };
                }
                cell.StyleIndex = bold ? 1U : header ? 0U : 2U;
                row.Append(cell);
            }
            data.Append(row);
        }
        worksheet.Append(data);
        return worksheet;
    }

    /// <summary>Style 0 normal, 1 bold header, 2 wrapped text (text-only sheet), 3 grouped integer, 4 two decimals.</summary>
    private static void AddStyles(WorkbookPart workbookPart)
    {
        var styles = workbookPart.AddNewPart<WorkbookStylesPart>();
        styles.Stylesheet = new Stylesheet(
            new Fonts(
                new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" }),
                new Font(new Bold(), new FontSize { Val = 11 }, new FontName { Val = "Calibri" })) { Count = 2U },
            new Fills(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 })) { Count = 2U },
            new Borders(new Border(new LeftBorder(), new RightBorder(), new TopBorder(), new BottomBorder(), new DiagonalBorder())) { Count = 1U },
            new CellFormats(
                new CellFormat { FontId = 0U, FillId = 0U, BorderId = 0U },
                new CellFormat { FontId = 1U, FillId = 0U, BorderId = 0U, ApplyFont = true },
                new CellFormat(new Alignment { WrapText = true, Vertical = VerticalAlignmentValues.Top })
                    { FontId = 0U, FillId = 0U, BorderId = 0U, ApplyAlignment = true },
                // Built-in formats 3 "#,##0" and 4 "#,##0.00".
                new CellFormat { NumberFormatId = 3U, FontId = 0U, FillId = 0U, BorderId = 0U, ApplyNumberFormat = true },
                new CellFormat { NumberFormatId = 4U, FontId = 0U, FillId = 0U, BorderId = 0U, ApplyNumberFormat = true }) { Count = 5U });
        styles.Stylesheet.Save();
    }

    public static string ColumnName(int index)
    {
        var name = "";
        for (var n = index + 1; n > 0; n = (n - 1) / 26) name = (char)('A' + (n - 1) % 26) + name;
        return name;
    }

    /// <summary>Excel sheet names: at most 31 characters, no []:*?/\ and unique ignoring case.</summary>
    private static string UniqueName(string name, HashSet<string> used)
    {
        var clean = new string(name.Where(c => "[]:*?/\\".IndexOf(c) < 0).ToArray()).Trim('\'').Trim();
        if (clean.Length == 0) clean = "Sheet";
        if (clean.Length > 31) clean = clean[..31];
        var candidate = clean;
        for (var n = 2; !used.Add(candidate); n++)
        {
            var suffix = $" ({n})";
            candidate = (clean.Length + suffix.Length > 31 ? clean[..(31 - suffix.Length)] : clean) + suffix;
        }
        return candidate;
    }
}
