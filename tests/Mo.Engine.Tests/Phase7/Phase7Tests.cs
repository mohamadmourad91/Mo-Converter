using System.Drawing;
using System.Drawing.Imaging;
using ImageMagick;
using Mo.Engine.Images;
using Mo.Engine.Media;
using Mo.Engine.Pdf;
using Mo.Engine.Tests.Media;
using Mo.Engine.Tests.Pdf;
using PdfSharp.Pdf.IO;

namespace Mo.Engine.Tests.Phase7;

/// <summary>Pictures with real text (drawn by Windows, so Arabic is shaped correctly) and a QR code.</summary>
public sealed class Phase7Fixture : IDisposable
{
    public string Folder { get; } = Directory.CreateTempSubdirectory("mo-p7-").FullName;
    public string GermanText { get; }
    public string ArabicText { get; }
    public string Qr { get; }
    public string Plain { get; }

    public const string QrContent = "https://example.com/mo-converter?lang=de";

    public Phase7Fixture()
    {
        GermanText = Draw("brief.png", "Sehr geehrte Damen und Herren\nMo Converter liest Text", rightToLeft: false);
        ArabicText = Draw("arabic.png", "مرحبا بكم في البرنامج", rightToLeft: true);
        Qr = Path.Combine(Folder, "qr.png");
        File.WriteAllBytes(Qr, QrCodes.Png(QrContent));
        Plain = Path.Combine(Folder, "plain.png");
        using (var image = new MagickImage(MagickColors.SteelBlue, 200, 100))
        {
            using var red = new MagickImage(MagickColors.Red, 20, 100);
            image.Composite(red, 0, 0, CompositeOperator.Over); // left edge red, for the mirror test
            image.Write(Plain, MagickFormat.Png);
        }
    }

    private string Draw(string name, string text, bool rightToLeft)
    {
        var path = Path.Combine(Folder, name);
        using var bitmap = new Bitmap(1400, 360);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.White);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using var font = new Font("Arial", 44);
            var format = new StringFormat();
            if (rightToLeft) format.FormatFlags |= StringFormatFlags.DirectionRightToLeft;
            g.DrawString(text, font, Brushes.Black, new RectangleF(40, 40, 1320, 300), format);
        }
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    public string NewOutputFolder() => Directory.CreateDirectory(Path.Combine(Folder, "out-" + Guid.NewGuid().ToString("N")[..6])).FullName;

    public void Dispose()
    {
        try { Directory.Delete(Folder, true); } catch (IOException) { }
    }
}

public sealed class Phase7Tests(Phase7Fixture fixture) : IClassFixture<Phase7Fixture>
{
    private static readonly OperationRegistry Registry = OperationRegistry.CreateDefault();

    private async Task<IReadOnlyList<string>> Run(string id, IReadOnlyDictionary<string, object?>? options, params string[] files) =>
        (await Registry.Find(id)!.RunAsync(
            new OperationContext(new InputSet(files), new OutputPlanner(fixture.NewOutputFolder()), Options: options), CancellationToken.None)).Outputs;

    // ---------- OCR ----------

    [Fact]
    public async Task German_text_is_read_from_a_picture()
    {
        var text = File.ReadAllText(Assert.Single(await Run("image.ocr", null, fixture.GermanText)));
        Assert.Contains("Sehr geehrte Damen und Herren", text);
        Assert.Contains("Mo Converter liest Text", text);
    }

    [Fact]
    public async Task Arabic_text_is_read_with_the_arabic_recogniser()
    {
        if (!TextRecognition.Languages.Any(l => l.StartsWith("ar"))) return; // Arabic OCR not installed on this PC
        var text = File.ReadAllText(Assert.Single(await Run("image.ocr", null, fixture.ArabicText)));
        Assert.Contains("مرحبا", text);
        Assert.Contains("البرنامج", text);
    }

    [Fact]
    public async Task Scanned_pdf_pages_are_read()
    {
        var pdf = Assert.Single(await Run("image.to-pdf", null, fixture.GermanText));
        var text = File.ReadAllText(Assert.Single(await Run("pdf.ocr", null, pdf)));
        Assert.Contains("Mo Converter liest Text", text);
    }

    [Fact]
    public async Task A_picture_without_text_says_so()
    {
        var error = await Assert.ThrowsAsync<OperationException>(() => Run("image.ocr", null, fixture.Plain));
        Assert.Equal("error.ocr-nothing", error.Key);
    }

    [Fact]
    public void Wrong_language_output_scores_lower()
    {
        Assert.True(TextRecognition.Score("Sehr geehrte Damen", rightToLeft: false) > TextRecognition.Score("Sehr geehrte Damen", rightToLeft: true));
        Assert.True(TextRecognition.Score("مرحبا بكم", rightToLeft: true) > TextRecognition.Score("(| ~ ;'", rightToLeft: false));
    }

    // ---------- QR ----------

    [Fact]
    public void Qr_codes_read_back_what_was_written()
    {
        Assert.Equal([Phase7Fixture.QrContent], QrCodes.Read(fixture.Qr));
        Assert.Empty(QrCodes.Read(fixture.Plain));
        Assert.Contains("<svg", QrCodes.Svg("hello"));
    }

    [Fact]
    public void Qr_code_in_a_photo_like_picture_is_found()
    {
        // A smaller code placed on a busy background, as in a photo of a poster.
        var path = Path.Combine(fixture.NewOutputFolder(), "poster.jpg");
        using (var poster = new MagickImage(MagickColors.SteelBlue, 1600, 1200))
        {
            poster.AddNoise(NoiseType.Gaussian);
            using var code = new MagickImage(QrCodes.Png("Mo Converter", 6));
            poster.Composite(code, 900, 500, CompositeOperator.Over);
            poster.Quality = 80;
            poster.Write(path, MagickFormat.Jpeg);
        }
        Assert.Equal(["Mo Converter"], QrCodes.Read(path));
    }

    // ---------- collage and editor ----------

    [Fact]
    public async Task Three_pictures_become_a_two_by_two_collage()
    {
        var output = Assert.Single(await Run("image.collage", null, fixture.Plain, fixture.Qr, fixture.GermanText));
        using var image = new MagickImage(output);
        Assert.Equal((1672u, 1672u), (image.Width, image.Height));
    }

    private async Task<MagickImage> Edit(ImageEdit edit) =>
        new(Assert.Single(await Run("image.edit", new Dictionary<string, object?> { ["edit"] = edit }, fixture.Plain)));

    [Fact]
    public async Task Crop_then_turn()
    {
        using var image = await Edit(new ImageEdit { Crop = new CropRect(10, 10, 100, 50), Rotation = 90 });
        Assert.Equal((50u, 100u), (image.Width, image.Height));
    }

    [Fact]
    public async Task Mirror_moves_the_red_edge_to_the_right()
    {
        using var image = await Edit(new ImageEdit { FlipHorizontal = true });
        var right = image.GetPixels().GetPixel(195, 50).ToColor()!;
        var left = image.GetPixels().GetPixel(5, 50).ToColor()!;
        Assert.True(right.R > 200 && right.B < 60, $"right {right}");
        Assert.True(left.B > 100, $"left {left}");
    }

    [Fact]
    public async Task Hidden_areas_are_blacked_out_or_pixelated()
    {
        using (var black = await Edit(new ImageEdit { Redactions = [new CropRect(100, 20, 60, 40)], RedactionStyle = "black" }))
        {
            var pixel = black.GetPixels().GetPixel(130, 40).ToColor()!;
            Assert.True(pixel.R < 10 && pixel.G < 10 && pixel.B < 10);
            Assert.True(black.GetPixels().GetPixel(180, 80).ToColor()!.B > 100); // outside stays as it was
        }

        // Pixelated text cannot be read any more.
        var output = Assert.Single(await Run("image.edit",
            new Dictionary<string, object?> { ["edit"] = new ImageEdit { Redactions = [new CropRect(0, 0, 1400, 360)] } }, fixture.GermanText));
        var error = await Assert.ThrowsAsync<OperationException>(() => Run("image.ocr", null, output));
        Assert.Equal("error.ocr-nothing", error.Key);
    }

    [Fact]
    public async Task Brightness_makes_the_picture_lighter()
    {
        using var original = new MagickImage(fixture.Plain);
        using var lighter = await Edit(new ImageEdit { Brightness = 40 });
        Assert.True(lighter.Statistics().Composite().Mean > original.Statistics().Composite().Mean);
    }

    [Fact]
    public async Task Editing_without_the_editor_explains_itself()
    {
        var error = await Assert.ThrowsAsync<OperationException>(() => Run("image.edit", null, fixture.Plain));
        Assert.Equal("error.edit-needs-editor", error.Key);
        Assert.Equal("image.editor", Registry.Find("image.crop")!.EditorId);
        Assert.Equal("image.editor", Registry.Find("image.edit")!.EditorId);
    }
}

public sealed class PdfPagesTests(PdfFixture fixture) : IClassFixture<PdfFixture>
{
    [Fact]
    public async Task Pages_are_reordered_turned_duplicated_and_dropped()
    {
        var operation = OperationRegistry.CreateDefault().Find("pdf.organize")!;
        Assert.Equal("pdf.pages", operation.EditorId);
        var options = new Dictionary<string, object?> { ["pages"] = new List<PdfPageChoice> { new(2, 90), new(0), new(0, 180) } };
        var result = await operation.RunAsync(new OperationContext(new InputSet([fixture.ThreePages]), new OutputPlanner(fixture.NewOutputFolder()), Options: options), CancellationToken.None);

        var output = Assert.Single(result.Outputs);
        Assert.Equal("three-arranged.pdf", Path.GetFileName(output));
        using var document = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        Assert.Equal(3, document.PageCount);
        Assert.Equal([90, 0, 180], document.Pages.Cast<PdfSharp.Pdf.PdfPage>().Select(p => p.Rotate));
        using var pig = UglyToad.PdfPig.PdfDocument.Open(output);
        Assert.Contains("Page 3", pig.GetPage(1).Text);
        Assert.Contains("Page 1", pig.GetPage(2).Text);
        Assert.Contains("Page 1", pig.GetPage(3).Text);
    }

    [Fact]
    public async Task Arranging_without_the_editor_explains_itself()
    {
        var operation = OperationRegistry.CreateDefault().Find("pdf.organize")!;
        var error = await Assert.ThrowsAsync<OperationException>(() =>
            operation.RunAsync(new OperationContext(new InputSet([fixture.ThreePages]), new OutputPlanner(fixture.NewOutputFolder())), CancellationToken.None));
        Assert.Equal("error.edit-needs-editor", error.Key);
    }
}

public sealed class VideoTrimTests(MediaFixture fixture) : IClassFixture<MediaFixture>
{
    [Fact]
    public async Task Trim_keeps_exactly_the_chosen_part()
    {
        var operation = OperationRegistry.CreateDefault().Find("video.trim")!;
        Assert.Equal("video.trim", operation.EditorId);
        var options = new Dictionary<string, object?> { ["start"] = 0.5, ["end"] = 2.0 };
        var result = await operation.RunAsync(new OperationContext(new InputSet([fixture.Clip]), new OutputPlanner(fixture.NewOutputFolder()), Options: options), CancellationToken.None);
        var info = Ffmpeg.Probe(Assert.Single(result.Outputs));
        Assert.InRange(info.Duration, 1.4, 1.65);
        Assert.True(info.HasAudio);
    }

    [Fact]
    public async Task Trim_needs_a_range()
    {
        var operation = OperationRegistry.CreateDefault().Find("video.trim")!;
        var error = await Assert.ThrowsAsync<OperationException>(() =>
            operation.RunAsync(new OperationContext(new InputSet([fixture.Clip]), new OutputPlanner(fixture.NewOutputFolder())), CancellationToken.None));
        Assert.Equal("error.edit-needs-editor", error.Key);
    }
}
