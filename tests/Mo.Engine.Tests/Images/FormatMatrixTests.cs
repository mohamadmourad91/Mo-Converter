using ImageMagick;

namespace Mo.Engine.Tests.Images;

/// <summary>Every readable format through every operation the wheel offers for it; each result is opened again and checked.</summary>
public sealed class FormatMatrixFixture : IDisposable
{
    public static readonly string[] Sources = ["png", "jpg", "jpeg", "webp", "avif", "tif", "tiff", "gif", "bmp", "ico", "heic"];

    public string Folder { get; } = Directory.CreateTempSubdirectory("mo-matrix-").FullName;
    public Dictionary<string, string> Files { get; } = new();
    public Dictionary<string, string> Skipped { get; } = new();

    public FormatMatrixFixture()
    {
        foreach (var extension in Sources)
        {
            var path = Path.Combine(Folder, $"sample-{extension}.{extension}");
            try
            {
                using var image = new MagickImage(MagickColors.SteelBlue, 64, 48);
                using (var patch = new MagickImage(MagickColors.Orange, 32, 22))
                    image.Composite(patch, 8, 8, CompositeOperator.Over);
                image.Write(path, Format(extension));
                Files[extension] = path;
            }
            catch (MagickException e)
            {
                // This Magick.NET build cannot write the format; reading it is still covered elsewhere.
                Skipped[extension] = e.Message;
            }
        }
    }

    private static MagickFormat Format(string extension) => extension switch
    {
        "jpg" or "jpeg" => MagickFormat.Jpeg,
        "tif" or "tiff" => MagickFormat.Tiff,
        "heic" => MagickFormat.Heic,
        "avif" => MagickFormat.Avif,
        "webp" => MagickFormat.WebP,
        "gif" => MagickFormat.Gif,
        "bmp" => MagickFormat.Bmp,
        "ico" => MagickFormat.Ico,
        _ => MagickFormat.Png,
    };

    public void Dispose() => Directory.Delete(Folder, true);
}

public sealed class FormatMatrixTests(FormatMatrixFixture fixture) : IClassFixture<FormatMatrixFixture>
{
    private static readonly OperationRegistry Registry = OperationRegistry.CreateDefault();

    public static TheoryData<string> Sources => new(FormatMatrixFixture.Sources);

    private static readonly Dictionary<string, MagickFormat[]> Expected = new()
    {
        ["image.to-png"] = [MagickFormat.Png],
        ["image.to-jpg"] = [MagickFormat.Jpeg],
        ["image.to-webp"] = [MagickFormat.WebP],
        ["image.to-avif"] = [MagickFormat.Avif, MagickFormat.Heic],
        ["image.to-tiff"] = [MagickFormat.Tiff],
        ["image.to-gif"] = [MagickFormat.Gif],
        ["image.to-bmp"] = [MagickFormat.Bmp, MagickFormat.Bmp3],
        ["image.to-ico"] = [MagickFormat.Ico],
    };

    [Fact]
    public void Heic_samples_can_be_created_or_the_reason_is_known()
    {
        // Documents what this build can encode; every other format must be writable for the matrix to be meaningful.
        Assert.Empty(fixture.Skipped.Keys.Except(["heic"]));
    }

    /// <summary>Every extension the engine calls an image must be readable by this Magick.NET build.</summary>
    [Fact]
    public void Every_image_extension_is_readable()
    {
        var unreadable = new[] { "png", "jpg", "jpeg", "webp", "avif", "tif", "tiff", "gif", "bmp", "ico", "heic", "heif" }
            .Where(e => MagickFormatInfo.Create("x." + e) is not { SupportsReading: true })
            .ToList();
        Assert.Empty(unreadable);
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public async Task Every_conversion_offered_for_a_format_produces_a_valid_file(string source)
    {
        if (!fixture.Files.TryGetValue(source, out var file)) return;
        var input = new InputSet([file]);
        var offered = Registry.Match(WheelKind.Convert, input).Where(o => o.Id != "image.to-pdf").ToList();
        Assert.NotEmpty(offered);

        foreach (var operation in offered)
        {
            var output = Path.Combine(fixture.Folder, $"out-{source}-{operation.Id}");
            var result = await operation.RunAsync(new OperationContext(input, new OutputPlanner(output)), CancellationToken.None);
            var written = Assert.Single(result.Outputs);
            using var image = new MagickImage(written);
            Assert.Contains(image.Format, Expected[operation.Id]);
            if (operation.Id == "image.to-ico")
            {
                Assert.Equal(image.Width, image.Height);
            }
            else
            {
                Assert.Equal(64u, image.Width);
                Assert.Equal(48u, image.Height);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public async Task Every_tool_offered_for_a_format_writes_the_same_format(string source)
    {
        if (!fixture.Files.TryGetValue(source, out var file)) return;
        var input = new InputSet([file]);
        // Tools that open an editor need its choices, and OCR writes text: both are tested on their own.
        foreach (var operation in Registry.Match(WheelKind.Tools, input).Where(o => o.EditorId is null && o.Id != "image.ocr"))
        {
            var output = Path.Combine(fixture.Folder, $"tool-{source}-{operation.Id}");
            var result = await operation.RunAsync(new OperationContext(input, new OutputPlanner(output)), CancellationToken.None);
            var written = Assert.Single(result.Outputs);
            Assert.Equal(source, FileTypes.ExtensionOf(written));
            using var image = new MagickImage(written);
            Assert.True(image.Width > 0 && image.Height > 0, $"{operation.Id} on {source}");
        }
    }

    [Fact]
    public async Task All_formats_together_become_one_pdf_page_each()
    {
        var files = fixture.Files.Values.ToArray();
        var operation = Registry.Find("image.to-pdf")!;
        var result = await operation.RunAsync(
            new OperationContext(new InputSet(files), new OutputPlanner(Path.Combine(fixture.Folder, "pdf"))), CancellationToken.None);

        var pdf = Assert.Single(result.Outputs);
        var text = File.ReadAllText(pdf, System.Text.Encoding.Latin1);
        Assert.StartsWith("%PDF", text);
        Assert.Equal(files.Length, System.Text.RegularExpressions.Regex.Matches(text, @"/Type\s*/Page\b").Count);
    }
}
