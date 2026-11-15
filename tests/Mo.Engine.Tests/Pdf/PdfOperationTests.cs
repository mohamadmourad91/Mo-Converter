using ImageMagick;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Mo.Engine.Tests.Pdf;

/// <summary>Sample PDFs made once per test run: text pages, a photo page, a protected file and a broken one.</summary>
public sealed class PdfFixture : IDisposable
{
    public string Folder { get; } = Directory.CreateTempSubdirectory("mo-pdf-").FullName;

    /// <summary>Three A4 pages saying "Page 1" … "Page 3", with title and author set.</summary>
    public string ThreePages { get; }

    /// <summary>One A4 page with text.</summary>
    public string OnePage { get; }

    /// <summary>One page holding only a large, noisy JPEG (no text) — compressible, nothing to extract.</summary>
    public string PhotoOnly { get; }

    public string Protected { get; }
    public string Broken { get; }

    public PdfFixture()
    {
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;

        ThreePages = TextPdf("three.pdf", 3, document =>
        {
            document.Info.Title = "Secret title";
            document.Info.Author = "Secret author";
            document.Info.Subject = "Secret subject";
            document.Info.Keywords = "secret, keywords";
        });
        OnePage = TextPdf("one.pdf", 1);

        var photo = Path.Combine(Folder, "photo.jpg");
        using (var image = new MagickImage(MagickColors.White, 1800, 1200))
        {
            image.AddNoise(NoiseType.Gaussian);
            image.Quality = 98;
            image.Write(photo, MagickFormat.Jpeg);
        }
        PhotoOnly = Path.Combine(Folder, "photo.pdf");
        using (var document = new PdfDocument())
        {
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            using var picture = XImage.FromFile(photo);
            gfx.DrawImage(picture, 0, 0, page.Width.Point, page.Height.Point);
            gfx.Dispose();
            document.Save(PhotoOnly);
        }

        Protected = TextPdf("protected.pdf", 2, document => document.SecuritySettings.UserPassword = "geheim");

        Broken = Path.Combine(Folder, "broken.pdf");
        File.WriteAllText(Broken, "%PDF-1.7 this is not really a PDF");
    }

    private string TextPdf(string name, int pages, Action<PdfDocument>? configure = null)
    {
        var path = Path.Combine(Folder, name);
        using var document = new PdfDocument();
        var font = new XFont("Arial", 28);
        for (var i = 1; i <= pages; i++)
        {
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString($"Page {i}", font, XBrushes.Black, new XPoint(72, 120));
            gfx.DrawString("Mo Converter", font, XBrushes.DarkBlue, new XPoint(72, 180));
        }
        configure?.Invoke(document);
        document.Save(path);
        return path;
    }

    public string NewOutputFolder() => Directory.CreateDirectory(Path.Combine(Folder, "out-" + Guid.NewGuid().ToString("N")[..6])).FullName;

    public void Dispose() => Directory.Delete(Folder, true);
}

public sealed class PdfOperationTests(PdfFixture fixture) : IClassFixture<PdfFixture>
{
    private static readonly OperationRegistry Registry = OperationRegistry.CreateDefault();

    private async Task<IReadOnlyList<string>> Run(string id, params string[] files) =>
        (await Registry.Find(id)!.RunAsync(
            new OperationContext(new InputSet(files), new OutputPlanner(fixture.NewOutputFolder())), CancellationToken.None)).Outputs;

    private static int PageCount(string pdf)
    {
        using var document = PdfReader.Open(pdf, PdfDocumentOpenMode.Import);
        return document.PageCount;
    }

    // ---------- wheels ----------

    [Fact]
    public void One_pdf_offers_conversions_and_tools_but_not_merge()
    {
        var input = new InputSet([fixture.ThreePages]);
        Assert.Equal(["pdf.to-docx", "pdf.to-xlsx", "pdf.to-pptx", "pdf.to-png", "pdf.to-jpg", "pdf.to-txt", "pdf.to-md", "pdf.to-epub"],
            Registry.Match(WheelKind.Convert, input).Where(o => o.Id.StartsWith("pdf.")).Select(o => o.Id));
        Assert.Equal(["pdf.organize", "pdf.split", "pdf.rotate", "pdf.compress", "pdf.ocr", "pdf.strip-metadata"],
            Registry.Match(WheelKind.Tools, input).Where(o => o.Id.StartsWith("pdf.")).Select(o => o.Id));
    }

    [Fact]
    public void Merge_needs_two_or_more_pdfs_and_nothing_else()
    {
        var merge = Registry.Find("pdf.merge")!;
        Assert.True(merge.Accepts(new InputSet(["a.pdf", "b.pdf"])));
        Assert.False(merge.Accepts(new InputSet(["a.pdf"])));
        Assert.False(merge.Accepts(new InputSet(["a.pdf", "b.png"])));
    }

    [Fact]
    public void Pdf_operations_ignore_images()
    {
        foreach (var wheel in Enum.GetValues<WheelKind>())
            Assert.DoesNotContain(Registry.Match(wheel, new InputSet(["photo.png"])), o => o.Id.StartsWith("pdf."));
    }

    // ---------- to images ----------

    [Fact]
    public async Task Pages_become_numbered_pngs_in_a_folder()
    {
        var outputs = await Run("pdf.to-png", fixture.ThreePages);

        Assert.Equal(["three-p01.png", "three-p02.png", "three-p03.png"], outputs.Select(Path.GetFileName));
        Assert.All(outputs, o => Assert.Equal("three-pages", Path.GetFileName(Path.GetDirectoryName(o))));
        using var first = new MagickImage(outputs[0]);
        Assert.Equal(MagickFormat.Png, first.Format);
        // A4 is 595 pt wide: 595 / 72 * 150 dpi ≈ 1240 px.
        Assert.InRange(first.Width, 1236u, 1244u);
    }

    [Fact]
    public async Task A_single_page_becomes_one_jpg_without_a_folder()
    {
        var output = Assert.Single(await Run("pdf.to-jpg", fixture.OnePage));
        Assert.Equal("one.jpg", Path.GetFileName(output));
        using var image = new MagickImage(output);
        Assert.Equal(MagickFormat.Jpeg, image.Format);
        // The page is white with dark text: the corner must be white, not transparent black.
        Assert.True(image.GetPixels().GetPixel(5, 5).ToColor()!.R > 240);
    }

    [Fact]
    public async Task Page_images_have_a_white_background_not_a_transparent_one()
    {
        var png = Assert.Single(await Run("pdf.to-png", fixture.OnePage));
        using var image = new MagickImage(png);
        var corner = image.GetPixels().GetPixel(3, 3).ToColor()!;
        Assert.Equal(255, corner.A);                         // opaque: no see-through page (8-bit colour values)
        Assert.True(corner.R > 240 && corner.G > 240 && corner.B > 240, $"corner {corner}");
    }

    [Fact]
    public async Task Jpg_pages_are_real_jpeg_files_of_the_right_size()
    {
        var jpg = Assert.Single(await Run("pdf.to-jpg", fixture.OnePage));
        var bytes = File.ReadAllBytes(jpg);
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF }, bytes[..3]);   // JPEG signature
        using var image = new MagickImage(jpg);
        Assert.InRange(image.Width, 1236u, 1244u);                    // A4 at 150 dpi
        Assert.InRange(bytes.Length, 20_000, 2_000_000);
    }

    // ---------- text ----------

    [Fact]
    public async Task Text_is_extracted_page_by_page_as_utf8()
    {
        var output = Assert.Single(await Run("pdf.to-txt", fixture.ThreePages));
        var bytes = File.ReadAllBytes(output);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);

        var text = File.ReadAllText(output);
        Assert.Contains("Page 1", text);
        Assert.Contains("Page 3", text);
        Assert.Contains("Mo Converter", text);
        Assert.Equal(2, text.Count(c => c == '\f'));
        Assert.True(text.IndexOf("Page 1") < text.IndexOf("Page 2"));
    }

    [Fact]
    public async Task A_pdf_without_text_says_so()
    {
        var error = await Assert.ThrowsAsync<OperationException>(() => Run("pdf.to-txt", fixture.PhotoOnly));
        Assert.Equal("error.pdf-no-text", error.Key);
    }

    // ---------- merge, split, rotate ----------

    [Fact]
    public async Task Merge_keeps_the_order_and_every_page()
    {
        var output = Assert.Single(await Run("pdf.merge", fixture.ThreePages, fixture.OnePage));
        Assert.Equal("three-merged.pdf", Path.GetFileName(output));
        Assert.Equal(4, PageCount(output));
        using var pig = UglyToad.PdfPig.PdfDocument.Open(output);
        Assert.Contains("Page 3", pig.GetPage(3).Text);
        Assert.Contains("Page 1", pig.GetPage(4).Text);
    }

    [Fact]
    public async Task Split_makes_one_file_per_page()
    {
        var outputs = await Run("pdf.split", fixture.ThreePages);
        Assert.Equal(["three-p01.pdf", "three-p02.pdf", "three-p03.pdf"], outputs.Select(Path.GetFileName));
        Assert.All(outputs, o => Assert.Equal(1, PageCount(o)));
        using var pig = UglyToad.PdfPig.PdfDocument.Open(outputs[1]);
        Assert.Contains("Page 2", pig.GetPage(1).Text);
    }

    [Fact]
    public async Task Split_refuses_a_single_page()
    {
        var error = await Assert.ThrowsAsync<OperationException>(() => Run("pdf.split", fixture.OnePage));
        Assert.Equal("error.pdf-single-page", error.Key);
    }

    [Fact]
    public async Task Rotate_turns_every_page_and_adds_up()
    {
        var once = Assert.Single(await Run("pdf.rotate", fixture.ThreePages));
        var twice = Assert.Single(await Run("pdf.rotate", once));
        using var first = PdfReader.Open(once, PdfDocumentOpenMode.Import);
        using var second = PdfReader.Open(twice, PdfDocumentOpenMode.Import);
        Assert.All(first.Pages.Cast<PdfPage>(), p => Assert.Equal(90, p.Rotate));
        Assert.All(second.Pages.Cast<PdfPage>(), p => Assert.Equal(180, p.Rotate));
        Assert.Equal(3, first.PageCount);
    }

    // ---------- compress, metadata ----------

    [Fact]
    public async Task Compress_shrinks_a_photo_pdf_and_keeps_it_readable()
    {
        var output = Assert.Single(await Run("pdf.compress", fixture.PhotoOnly));
        Assert.Equal("photo-compressed.pdf", Path.GetFileName(output));
        Assert.True(new FileInfo(output).Length < new FileInfo(fixture.PhotoOnly).Length * 0.8,
            $"{new FileInfo(output).Length} vs {new FileInfo(fixture.PhotoOnly).Length}");
        Assert.Equal(1, PageCount(output));
        // Still renders: the page becomes a proper image.
        var rendered = Assert.Single(await Run("pdf.to-png", output));
        using var image = new MagickImage(rendered);
        Assert.True(image.Width > 1000);
    }

    [Fact]
    public async Task Compress_never_makes_a_file_bigger()
    {
        var output = Assert.Single(await Run("pdf.compress", fixture.OnePage));
        Assert.True(new FileInfo(output).Length <= new FileInfo(fixture.OnePage).Length);
        Assert.Equal(1, PageCount(output));
    }

    [Fact]
    public async Task Metadata_is_removed_but_pages_stay()
    {
        var output = Assert.Single(await Run("pdf.strip-metadata", fixture.ThreePages));
        Assert.Equal(3, PageCount(output));
        using var pig = UglyToad.PdfPig.PdfDocument.Open(output);
        var info = pig.Information;
        Assert.True(string.IsNullOrEmpty(info.Title), info.Title);
        Assert.True(string.IsNullOrEmpty(info.Author), info.Author);
        Assert.True(string.IsNullOrEmpty(info.Subject), info.Subject);
        Assert.True(string.IsNullOrEmpty(info.Keywords), info.Keywords);
        Assert.DoesNotContain("Secret", File.ReadAllText(output, System.Text.Encoding.Latin1));
    }

    // ---------- errors ----------

    [Theory]
    [InlineData("pdf.rotate")]
    [InlineData("pdf.split")]
    [InlineData("pdf.compress")]
    [InlineData("pdf.to-txt")]
    [InlineData("pdf.to-png")]
    public async Task Protected_files_get_a_clear_message(string id)
    {
        var error = await Assert.ThrowsAsync<OperationException>(() => Run(id, fixture.Protected));
        Assert.Equal("error.pdf-protected", error.Key);
        Assert.Equal("protected.pdf", error.Args[0]);
    }

    [Theory]
    [InlineData("pdf.rotate")]
    [InlineData("pdf.to-txt")]
    [InlineData("pdf.to-jpg")]
    [InlineData("pdf.strip-metadata")]
    public async Task Broken_files_get_a_clear_message(string id)
    {
        var error = await Assert.ThrowsAsync<OperationException>(() => Run(id, fixture.Broken));
        Assert.Equal("error.pdf-unreadable", error.Key);
    }

    [Fact]
    public async Task Merge_with_a_protected_file_names_it()
    {
        var error = await Assert.ThrowsAsync<OperationException>(() => Run("pdf.merge", fixture.OnePage, fixture.Protected));
        Assert.Equal("error.pdf-protected", error.Key);
    }

    [Fact]
    public async Task Nothing_is_overwritten()
    {
        var folder = fixture.NewOutputFolder();
        var planner = new OutputPlanner(folder);
        var rotate = Registry.Find("pdf.rotate")!;
        var input = new InputSet([fixture.OnePage]);
        var a = (await rotate.RunAsync(new OperationContext(input, planner), CancellationToken.None)).Outputs[0];
        var b = (await rotate.RunAsync(new OperationContext(input, planner), CancellationToken.None)).Outputs[0];
        Assert.NotEqual(a, b);
        Assert.True(File.Exists(a) && File.Exists(b));
    }
}
