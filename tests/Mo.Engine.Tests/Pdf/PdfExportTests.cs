using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using ImageMagick;
using Mo.Engine.Pdf.Export;
using Mo.Engine.Pdf.Layout;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using W = DocumentFormat.OpenXml.Wordprocessing;
using X = DocumentFormat.OpenXml.Spreadsheet;

namespace Mo.Engine.Tests.Pdf;

/// <summary>
/// A two-page "quarterly report": title, subheading, a paragraph broken over two lines, a second paragraph,
/// a 4×3 table with German numbers, a photo, and on page two a heading, text and an Arabic line.
/// </summary>
public sealed class ReportFixture : IDisposable
{
    public string Folder { get; } = Directory.CreateTempSubdirectory("mo-export-").FullName;
    public string Report { get; }
    public string TextOnly { get; }
    public string PhotoOnly { get; }
    public string Protected { get; }

    public const string Title = "Quartalsbericht 2026";
    public const string Arabic = "مرحبا بالعالم";

    public ReportFixture()
    {
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        XFont Font(double size, XFontStyleEx style = XFontStyleEx.Regular) =>
            new("Arial", size, style, new XPdfFontOptions(PdfFontEncoding.Unicode));

        var photo = Path.Combine(Folder, "photo.jpg");
        using (var image = new MagickImage(MagickColors.SteelBlue, 300, 200))
        {
            using var patch = new MagickImage(MagickColors.Orange, 120, 80);
            image.Composite(patch, 40, 40, CompositeOperator.Over);
            image.Write(photo, MagickFormat.Jpeg);
        }

        Report = Path.Combine(Folder, "report.pdf");
        using (var document = new PdfDocument())
        {
            document.Info.Title = Title;
            var body = Font(11);
            var page = document.AddPage();
            using (var gfx = XGraphics.FromPdfPage(page))
            {
                gfx.DrawString(Title, Font(24, XFontStyleEx.Bold), XBrushes.Black, 72, 90);
                gfx.DrawString("Umsatz nach Region", Font(16, XFontStyleEx.Bold), XBrushes.Black, 72, 130);
                gfx.DrawString("Der Umsatz ist im dritten Quartal deutlich gestiegen, vor allem im", body, XBrushes.Black, 72, 160);
                gfx.DrawString("Norden und im Westen.", body, XBrushes.Black, 72, 174);
                gfx.DrawString("Die folgende Tabelle zeigt die Zahlen.", body, XBrushes.Black, 72, 200);

                string[][] rows =
                [
                    ["Region", "Umsatz", "Anteil"],
                    ["Nord", "1.234,50", "25 %"],
                    ["West", "980,00", "20 %"],
                    ["Süd", "2.010,75", "55 %"],
                ];
                for (var r = 0; r < rows.Length; r++)
                {
                    var y = 240 + r * 18;
                    gfx.DrawString(rows[r][0], body, XBrushes.Black, 72, y);
                    gfx.DrawString(rows[r][1], body, XBrushes.Black, 250, y);
                    gfx.DrawString(rows[r][2], body, XBrushes.Black, 400, y);
                }

                using var picture = XImage.FromFile(photo);
                gfx.DrawImage(picture, 72, 330, 200, 133);
                gfx.DrawString("Ende der ersten Seite.", body, XBrushes.Black, 72, 500);
            }

            page = document.AddPage();
            using (var gfx = XGraphics.FromPdfPage(page))
            {
                gfx.DrawString("Ausblick", Font(16, XFontStyleEx.Bold), XBrushes.Black, 72, 90);
                gfx.DrawString("Im vierten Quartal erwarten wir weiteres Wachstum.", body, XBrushes.Black, 72, 120);
                gfx.DrawString(Arabic, body, XBrushes.Black, 300, 170);
            }
            document.Save(Report);
        }

        TextOnly = Path.Combine(Folder, "letter.pdf");
        using (var document = new PdfDocument())
        {
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString("Sehr geehrte Damen und Herren,", Font(11), XBrushes.Black, 72, 100);
            gfx.DrawString("vielen Dank für Ihre Anfrage.", Font(11), XBrushes.Black, 72, 140);
            gfx.Dispose();
            document.Save(TextOnly);
        }

        PhotoOnly = Path.Combine(Folder, "scan.pdf");
        using (var document = new PdfDocument())
        {
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            using var picture = XImage.FromFile(photo);
            gfx.DrawImage(picture, 0, 0, page.Width.Point, page.Height.Point);
            gfx.Dispose();
            document.Save(PhotoOnly);
        }

        Protected = Path.Combine(Folder, "locked.pdf");
        using (var document = new PdfDocument())
        {
            var page = document.AddPage();
            using (var gfx = XGraphics.FromPdfPage(page)) gfx.DrawString("Geheim", Font(11), XBrushes.Black, 72, 100);
            document.SecuritySettings.UserPassword = "geheim";
            document.Save(Protected);
        }
    }

    public string NewOutputFolder() => Directory.CreateDirectory(Path.Combine(Folder, "out-" + Guid.NewGuid().ToString("N")[..6])).FullName;

    public void Dispose() => Directory.Delete(Folder, true);
}

public sealed class PdfExportTests(ReportFixture fixture) : IClassFixture<ReportFixture>
{
    private static readonly OperationRegistry Registry = OperationRegistry.CreateDefault();

    private async Task<IReadOnlyList<string>> Run(string id, string file)
    {
        Localizer.Current = new Localizer("en");
        return (await Registry.Find(id)!.RunAsync(
            new OperationContext(new InputSet([file]), new OutputPlanner(fixture.NewOutputFolder())), CancellationToken.None)).Outputs;
    }

    private static void AssertValid(OpenXmlPackage package)
    {
        var errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package)
            .Select(e => $"{e.Path?.XPath}: {e.Description}")
            .ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors.Take(10)));
    }

    // ---------- layout ----------

    [Fact]
    public void Layout_finds_headings_paragraphs_table_and_picture_in_order()
    {
        var layout = PdfLayoutReader.Read(fixture.Report);
        Assert.Equal(ReportFixture.Title, layout.Title);
        Assert.Equal(2, layout.Pages.Count);

        var first = layout.Pages[0].Blocks;
        var title = Assert.IsType<ParagraphBlock>(first[0]);
        Assert.Equal((ReportFixture.Title, 1), (title.Text, title.HeadingLevel));
        var sub = Assert.IsType<ParagraphBlock>(first[1]);
        Assert.Equal(("Umsatz nach Region", 2), (sub.Text, sub.HeadingLevel));

        // Two lines joined into one paragraph; the next line starts a new one.
        var paragraph = Assert.IsType<ParagraphBlock>(first[2]);
        Assert.Equal("Der Umsatz ist im dritten Quartal deutlich gestiegen, vor allem im Norden und im Westen.", paragraph.Text);
        Assert.Equal(0, paragraph.HeadingLevel);
        Assert.Equal("Die folgende Tabelle zeigt die Zahlen.", Assert.IsType<ParagraphBlock>(first[3]).Text);

        var table = Assert.IsType<TableBlock>(first[4]);
        Assert.Equal(3, table.ColumnCount);
        Assert.Equal(["Region", "Umsatz", "Anteil"], table.Rows[0]);
        Assert.Equal(["Süd", "2.010,75", "55 %"], table.Rows[3]);

        var image = Assert.IsType<ImageBlock>(first[5]);
        Assert.InRange(image.WidthPt, 195, 205);
        Assert.Equal("Ende der ersten Seite.", Assert.IsType<ParagraphBlock>(first[6]).Text);
    }

    /// <summary>
    /// A LibreOffice export whose five columns are only about 6 pt apart (a word space is 3 pt) — found with
    /// gaps shared by all rows, not by gaps within one line. The header row has the same tight spacing.
    /// </summary>
    [Fact]
    public void Tight_table_from_another_program_is_read_column_by_column()
    {
        var layout = PdfLayoutReader.Read(Path.Combine(AppContext.BaseDirectory, "TestData", "tight-table.pdf"));
        var blocks = layout.Pages[0].Blocks;
        Assert.Equal(("Quartalsbericht Q3 2026", 1), (Assert.IsType<ParagraphBlock>(blocks[0]).Text, ((ParagraphBlock)blocks[0]).HeadingLevel));

        var table = Assert.Single(blocks.OfType<TableBlock>());
        Assert.Equal(6, table.Rows.Count);
        Assert.Equal(["Abteilung", "Juli", "August", "September", "Summe"], table.Rows[0]);
        Assert.Equal(["Vertrieb", "12.400", "13.850", "15.200", "41.450"], table.Rows[1]);
        Assert.Equal(["Verwaltung", "2.700", "2.650", "2.900", "8.250"], table.Rows[5]);
        Assert.Equal("Alle Werte in Euro. Die Verarbeitung erfolgt lokal auf dem PC.", Assert.IsType<ParagraphBlock>(blocks[^1]).Text);
    }

    [Fact]
    public void Running_text_is_never_mistaken_for_a_table()
    {
        Assert.Empty(PdfLayoutReader.Read(fixture.TextOnly).Tables);
        var report = PdfLayoutReader.Read(fixture.Report);
        Assert.Single(report.Tables);
    }

    [Fact]
    public void Arabic_text_is_marked_right_to_left()
    {
        var layout = PdfLayoutReader.Read(fixture.Report);
        var arabic = layout.Pages[1].Blocks.OfType<ParagraphBlock>().Single(p => Script.IsMostlyRightToLeft(p.Text));
        Assert.True(arabic.RightToLeft);
        Assert.Contains("مرحبا", arabic.Text);
        Assert.Contains("بالعالم", arabic.Text);
        Assert.False(layout.MostlyRightToLeft); // the document as a whole is German
    }

    [Theory]
    [InlineData("1.234,50", 1234.5)]
    [InlineData("1,234.50", 1234.5)]
    [InlineData("980,00", 980)]
    [InlineData("3,5", 3.5)]
    [InlineData("0.125", 0.125)]
    [InlineData("12.5", 12.5)]
    [InlineData("1.234", 1234)]
    [InlineData("1.234.567", 1234567)]
    [InlineData("1 234,5", 1234.5)]
    [InlineData("-42", -42)]
    [InlineData("\u221242,5", -42.5)]
    public void Numbers_are_recognised_in_german_and_english(string text, double expected)
    {
        Assert.True(NumberText.TryParse(text, out var value));
        Assert.Equal(expected, value, 6);
    }

    [Theory]
    [InlineData("007")]
    [InlineData("25 %")]
    [InlineData("2026-11-14")]
    [InlineData("14.11.2026")]
    [InlineData("€ 12")]
    [InlineData("1.2.3")]
    [InlineData("Nord")]
    [InlineData("")]
    public void Doubtful_values_stay_text(string text) => Assert.False(NumberText.TryParse(text, out _));

    // ---------- Word ----------

    [Fact]
    public async Task Word_file_is_valid_and_keeps_the_structure()
    {
        var docx = Assert.Single(await Run("pdf.to-docx", fixture.Report));
        Assert.Equal("report.docx", Path.GetFileName(docx));
        using var document = WordprocessingDocument.Open(docx, false);
        AssertValid(document);

        var body = document.MainDocumentPart!.Document!.Body!;
        var paragraphs = body.Elements<W.Paragraph>().ToList();
        Assert.Contains(paragraphs, p => p.ParagraphProperties?.ParagraphStyleId?.Val == "Heading1" && p.InnerText == ReportFixture.Title);
        Assert.Contains(paragraphs, p => p.ParagraphProperties?.ParagraphStyleId?.Val == "Heading2" && p.InnerText == "Ausblick");
        Assert.Contains(paragraphs, p => p.ParagraphProperties?.BiDi is not null && p.InnerText.Contains("مرحبا"));

        var table = Assert.Single(body.Elements<W.Table>());
        var rows = table.Elements<W.TableRow>().ToList();
        Assert.Equal(4, rows.Count);
        Assert.Equal(["Nord", "1.234,50", "25 %"], rows[1].Elements<W.TableCell>().Select(c => c.InnerText));

        Assert.Single(document.MainDocumentPart.ImageParts);
        Assert.Single(body.Descendants<W.Break>(), b => b.Type?.Value == W.BreakValues.Page);
        Assert.Equal(ReportFixture.Title, document.PackageProperties.Title);
    }

    [Fact]
    public async Task A_scanned_pdf_becomes_a_word_file_with_the_page_picture()
    {
        var docx = Assert.Single(await Run("pdf.to-docx", fixture.PhotoOnly));
        using var document = WordprocessingDocument.Open(docx, false);
        AssertValid(document);
        Assert.Single(document.MainDocumentPart!.ImageParts);
    }

    // ---------- Excel ----------

    [Fact]
    public async Task Excel_file_has_one_sheet_per_table_with_real_numbers()
    {
        var xlsx = Assert.Single(await Run("pdf.to-xlsx", fixture.Report));
        using var document = SpreadsheetDocument.Open(xlsx, false);
        AssertValid(document);

        var workbook = document.WorkbookPart!;
        var sheet = Assert.Single(workbook.Workbook!.Sheets!.Elements<X.Sheet>());
        Assert.Equal("Table 1", sheet.Name?.Value);

        var data = ((WorksheetPart)workbook.GetPartById(sheet.Id!)).Worksheet!.GetFirstChild<X.SheetData>()!;
        var cells = data.Descendants<X.Cell>().ToDictionary(c => c.CellReference!.Value!);
        Assert.Equal("Region", cells["A1"].InnerText);
        Assert.Equal(1U, cells["A1"].StyleIndex!.Value); // bold header
        Assert.Equal(X.CellValues.Number, cells["B2"].DataType!.Value);
        Assert.Equal(1234.5, double.Parse(cells["B2"].CellValue!.Text, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(4U, cells["B2"].StyleIndex!.Value); // "#,##0.00" like "1.234,50" in the PDF
        Assert.Equal(2010.75, double.Parse(cells["B4"].CellValue!.Text, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("25 %", cells["C2"].InnerText); // percentages stay as written
        Assert.Equal("Süd", cells["A4"].InnerText);
    }

    [Fact]
    public async Task Excel_from_a_pdf_without_tables_lists_the_text()
    {
        var xlsx = Assert.Single(await Run("pdf.to-xlsx", fixture.TextOnly));
        using var document = SpreadsheetDocument.Open(xlsx, false);
        AssertValid(document);
        var sheet = Assert.Single(document.WorkbookPart!.Workbook!.Sheets!.Elements<X.Sheet>());
        Assert.Equal("Text", sheet.Name?.Value);
        var text = ((WorksheetPart)document.WorkbookPart.GetPartById(sheet.Id!)).Worksheet!.InnerText;
        Assert.Contains("Sehr geehrte Damen und Herren,", text);
        Assert.Contains("vielen Dank für Ihre Anfrage.", text);
    }

    [Fact]
    public async Task Excel_from_a_scan_explains_that_there_is_no_text()
    {
        var error = await Assert.ThrowsAsync<OperationException>(() => Run("pdf.to-xlsx", fixture.PhotoOnly));
        Assert.Equal("error.pdf-no-text", error.Key);
    }

    // ---------- PowerPoint ----------

    [Fact]
    public async Task PowerPoint_has_one_slide_per_page_in_page_size()
    {
        var pptx = Assert.Single(await Run("pdf.to-pptx", fixture.Report));
        using var document = PresentationDocument.Open(pptx, false);
        AssertValid(document);
        var presentation = document.PresentationPart!;
        Assert.Equal(2, presentation.SlideParts.Count());
        Assert.All(presentation.SlideParts, s => Assert.Single(s.ImageParts));
        var size = presentation.Presentation!.SlideSize!;
        // A4 portrait: 595 × 842 pt.
        Assert.InRange(size.Cx!.Value / 12700.0, 590, 600);
        Assert.InRange(size.Cy!.Value / 12700.0, 837, 847);
    }

    // ---------- Markdown and EPUB ----------

    [Fact]
    public async Task Markdown_has_headings_a_pipe_table_and_the_picture_file()
    {
        var outputs = await Run("pdf.to-md", fixture.Report);
        var markdown = File.ReadAllText(outputs[0]);
        Assert.Contains("# " + ReportFixture.Title, markdown);
        Assert.Contains("## Umsatz nach Region", markdown);
        Assert.Contains("| Region | Umsatz | Anteil |", markdown);
        Assert.Contains("| --- | --- | --- |", markdown);
        Assert.Contains("| Süd | 2.010,75 | 55 % |", markdown);
        var picture = Assert.Single(outputs.Skip(1));
        Assert.True(File.Exists(picture));
        Assert.Contains("](report-images/image-01.png)", markdown);
    }

    [Fact]
    public async Task Epub_is_a_well_formed_book()
    {
        var epub = Assert.Single(await Run("pdf.to-epub", fixture.Report));
        using var zip = ZipFile.OpenRead(epub);
        var first = zip.Entries[0];
        Assert.Equal("mimetype", first.FullName);
        Assert.Equal(first.Length, first.CompressedLength); // stored, not compressed
        using (var reader = new StreamReader(first.Open())) Assert.Equal("application/epub+zip", reader.ReadToEnd());

        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".xhtml") || e.FullName.EndsWith(".opf") || e.FullName.EndsWith(".xml")))
        {
            using var stream = entry.Open();
            XDocument.Load(stream); // throws when not well-formed
        }
        Assert.Equal(2, zip.Entries.Count(e => e.FullName.StartsWith("OEBPS/page-")));
        Assert.Single(zip.Entries, e => e.FullName.StartsWith("OEBPS/images/"));
        using var opf = new StreamReader(zip.GetEntry("OEBPS/content.opf")!.Open());
        Assert.Contains($"<dc:title>{ReportFixture.Title}</dc:title>", opf.ReadToEnd());
    }

    // ---------- wheel and errors ----------

    [Fact]
    public void The_convert_wheel_starts_with_word_excel_powerpoint()
    {
        var ids = Registry.Match(WheelKind.Convert, new InputSet(["a.pdf"])).Select(o => o.Id).ToList();
        Assert.Equal(["pdf.to-docx", "pdf.to-xlsx", "pdf.to-pptx", "pdf.to-png", "pdf.to-jpg", "pdf.to-txt", "pdf.to-md", "pdf.to-epub"], ids);
    }

    [Theory]
    [InlineData("pdf.to-docx")]
    [InlineData("pdf.to-xlsx")]
    [InlineData("pdf.to-pptx")]
    [InlineData("pdf.to-epub")]
    public async Task Protected_files_get_a_clear_message(string id)
    {
        var error = await Assert.ThrowsAsync<OperationException>(() => Run(id, fixture.Protected));
        Assert.Equal("error.pdf-protected", error.Key);
    }

    // ---------- a real office suite ----------

    [Theory]
    [InlineData("pdf.to-docx")]
    [InlineData("pdf.to-xlsx")]
    [InlineData("pdf.to-pptx")]
    public async Task LibreOffice_opens_the_result(string id)
    {
        const string soffice = @"C:\Program Files\LibreOffice\program\soffice.exe";
        if (!File.Exists(soffice)) return; // not installed on this machine

        var file = Assert.Single(await Run(id, fixture.Report));
        var outFolder = fixture.NewOutputFolder();
        var profile = new Uri(Path.Combine(fixture.Folder, "lo-profile-" + Guid.NewGuid().ToString("N")[..6])).AbsoluteUri;
        var start = new ProcessStartInfo(soffice) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { $"-env:UserInstallation={profile}", "--headless", "--norestore", "--convert-to", "pdf", "--outdir", outFolder, file })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        Assert.True(process.WaitForExit(120_000), "LibreOffice did not finish");
        var pdf = Path.Combine(outFolder, Path.GetFileNameWithoutExtension(file) + ".pdf");
        Assert.True(File.Exists(pdf), $"LibreOffice could not open {Path.GetFileName(file)}");
        using var reread = UglyToad.PdfPig.PdfDocument.Open(pdf);
        Assert.True(reread.NumberOfPages >= 1);
    }
}
