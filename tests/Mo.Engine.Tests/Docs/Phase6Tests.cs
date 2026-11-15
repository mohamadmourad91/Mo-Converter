using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Mo.Engine.Office;
using W = DocumentFormat.OpenXml.Wordprocessing;
using X = DocumentFormat.OpenXml.Spreadsheet;

namespace Mo.Engine.Tests.Docs;

public sealed class Phase6Tests(Phase6Fixture fixture) : IClassFixture<Phase6Fixture>
{
    private static readonly OperationRegistry Registry = OperationRegistry.CreateDefault();

    private async Task<IReadOnlyList<string>> Run(string id, params string[] files)
    {
        Localizer.Current = new Localizer("en");
        return (await Registry.Find(id)!.RunAsync(
            new OperationContext(new InputSet(files), new OutputPlanner(fixture.NewOutputFolder())), CancellationToken.None)).Outputs;
    }

    private static void AssertValid(OpenXmlPackage package)
    {
        var errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package).Select(e => $"{e.Path?.XPath}: {e.Description}").ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors.Take(10)));
    }

    private static string PdfText(string pdf)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(pdf);
        return string.Join("\n", document.GetPages().Select(p => p.Text));
    }

    // ---------- wheels ----------

    [Fact]
    public void Office_options_appear_only_with_libreoffice()
    {
        var word = new InputSet(["a.docx"]);
        try
        {
            LibreOffice.AvailabilityOverride = () => false;
            Assert.Equal(["doc.to-md", "doc.to-txt", "doc.to-html", "doc.to-epub"], Registry.Match(WheelKind.Convert, word).Select(o => o.Id));
            Assert.Empty(Registry.Match(WheelKind.Convert, new InputSet(["old.doc"])));
            Assert.Empty(Registry.Match(WheelKind.Convert, new InputSet(["talk.pptx"])));
            Assert.Equal(["sheet.to-xlsx"], Registry.Match(WheelKind.Convert, new InputSet(["a.csv"])).Select(o => o.Id));

            LibreOffice.AvailabilityOverride = () => true;
            Assert.Equal(["doc.to-pdf", "doc.to-odt", "doc.to-md", "doc.to-txt", "doc.to-html", "doc.to-epub"], Registry.Match(WheelKind.Convert, word).Select(o => o.Id));
            Assert.Equal(["slides.to-pdf", "slides.to-png"], Registry.Match(WheelKind.Convert, new InputSet(["talk.pptx"])).Select(o => o.Id));
            Assert.Contains(Registry.Match(WheelKind.Convert, new InputSet(["talk.ppt"])), o => o.Id == "slides.to-pptx");
        }
        finally
        {
            LibreOffice.AvailabilityOverride = null;
        }
    }

    [Fact]
    public void Markdown_offers_word_but_not_markdown()
    {
        var ids = Registry.Match(WheelKind.Convert, new InputSet(["notes.md"])).Select(o => o.Id).ToList();
        Assert.Contains("doc.to-docx", ids);
        Assert.DoesNotContain("doc.to-md", ids);
    }

    // ---------- documents ----------

    [Fact]
    public async Task Markdown_becomes_a_valid_word_file_with_structure_and_picture()
    {
        var docx = Assert.Single(await Run("doc.to-docx", fixture.Markdown));
        using var document = WordprocessingDocument.Open(docx, false);
        AssertValid(document);
        var paragraphs = document.MainDocumentPart!.Document!.Body!.Elements<W.Paragraph>().ToList();
        Assert.Contains(paragraphs, p => p.ParagraphProperties?.ParagraphStyleId?.Val == "Heading1" && p.InnerText == "Projektplan");
        Assert.Contains(paragraphs, p => p.ParagraphProperties?.ParagraphStyleId?.Val == "Heading2" && p.InnerText == "Aufgaben");
        Assert.Contains(paragraphs, p => p.InnerText == "Der Plan hat drei Phasen und einen Link.");
        Assert.Contains(paragraphs, p => p.InnerText == "• Entwurf");
        Assert.Contains(paragraphs, p => p.InnerText == "2. Zweitens");
        var table = Assert.Single(document.MainDocumentPart.Document.Body.Elements<W.Table>());
        Assert.Equal(["Entwurf", "2 Wochen"], table.Elements<W.TableRow>().ElementAt(1).Elements<W.TableCell>().Select(c => c.InnerText));
        Assert.Single(document.MainDocumentPart.ImageParts);
    }

    [Fact]
    public async Task Markdown_becomes_a_web_page_with_formatting()
    {
        var html = File.ReadAllText(Assert.Single(await Run("doc.to-html", fixture.Markdown)));
        Assert.Contains("<h1", html);
        Assert.Contains("<strong>drei</strong>", html);
        Assert.Contains("<table>", html);
        Assert.Contains("href=\"https://example.com\"", html);
        Assert.Contains("<meta charset=\"utf-8\">", html);
    }

    [Fact]
    public async Task Markdown_becomes_an_ebook()
    {
        using var zip = ZipFile.OpenRead(Assert.Single(await Run("doc.to-epub", fixture.Markdown)));
        Assert.Equal("mimetype", zip.Entries[0].FullName);
        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".xhtml")))
        {
            using var stream = entry.Open();
            XDocument.Load(stream);
        }
    }

    [Fact]
    public async Task Old_text_files_keep_their_umlauts()
    {
        var docx = Assert.Single(await Run("doc.to-docx", fixture.Latin1Text));
        using var document = WordprocessingDocument.Open(docx, false);
        var text = document.MainDocumentPart!.Document!.Body!.InnerText;
        Assert.Contains("Frau Müller", text);
        Assert.Contains("die Größe passt.", text);
        Assert.Contains("Grüße", text);
    }

    [Fact]
    public async Task Word_from_libreoffice_becomes_markdown_with_headings_and_table()
    {
        if (!Phase6Fixture.HasLibreOffice) return;
        var markdown = File.ReadAllText(Assert.Single(await Run("doc.to-md", fixture.WordFromLibreOffice)));
        Assert.Contains("# Jahresbericht", markdown);
        Assert.Contains("## Zahlen", markdown);
        Assert.Contains("| Monat | Umsatz |", markdown);
        Assert.Contains("| Januar | 1.200 |", markdown);
    }

    [Fact]
    public async Task Word_becomes_plain_text_with_tab_separated_tables()
    {
        if (!Phase6Fixture.HasLibreOffice) return;
        var text = File.ReadAllText(Assert.Single(await Run("doc.to-txt", fixture.WordFromLibreOffice)));
        Assert.Contains("Jahresbericht", text);
        Assert.Contains("Januar\t1.200", text);
    }

    [Theory]
    [InlineData("doc.to-pdf")]
    [InlineData("doc.to-odt")]
    public async Task Libreoffice_converts_word(string id)
    {
        if (!Phase6Fixture.HasLibreOffice) return;
        var output = Assert.Single(await Run(id, fixture.WordFromLibreOffice));
        if (id == "doc.to-pdf") Assert.Contains("Jahresbericht", PdfText(output));
        else
        {
            using var zip = ZipFile.OpenRead(output);
            using var reader = new StreamReader(zip.GetEntry("mimetype")!.Open());
            Assert.Equal("application/vnd.oasis.opendocument.text", reader.ReadToEnd());
        }
    }

    [Fact]
    public async Task Markdown_becomes_pdf_through_word()
    {
        if (!Phase6Fixture.HasLibreOffice) return;
        var text = PdfText(Assert.Single(await Run("doc.to-pdf", fixture.Markdown)));
        Assert.Contains("Projektplan", text);
        Assert.Contains("Umsetzung", text);
    }

    // ---------- spreadsheets ----------

    [Fact]
    public async Task German_csv_becomes_excel_with_real_numbers()
    {
        var xlsx = Assert.Single(await Run("sheet.to-xlsx", fixture.GermanCsv));
        using var document = SpreadsheetDocument.Open(xlsx, false);
        AssertValid(document);
        var sheet = Assert.Single(document.WorkbookPart!.Workbook!.Sheets!.Elements<X.Sheet>());
        Assert.Equal("umsatz", sheet.Name?.Value);
        var cells = ((WorksheetPart)document.WorkbookPart.GetPartById(sheet.Id!)).Worksheet!.Descendants<X.Cell>().ToDictionary(c => c.CellReference!.Value!);
        Assert.Equal("1234.5", cells["B2"].CellValue!.Text);
        Assert.Equal("enthält ; und\nZeilenumbruch", cells["C2"].InnerText);
        Assert.Equal("Süd", cells["A3"].InnerText);
    }

    [Fact]
    public async Task English_csv_with_quotes_is_detected()
    {
        var xlsx = Assert.Single(await Run("sheet.to-xlsx", fixture.EnglishCsv));
        using var document = SpreadsheetDocument.Open(xlsx, false);
        var sheet = document.WorkbookPart!.Workbook!.Sheets!.Elements<X.Sheet>().Single();
        var cells = ((WorksheetPart)document.WorkbookPart.GetPartById(sheet.Id!)).Worksheet!.Descendants<X.Cell>().ToDictionary(c => c.CellReference!.Value!);
        Assert.Equal("1234.5", cells["B2"].CellValue!.Text);
        Assert.Equal("say \"hi\"", cells["C3"].InnerText);
    }

    [Fact]
    public async Task Workbook_with_several_sheets_becomes_one_csv_per_sheet_in_the_regional_format()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var files = await Run("sheet.to-csv", fixture.TwoSheets);
            Assert.Equal(["Notizen.csv", "Zahlen.csv"], files.Select(Path.GetFileName).Order()); // the empty sheet is skipped
            var numbers = File.ReadAllText(files.Single(f => f.EndsWith("Zahlen.csv")));
            Assert.Equal("Datum;Betrag;\r\n2026-11-14;1234,5;\r\n;;\r\n;;7\r\n", numbers);
            Assert.Equal("\"Hallo; Welt\"\r\n", File.ReadAllText(files.Single(f => f.EndsWith("Notizen.csv"))));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task Excel_becomes_pdf_with_libreoffice()
    {
        if (!Phase6Fixture.HasLibreOffice) return;
        Assert.Contains("Betrag", PdfText(Assert.Single(await Run("sheet.to-pdf", fixture.TwoSheets))));
    }

    // ---------- presentations ----------

    [Fact]
    public async Task Slides_become_one_picture_each()
    {
        if (!Phase6Fixture.HasLibreOffice) return;
        var pictures = await Run("slides.to-png", fixture.Slides);
        Assert.Equal(["vortrag-p01.png", "vortrag-p02.png"], pictures.Select(Path.GetFileName));
        using var image = new ImageMagick.MagickImage(pictures[0]);
        Assert.True(image.Width > image.Height); // 16:9 slides
    }

    [Fact]
    public async Task Slides_become_a_pdf()
    {
        if (!Phase6Fixture.HasLibreOffice) return;
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(Assert.Single(await Run("slides.to-pdf", fixture.Slides)));
        Assert.Equal(2, pdf.NumberOfPages);
    }

    // ---------- archives ----------

    [Fact]
    public async Task Zip_is_extracted_into_a_folder_named_after_it()
    {
        var folder = Assert.Single(await Run("archive.extract", fixture.Zip));
        Assert.Equal("photos", Path.GetFileName(folder));
        Assert.Equal("A", File.ReadAllText(Path.Combine(folder, "a.txt")));
        Assert.Equal("B", File.ReadAllText(Path.Combine(folder, "sub", "b.txt")));
    }

    [Fact]
    public async Task Tar_gz_is_extracted_and_repacked_as_zip()
    {
        var folder = Assert.Single(await Run("archive.extract", fixture.TarGz));
        Assert.Equal("backup", Path.GetFileName(folder));
        Assert.True(File.Exists(Path.Combine(folder, "sub", "b.txt")));

        var zip = Assert.Single(await Run("archive.to-zip", fixture.TarGz));
        Assert.Equal("backup.zip", Path.GetFileName(zip));
        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal(["a.txt", "sub/b.txt"], archive.Entries.Where(e => e.Length > 0).Select(e => e.FullName.Replace('\\', '/')).Order());
    }

    [Fact]
    public async Task Entries_escaping_the_folder_are_refused_and_nothing_is_left()
    {
        var output = fixture.NewOutputFolder();
        var error = await Assert.ThrowsAsync<OperationException>(() =>
            Registry.Find("archive.extract")!.RunAsync(new OperationContext(new InputSet([fixture.EvilZip]), new OutputPlanner(output)), CancellationToken.None));
        Assert.Equal("error.archive-unsafe", error.Key);
        Assert.False(File.Exists(Path.Combine(fixture.Folder, "escaped.txt")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(fixture.Folder)!, "escaped.txt")));
        Assert.Empty(Directory.GetFiles(output, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Extracting_twice_never_mixes_folders()
    {
        var output = fixture.NewOutputFolder();
        var planner = new OutputPlanner(output);
        var extract = Registry.Find("archive.extract")!;
        var first = (await extract.RunAsync(new OperationContext(new InputSet([fixture.Zip]), planner), CancellationToken.None)).Outputs[0];
        var second = (await extract.RunAsync(new OperationContext(new InputSet([fixture.Zip]), planner), CancellationToken.None)).Outputs[0];
        Assert.Equal("photos (2)", Path.GetFileName(second));
        Assert.NotEqual(first, second);
    }

    // ---------- subtitles ----------

    [Fact]
    public async Task Srt_becomes_webvtt()
    {
        var vtt = File.ReadAllText(Assert.Single(await Run("subtitle.to-vtt", fixture.Srt)));
        Assert.StartsWith("﻿WEBVTT\n\n00:00:01.000 --> 00:00:02.500\nHallo <i>Welt</i>\n", vtt);
        Assert.Contains("mit Umbruch &amp; mehr", vtt);
    }

    [Fact]
    public async Task Ass_becomes_srt_in_time_order_with_styles()
    {
        var srt = File.ReadAllText(Assert.Single(await Run("subtitle.to-srt", fixture.Ass))).TrimStart('﻿');
        Assert.Equal("1\n00:00:01,000 --> 00:00:02,000\n<i>Kursiv</i>\nzweite Zeile\n\n2\n00:00:05,000 --> 00:00:06,500\nSpäter, mit Komma\n\n", srt);
    }

    [Fact]
    public async Task Webvtt_with_notes_and_short_times_becomes_srt()
    {
        var srt = File.ReadAllText(Assert.Single(await Run("subtitle.to-srt", fixture.Vtt))).TrimStart('﻿');
        Assert.Equal("1\n00:00:01,000 --> 00:00:02,000\nErster\n\n2\n00:00:03,500 --> 00:00:04,000\nZweiter\n\n", srt);
    }

    [Fact]
    public async Task Youtube_sbv_becomes_srt()
    {
        var srt = File.ReadAllText(Assert.Single(await Run("subtitle.to-srt", fixture.Sbv))).TrimStart('﻿');
        Assert.Contains("00:00:03,000 --> 00:00:04,500\nZwei", srt);
    }

    [Fact]
    public async Task Spoken_text_drops_times_and_tags()
    {
        var text = File.ReadAllText(Assert.Single(await Run("subtitle.to-txt", fixture.Srt))).TrimStart('﻿');
        Assert.Equal("Hallo Welt\n\nZweite Zeile\nmit Umbruch & mehr\n", text);
    }

    [Fact]
    public async Task Srt_becomes_ass_with_line_breaks_and_italics()
    {
        var ass = File.ReadAllText(Assert.Single(await Run("subtitle.to-ass", fixture.Srt)));
        Assert.Contains("Dialogue: 0,0:00:01.00,0:00:02.50,Default,,0,0,0,,Hallo {\\i1}Welt{\\i0}", ass);
        Assert.Contains("Zweite Zeile\\Nmit Umbruch & mehr", ass);
    }
}
