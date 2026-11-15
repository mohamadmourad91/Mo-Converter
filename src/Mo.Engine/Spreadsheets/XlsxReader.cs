using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Mo.Engine.Spreadsheets;

/// <summary>A worksheet as rows of displayed values.</summary>
public sealed record SheetData(string Name, List<List<string>> Rows);

/// <summary>
/// Reads the values of every worksheet: shared and inline text, numbers in the user's own format, booleans,
/// dates as ISO dates (2026-11-14) and cached formula results.
/// </summary>
public static class XlsxReader
{
    public static List<SheetData> Read(string path)
    {
        SpreadsheetDocument document;
        try
        {
            document = SpreadsheetDocument.Open(path, false);
        }
        catch (Exception e) when (e is OpenXmlPackageException or InvalidDataException or IOException or FileFormatException)
        {
            throw new OperationException("error.document-unreadable", Path.GetFileName(path));
        }

        using (document)
        {
            var workbook = document.WorkbookPart ?? throw new OperationException("error.document-unreadable", Path.GetFileName(path));
            var shared = workbook.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(i => i.InnerText).ToList() ?? [];
            var dateStyles = DateStyles(workbook);
            var result = new List<SheetData>();

            foreach (var sheet in workbook.Workbook?.Sheets?.Elements<Sheet>() ?? [])
            {
                if (sheet.Id?.Value is not { } id || workbook.GetPartById(id) is not WorksheetPart part) continue;
                var rows = new List<List<string>>();
                foreach (var row in part.Worksheet?.Descendants<Row>() ?? [])
                {
                    var index = (int)(row.RowIndex?.Value ?? (uint)(rows.Count + 1)) - 1;
                    while (rows.Count < index) rows.Add([]);
                    var values = new List<string>();
                    foreach (var cell in row.Elements<Cell>())
                    {
                        var column = cell.CellReference?.Value is { } reference ? ColumnIndex(reference) : values.Count;
                        while (values.Count < column) values.Add("");
                        values.Add(Value(cell, shared, dateStyles));
                    }
                    rows.Add(values);
                }
                // Trim empty rows at the end and pad every row to the same width.
                while (rows.Count > 0 && rows[^1].All(string.IsNullOrEmpty)) rows.RemoveAt(rows.Count - 1);
                var width = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
                foreach (var r in rows) while (r.Count < width) r.Add("");
                result.Add(new SheetData(sheet.Name?.Value ?? "Sheet", rows));
            }
            return result;
        }
    }

    private static string Value(Cell cell, List<string> shared, HashSet<uint> dateStyles)
    {
        var raw = cell.CellValue?.Text ?? "";
        var type = cell.DataType?.Value;
        if (type == CellValues.SharedString) return int.TryParse(raw, out var i) && i < shared.Count ? shared[i] : "";
        if (type == CellValues.InlineString) return cell.InlineString?.InnerText ?? "";
        if (type == CellValues.Boolean) return raw == "1" ? "TRUE" : "FALSE";
        if (type == CellValues.String || type == CellValues.Error) return raw;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return raw;

        if (cell.StyleIndex?.Value is { } style && dateStyles.Contains(style) && number is > -657435 and < 2958466)
        {
            var date = DateTime.FromOADate(number);
            return date.TimeOfDay == TimeSpan.Zero ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        return number.ToString("G15", CultureInfo.CurrentCulture);
    }

    /// <summary>Cell styles whose number format shows a date or time.</summary>
    private static HashSet<uint> DateStyles(WorkbookPart workbook)
    {
        var result = new HashSet<uint>();
        var stylesheet = workbook.WorkbookStylesPart?.Stylesheet;
        if (stylesheet?.CellFormats is null) return result;
        var custom = stylesheet.NumberingFormats?.Elements<NumberingFormat>()
            .Where(f => f.NumberFormatId?.Value is not null)
            .ToDictionary(f => f.NumberFormatId!.Value, f => f.FormatCode?.Value ?? "") ?? [];

        uint index = 0;
        foreach (var format in stylesheet.CellFormats.Elements<CellFormat>())
        {
            var id = format.NumberFormatId?.Value ?? 0;
            var isDate = id is >= 14 and <= 22 or >= 45 and <= 47
                         || (custom.TryGetValue(id, out var code) && LooksLikeDate(code));
            if (isDate) result.Add(index);
            index++;
        }
        return result;
    }

    private static bool LooksLikeDate(string code)
    {
        // Ignore quoted text and colours like [Red]; then look for day, month, year or hour placeholders.
        var plain = System.Text.RegularExpressions.Regex.Replace(code, "\"[^\"]*\"|\\[[^\\]]*\\]", "");
        return plain.IndexOfAny(['d', 'D', 'y', 'Y', 'h', 'H']) >= 0 || (plain.Contains('m') && !plain.Contains('0') && !plain.Contains('#'));
    }

    public static int ColumnIndex(string reference)
    {
        var index = 0;
        foreach (var c in reference)
        {
            if (!char.IsLetter(c)) break;
            index = index * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
        }
        return index - 1;
    }
}
