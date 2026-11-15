using ImageMagick;
using Mo.Engine.Images;

namespace Mo.Engine.Tests.Images;

public sealed class ImageFixture : IDisposable
{
    public string Folder { get; } = Directory.CreateTempSubdirectory("mo-img-").FullName;

    /// <summary>120x80 PNG whose left half is fully transparent.</summary>
    public string TransparentPng { get; }

    /// <summary>60x40 JPEG saved with EXIF orientation 6 (needs a 90 degree turn) and a Software tag.</summary>
    public string OrientedJpeg { get; }

    /// <summary>50x50 GIF with three frames.</summary>
    public string AnimatedGif { get; }

    /// <summary>Large noisy-ish JPEG so recompression has something to win.</summary>
    public string BigJpeg { get; }

    public string Corrupt { get; }

    public ImageFixture()
    {
        TransparentPng = Path.Combine(Folder, "alpha.png");
        using (var image = new MagickImage(MagickColors.Red, 120, 80))
        {
            image.Alpha(AlphaAction.Set);
            using var hole = new MagickImage(MagickColors.Transparent, 60, 80);
            image.Composite(hole, 0, 0, CompositeOperator.Copy);
            image.Write(TransparentPng, MagickFormat.Png);
        }

        OrientedJpeg = Path.Combine(Folder, "oriented.jpg");
        using (var image = new MagickImage(MagickColors.SteelBlue, 60, 40))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Software, "MoTest");
            exif.SetValue(ExifTag.Orientation, (ushort)6);
            image.SetProfile(exif);
            image.Orientation = Orientation.RightTop;
            image.Write(OrientedJpeg, MagickFormat.Jpeg);
        }

        AnimatedGif = Path.Combine(Folder, "anim.gif");
        using (var frames = new MagickImageCollection())
        {
            foreach (var color in new[] { MagickColors.Red, MagickColors.Green, MagickColors.Blue })
            {
                var frame = new MagickImage(color, 50, 50) { AnimationDelay = 10 };
                frames.Add(frame);
            }
            frames.Write(AnimatedGif, MagickFormat.Gif);
        }

        BigJpeg = Path.Combine(Folder, "big.jpg");
        using (var image = new MagickImage(MagickColors.White, 800, 600))
        {
            image.AddNoise(NoiseType.Gaussian);
            image.Quality = 100;
            image.Write(BigJpeg, MagickFormat.Jpeg);
        }

        Corrupt = Path.Combine(Folder, "broken.png");
        File.WriteAllBytes(Corrupt, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
    }

    public string NewOutputFolder() => Directory.CreateDirectory(Path.Combine(Folder, "out-" + Guid.NewGuid().ToString("N")[..6])).FullName;

    public void Dispose()
    {
        try { Directory.Delete(Folder, true); } catch (IOException) { }
    }
}

public class ImageOperationTests(ImageFixture fx) : IClassFixture<ImageFixture>
{
    private static readonly OperationRegistry Registry = OperationRegistry.CreateDefault();

    private async Task<IReadOnlyList<string>> Run(string id, IEnumerable<string> inputs,
        IReadOnlyDictionary<string, object?>? options = null)
    {
        var operation = Registry.Find(id) ?? throw new InvalidOperationException(id);
        var input = new InputSet(inputs);
        Assert.True(operation.Accepts(input), id + " should accept the files");
        var context = new OperationContext(input, new OutputPlanner(fx.NewOutputFolder()), null, options);
        return (await operation.RunAsync(context, CancellationToken.None)).Outputs;
    }

    private static MagickImageInfo Info(string path) => new(path);

    [Theory]
    [InlineData("png", MagickFormat.Png)]
    [InlineData("jpg", MagickFormat.Jpeg)]
    [InlineData("webp", MagickFormat.WebP)]
    [InlineData("avif", MagickFormat.Avif)]
    [InlineData("tiff", MagickFormat.Tiff)]
    [InlineData("gif", MagickFormat.Gif)]
    [InlineData("bmp", MagickFormat.Bmp)]
    public async Task Convert_produces_the_target_format(string target, MagickFormat expected)
    {
        var source = target == "png" ? fx.BigJpeg : fx.TransparentPng; // never convert to the format it already has
        var outputs = await Run("image.to-" + target, [source]);
        var output = Assert.Single(outputs);
        Assert.True(File.Exists(output));
        Assert.EndsWith("." + target, output);
        var info = Info(output);
        Assert.Equal(expected, info.Format);
        Assert.Equal(Info(source).Width, info.Width);
        Assert.Equal(Info(source).Height, info.Height);
    }

    [Fact]
    public async Task Convert_to_ico_is_square_and_at_most_256()
    {
        var output = Assert.Single(await Run("image.to-ico", [fx.TransparentPng]));
        using var frames = new MagickImageCollection(output);
        Assert.True(frames.Count > 1);
        Assert.All(frames, f => Assert.True(f.Width == f.Height && f.Width <= 256));
        Assert.Equal(16u, frames.Min(f => f.Width));
    }

    [Fact]
    public async Task Convert_to_jpg_flattens_transparency_onto_white()
    {
        var output = Assert.Single(await Run("image.to-jpg", [fx.TransparentPng]));
        using var image = new MagickImage(output);
        var pixel = image.GetPixels().GetPixel(10, 10).ToColor()!;
        Assert.True(pixel.R > 230 && pixel.G > 230 && pixel.B > 230, "transparent area should be white");
    }

    [Fact]
    public async Task Convert_honours_exif_orientation()
    {
        var output = Assert.Single(await Run("image.to-png", [fx.OrientedJpeg]));
        var info = Info(output);
        Assert.Equal(40u, info.Width);
        Assert.Equal(60u, info.Height);
    }

    [Fact]
    public async Task Animated_gif_to_png_uses_first_frame()
    {
        var output = Assert.Single(await Run("image.to-png", [fx.AnimatedGif]));
        using var frames = new MagickImageCollection(output);
        Assert.Single(frames);
        var pixel = frames[0].GetPixels().GetPixel(5, 5).ToColor()!;
        Assert.True(pixel.R > 200 && pixel.G < 60, "first frame is red");
    }

    [Fact]
    public async Task To_pdf_makes_one_page_per_image_in_order()
    {
        var output = Assert.Single(await Run("image.to-pdf", [fx.TransparentPng, fx.OrientedJpeg, fx.AnimatedGif]));
        Assert.EndsWith(".pdf", output);
        var text = System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(output));
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(text, @"/Type\s*/Page\b(?!s)").Count);
    }

    [Fact]
    public void Registry_does_not_offer_the_format_the_files_already_have()
    {
        var targets = Registry.Match(WheelKind.Convert, new InputSet(["a.jpg", "b.jpeg"])).Select(o => o.Id).ToList();
        Assert.DoesNotContain("image.to-jpg", targets);
        Assert.Contains("image.to-png", targets);
        Assert.Contains("image.to-pdf", targets);

        var tiff = Registry.Match(WheelKind.Convert, new InputSet(["a.tif"])).Select(o => o.Id).ToList();
        Assert.DoesNotContain("image.to-tiff", tiff);

        var mixed = Registry.Match(WheelKind.Convert, new InputSet(["a.jpg", "b.png"])).Select(o => o.Id).ToList();
        Assert.Contains("image.to-jpg", mixed);
        Assert.Contains("image.to-png", mixed);
    }

    [Fact]
    public void Registry_offers_nothing_for_mixed_image_and_pdf()
    {
        var input = new InputSet(["a.png", "b.pdf"]);
        Assert.Empty(Registry.Match(WheelKind.Convert, input));
        Assert.Empty(Registry.Match(WheelKind.Tools, input));
    }

    [Fact]
    public void Registry_orders_png_and_jpg_first_and_lists_the_tools()
    {
        var convert = Registry.Match(WheelKind.Convert, new InputSet(["a.bmp"])).Select(o => o.Id).ToList();
        Assert.Equal("image.to-png", convert[0]);
        Assert.Equal("image.to-jpg", convert[1]);
        var tools = Registry.Match(WheelKind.Tools, new InputSet(["a.png"])).Select(o => o.Id).ToList();
        Assert.Equal(["image.edit", "image.compress", "image.rotate", "image.crop", "image.strip-metadata", "image.ocr"], tools);
        Assert.Equal("image.editor", Registry.Find("image.crop")!.EditorId);
    }

    [Theory]
    [InlineData(CompressionLevel.Light)]
    [InlineData(CompressionLevel.Balanced)]
    [InlineData(CompressionLevel.Strong)]
    public async Task Compress_never_grows_the_file(CompressionLevel level)
    {
        foreach (var source in new[] { fx.BigJpeg, fx.TransparentPng, fx.AnimatedGif })
        {
            var output = Assert.Single(await Run("image.compress", [source],
                new Dictionary<string, object?> { ["level"] = level }));
            Assert.Contains("-compressed", output);
            Assert.Equal(Path.GetExtension(source), Path.GetExtension(output));
            Assert.True(new FileInfo(output).Length <= new FileInfo(source).Length, source + " " + level);
        }
    }

    [Fact]
    public async Task Compress_makes_a_large_jpeg_smaller()
    {
        var output = Assert.Single(await Run("image.compress", [fx.BigJpeg],
            new Dictionary<string, object?> { ["level"] = CompressionLevel.Strong }));
        Assert.True(new FileInfo(output).Length < new FileInfo(fx.BigJpeg).Length);
        Assert.Equal(MagickFormat.Jpeg, Info(output).Format);
    }

    [Fact]
    public async Task Rotate_swaps_width_and_height()
    {
        var output = Assert.Single(await Run("image.rotate", [fx.TransparentPng]));
        Assert.Contains("-rotated", output);
        var info = Info(output);
        Assert.Equal(80u, info.Width);
        Assert.Equal(120u, info.Height);
        Assert.Equal(MagickFormat.Png, info.Format);
    }

    [Fact]
    public async Task Rotate_180_keeps_dimensions()
    {
        var output = Assert.Single(await Run("image.rotate", [fx.TransparentPng],
            new Dictionary<string, object?> { ["degrees"] = 180 }));
        var info = Info(output);
        Assert.Equal(120u, info.Width);
        Assert.Equal(80u, info.Height);
    }

    [Fact]
    public async Task Crop_defaults_to_a_centred_square()
    {
        var output = Assert.Single(await Run("image.crop", [fx.TransparentPng]));
        Assert.Contains("-cropped", output);
        var info = Info(output);
        Assert.Equal(80u, info.Width);
        Assert.Equal(80u, info.Height);
    }

    [Fact]
    public async Task Crop_uses_the_given_rectangle()
    {
        var output = Assert.Single(await Run("image.crop", [fx.TransparentPng],
            new Dictionary<string, object?> { ["rect"] = new CropRect(10, 5, 30, 20) }));
        var info = Info(output);
        Assert.Equal(30u, info.Width);
        Assert.Equal(20u, info.Height);
    }

    [Fact]
    public async Task Strip_metadata_removes_exif_but_applies_orientation()
    {
        using (var before = new MagickImage(fx.OrientedJpeg))
            Assert.Equal("MoTest", before.GetExifProfile()?.GetValue(ExifTag.Software)?.Value);

        var output = Assert.Single(await Run("image.strip-metadata", [fx.OrientedJpeg]));
        Assert.Contains("-clean", output);
        using var after = new MagickImage(output);
        Assert.Null(after.GetExifProfile()?.GetValue(ExifTag.Software));
        Assert.Equal(40u, after.Width);
        Assert.Equal(60u, after.Height);
    }

    [Fact]
    public async Task Corrupt_file_raises_a_localized_operation_exception()
    {
        foreach (var id in new[] { "image.to-jpg", "image.compress", "image.rotate", "image.strip-metadata", "image.to-pdf" })
        {
            var e = await Assert.ThrowsAsync<OperationException>(() => Run(id, [fx.Corrupt]));
            Assert.Equal("error.image-unreadable", e.Key);
            Assert.Equal("broken.png", e.Args[0]);
        }
    }

    [Fact]
    public async Task Png_writing_uses_a_quick_zlib_level_and_stays_lossless()
    {
        // The encoder used to derive its level from Quality (here 100 from the JPG source): very slow for big pictures.
        var png = Assert.Single(await Run("image.to-png", [fx.BigJpeg]));
        var rotated = Assert.Single(await Run("image.rotate", [png], new Dictionary<string, object?> { ["degrees"] = 360 }));
        using var expected = new MagickImage(fx.BigJpeg);
        foreach (var file in new[] { png, rotated })
        {
            using var written = new MagickImage(file);
            Assert.Equal(0, expected.Compare(written, ErrorMetric.Absolute));
        }

        using var big = new MagickImage(MagickColors.White, 2500, 2000);
        using var small = new MagickImage(MagickColors.White, 100, 100);
        ImageSupport.PreparePng(big);
        ImageSupport.PreparePng(small);
        Assert.Equal("4", big.Settings.GetDefine(MagickFormat.Png, "compression-level"));
        Assert.Equal("6", small.Settings.GetDefine(MagickFormat.Png, "compression-level"));

        // A level the caller chose (the compress tool) is kept.
        small.Settings.SetDefine(MagickFormat.Png, "compression-level", "9");
        ImageSupport.PreparePng(small);
        Assert.Equal("9", small.Settings.GetDefine(MagickFormat.Png, "compression-level"));
    }

    [Theory]
    [InlineData(CompressionLevel.Light)]
    [InlineData(CompressionLevel.Balanced)]
    [InlineData(CompressionLevel.Strong)]
    public async Task Compressing_a_converted_png_never_makes_it_bigger(CompressionLevel level)
    {
        var png = Assert.Single(await Run("image.to-png", [fx.BigJpeg]));
        var compressed = Assert.Single(await Run("image.compress", [png], new Dictionary<string, object?> { ["level"] = level }));
        Assert.True(new FileInfo(compressed).Length <= new FileInfo(png).Length);
    }

    [Fact]
    public async Task Cancellation_stops_the_run()
    {
        var operation = Registry.Find("image.to-png")!;
        var context = new OperationContext(new InputSet([fx.OrientedJpeg]), new OutputPlanner(fx.NewOutputFolder()));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.RunAsync(context, cts.Token));
    }

    [Fact]
    public async Task Progress_is_reported_per_file()
    {
        var reports = new List<OperationProgress>();
        var operation = Registry.Find("image.to-png")!;
        var context = new OperationContext(new InputSet([fx.OrientedJpeg, fx.AnimatedGif]),
            new OutputPlanner(fx.NewOutputFolder()), new SyncProgress(reports.Add));
        var result = await operation.RunAsync(context, CancellationToken.None);
        Assert.Equal(2, result.Outputs.Count);
        Assert.Contains(reports, r => r.Detail == "oriented.jpg");
        Assert.Equal(1.0, reports[^1].Fraction);
    }

    [Fact]
    public void Every_image_title_is_localized_in_all_languages()
    {
        var loc = new Localizer("en");
        foreach (var operation in Registry.All)
            foreach (var language in Localizer.Languages)
                Assert.True(loc.Has(operation.TitleKey, language), operation.TitleKey + " / " + language);
        foreach (var language in Localizer.Languages)
            Assert.True(loc.Has("error.image-unreadable", language));
    }

    private sealed class SyncProgress(Action<OperationProgress> onReport) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => onReport(value);
    }
}


