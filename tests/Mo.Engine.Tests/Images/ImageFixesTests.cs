using ImageMagick;

namespace Mo.Engine.Tests.Images;

/// <summary>Regression tests for bugs found in the review of 2026-11-14.</summary>
public sealed class ImageFixesTests(ImageFixture fixture) : IClassFixture<ImageFixture>
{
    private static readonly OperationRegistry Registry = OperationRegistry.CreateDefault();

    private async Task<string> Run(string id, string file, IReadOnlyDictionary<string, object?>? options = null)
    {
        var result = await Registry.Find(id)!.RunAsync(
            new OperationContext(new InputSet([file]), new OutputPlanner(fixture.NewOutputFolder()), Options: options), CancellationToken.None);
        return Assert.Single(result.Outputs);
    }

    [Theory]
    [InlineData("image.to-tiff", 3)]  // was cut to one page: the check looked for "tif" while the target is "tiff"
    [InlineData("image.to-webp", 3)]
    [InlineData("image.to-png", 1)]
    [InlineData("image.to-ico", 1)]  // the icon is built from the first frame only
    public async Task Animated_gif_keeps_its_frames_only_where_the_format_can_hold_them(string id, int frames)
    {
        var output = await Run(id, fixture.AnimatedGif);
        using var images = new MagickImageCollection(output);
        if (id == "image.to-ico")
            Assert.All(images, i => Assert.Equal(i.Width, i.Height)); // sizes of one picture, not animation frames
        else
            Assert.Equal(frames, images.Count);
    }

    [Fact]
    public async Task Small_picture_keeps_its_own_size_in_the_icon()
    {
        var output = await Run("image.to-ico", fixture.OrientedJpeg); // 60x40 shown upright as 40x60
        using var sizes = new MagickImageCollection(output);
        Assert.Equal(60u, sizes.Max(i => i.Width));
        Assert.Contains(sizes, i => i.Width == 16);
    }

    [Fact]
    public async Task Compression_level_from_the_settings_is_used()
    {
        var light = new FileInfo(await Run("image.compress", fixture.BigJpeg,
            new Dictionary<string, object?> { ["level"] = CompressionLevel.Light })).Length;
        var strong = new FileInfo(await Run("image.compress", fixture.BigJpeg,
            new Dictionary<string, object?> { ["level"] = CompressionLevel.Strong })).Length;
        Assert.True(strong < light, $"strong {strong} B should be smaller than light {light} B");
    }

    [Fact]
    public void Settings_from_a_folder_path_fall_back_to_defaults()
    {
        var settings = AppSettings.Load(fixture.Folder);
        Assert.Equal(ModifierKey.Shift, settings.ConvertKey);
    }
}
