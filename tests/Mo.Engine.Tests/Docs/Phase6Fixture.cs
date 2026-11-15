using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ImageMagick;
using Mo.Engine.Office;
using Mo.Engine.Pdf.Export;

namespace Mo.Engine.Tests.Docs;

/// <summary>Documents, sheets, slides, archives and subtitles for the phase 6 tests.</summary>
public sealed class Phase6Fixture : IDisposable
{
    public string Folder { get; } = Directory.CreateTempSubdirectory("mo-docs-").FullName;

    public string Markdown { get; }
    public string Latin1Text { get; }
    public string WordFromLibreOffice { get; }
    public string GermanCsv { get; }
    public string EnglishCsv { get; }
    public string TwoSheets { get; }
    public string Slides { get; }
    public string Zip { get; }
    public string TarGz { get; }
    public string EvilZip { get; }
    public string Srt { get; }
    public string Ass { get; }
    public string Vtt { get; }
    public string Sbv { get; }

    public static bool HasLibreOffice => LibreOffice.IsAvailable;

    public Phase6Fixture()
    {
        // ---------- documents ----------
        using (var image = new MagickImage(MagickColors.SteelBlue, 120, 80)) image.Write(Path.Combine(Folder, "chart.png"), MagickFormat.Png);
        Markdown = Write("notes.md",
            "# Projektplan\n\nDer Plan hat **drei** Phasen und einen [Link](https://example.com).\n\n" +
            "## Aufgaben\n\n- Entwurf\n- Umsetzung\n\n1. Erstens\n2. Zweitens\n\n" +
            "| Phase | Dauer |\n| --- | --- |\n| Entwurf | 2 Wochen |\n| Umsetzung | 6 Wochen |\n\n![Diagramm](chart.png)\n");

        Latin1Text = Path.Combine(Folder, "brief.txt");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        File.WriteAllText(Latin1Text, "Sehr geehrte Frau Müller,\r\n\r\ndie Größe passt.\r\nGrüße\r\n", Encoding.GetEncoding(1252));

        // A real Word file written by LibreOffice (heading styles, a table), not by Mo Converter itself.
        var html = Write("source.html",
            "<html><body><h1>Jahresbericht</h1><p>Ein kurzer Absatz.</p><h2>Zahlen</h2>" +
            "<table><tr><td>Monat</td><td>Umsatz</td></tr><tr><td>Januar</td><td>1.200</td></tr></table></body></html>");
        WordFromLibreOffice = Path.Combine(Folder, "jahresbericht.docx");
        if (HasLibreOffice) LibreOffice.ConvertAsync(html, "docx:MS Word 2007 XML", WordFromLibreOffice, CancellationToken.None).GetAwaiter().GetResult();

        // ---------- spreadsheets ----------
        GermanCsv = Write("umsatz.csv", "Region;Umsatz;Notiz\r\nNord;1.234,50;\"enthält ; und\r\nZeilenumbruch\"\r\nSüd;980,00;ok\r\n");
        EnglishCsv = Write("sales.csv", "Region,Sales,Note\nNorth,\"1,234.50\",fine\nSouth,980.00,\"say \"\"hi\"\"\"\n");
        TwoSheets = Path.Combine(Folder, "mappe.xlsx");
        WriteWorkbook(TwoSheets);

        // ---------- slides ----------
        Slides = Path.Combine(Folder, "vortrag.pptx");
        var slide = File.ReadAllBytes(Path.Combine(Folder, "chart.png"));
        PptxWriter.Write([new SlideImage(slide, 720, 405), new SlideImage(slide, 720, 405)], Slides, "Vortrag");

        // ---------- archives ----------
        var content = Directory.CreateDirectory(Path.Combine(Folder, "content")).FullName;
        File.WriteAllText(Path.Combine(content, "a.txt"), "A");
        Directory.CreateDirectory(Path.Combine(content, "sub"));
        File.WriteAllText(Path.Combine(content, "sub", "b.txt"), "B");
        Zip = Path.Combine(Folder, "photos.zip");
        ZipFile.CreateFromDirectory(content, Zip);
        TarGz = Path.Combine(Folder, "backup.tar.gz");
        using (var output = File.Create(TarGz))
        using (var gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Optimal))
            TarFile.CreateFromDirectory(content, gzip, includeBaseDirectory: false);
        EvilZip = Path.Combine(Folder, "evil.zip");
        using (var archive = ZipFile.Open(EvilZip, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("ok.txt").Open())) writer.Write("fine");
            using (var writer = new StreamWriter(archive.CreateEntry("../../escaped.txt").Open())) writer.Write("should never be written");
        }

        // ---------- subtitles ----------
        Srt = Write("film.srt", "1\n00:00:01,000 --> 00:00:02,500\nHallo <i>Welt</i>\n\n2\n00:00:03,000 --> 00:00:04,000\nZweite Zeile\nmit Umbruch & mehr\n");
        Ass = Write("film.ass",
            "[Script Info]\nScriptType: v4.00+\n\n[V4+ Styles]\nFormat: Name, Fontname\nStyle: Default,Arial\n\n[Events]\n" +
            "Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n" +
            "Dialogue: 0,0:00:05.00,0:00:06.50,Default,,0,0,0,,Später, mit Komma\n" +
            "Dialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,,{\\i1}Kursiv{\\i0}\\Nzweite Zeile{\\pos(10,10)}\n");
        Vtt = Write("film.vtt", "WEBVTT\n\nNOTE ein Kommentar\n\nintro\n00:01.000 --> 00:02.000 align:center\nErster\n\n00:00:03.500 --> 00:00:04.000\nZweiter\n");
        Sbv = Write("film.sbv", "0:00:01.000,0:00:02.000\nEins\n\n0:00:03.000,0:00:04.500\nZwei\n");
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(Folder, name);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    /// <summary>Sheet "Zahlen" with a number and a date (style 14 = short date), and sheet "Leer" + "Notizen".</summary>
    private static void WriteWorkbook(string path)
    {
        using var document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var workbook = document.AddWorkbookPart();
        workbook.Workbook = new Workbook();
        var styles = workbook.AddNewPart<WorkbookStylesPart>();
        styles.Stylesheet = new Stylesheet(
            new Fonts(new Font()) { Count = 1 }, new Fills(new Fill()) { Count = 1 }, new Borders(new Border()) { Count = 1 },
            new CellFormats(new CellFormat(), new CellFormat { NumberFormatId = 14, ApplyNumberFormat = true }) { Count = 2 });
        var sheets = workbook.Workbook.AppendChild(new Sheets());

        void AddSheet(uint id, string name, params Row[] rows)
        {
            var part = workbook.AddNewPart<WorksheetPart>();
            part.Worksheet = new Worksheet(new SheetData(rows));
            sheets.Append(new Sheet { Id = workbook.GetIdOfPart(part), SheetId = id, Name = name });
        }

        Cell Text(string reference, string value) => new() { CellReference = reference, DataType = CellValues.InlineString, InlineString = new InlineString(new Text(value)) };
        AddSheet(1, "Zahlen",
            new Row(Text("A1", "Datum"), Text("B1", "Betrag")) { RowIndex = 1 },
            new Row(new Cell { CellReference = "A2", StyleIndex = 1, CellValue = new CellValue(46340) },
                    new Cell { CellReference = "B2", CellValue = new CellValue(1234.5) }) { RowIndex = 2 },
            new Row(new Cell { CellReference = "C4", CellValue = new CellValue(7) }) { RowIndex = 4 });
        AddSheet(2, "Leer");
        AddSheet(3, "Notizen", new Row(Text("A1", "Hallo; Welt")) { RowIndex = 1 });
        workbook.Workbook.Save();
    }

    public string NewOutputFolder() => Directory.CreateDirectory(Path.Combine(Folder, "out-" + Guid.NewGuid().ToString("N")[..6])).FullName;

    public void Dispose()
    {
        try { Directory.Delete(Folder, true); } catch (IOException) { }
    }
}
