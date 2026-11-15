using ImageMagick;

namespace Mo.Engine.Images;

/// <summary>Base for operations that handle images one file at a time.</summary>
internal abstract class ImageOperation : IOperation
{
    public abstract string Id { get; }
    public abstract WheelKind Wheel { get; }
    public string TitleKey => "op." + Id;
    public virtual string? Badge => null;
    public abstract string Glyph { get; }
    public abstract int Order { get; }
    public virtual string? EditorId => null;

    public virtual bool Accepts(InputSet input) => input.AllAre(FileCategory.Image);

    /// <summary>Handles one file and returns the path written.</summary>
    protected abstract string ProcessFile(OperationContext context, string source, CancellationToken cancellation);

    public virtual Task<OperationResult> RunAsync(OperationContext context, CancellationToken cancellation) =>
        Task.Run(() =>
        {
            var outputs = new List<string>();
            var paths = context.Input.Paths;
            for (var i = 0; i < paths.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                context.Report((double)i / paths.Count, Path.GetFileName(paths[i]));
                outputs.Add(ProcessFile(context, paths[i], cancellation));
            }
            context.Report(1);
            return OperationResult.Of([.. outputs]);
        }, cancellation);
}

/// <summary>Convert wheel: one instance per target format.</summary>
internal sealed class ConvertImageOperation(
    string extension, string badge, string glyph, int order, params string[] aliases) : ImageOperation
{
    public override string Id => "image.to-" + extension;
    public override WheelKind Wheel => WheelKind.Convert;
    public override string? Badge => badge;
    public override string Glyph => glyph;
    public override int Order => order;

    private string[] SameFormat => [extension, .. aliases];

    public override bool Accepts(InputSet input) => base.Accepts(input) && !input.AllHaveExtension(SameFormat);

    protected override string ProcessFile(OperationContext context, string source, CancellationToken cancellation)
    {
        using var frames = ImageSupport.Load(source);
        var target = context.Output.PathFor(source, extension);
        try
        {
            Encode(frames, extension);
            cancellation.ThrowIfCancellationRequested();
            ImageSupport.Write(frames, target, extension);
            return target;
        }
        catch (MagickException)
        {
            ImageSupport.TryDelete(target);
            throw ImageSupport.WriteFailed(source);
        }
        catch
        {
            ImageSupport.TryDelete(target);
            throw;
        }
    }

    private static void Encode(MagickImageCollection frames, string extension)
    {
        // GIF, WEBP and TIFF keep every frame (animation or pages); every other target takes the first frame.
        // ICO is multi-size, not multi-frame: PrepareIcon builds its sizes from the first frame.
        if (!ImageSupport.IsMultiFrame(extension) || extension == "ico")
            while (frames.Count > 1) frames.RemoveAt(frames.Count - 1);

        foreach (var frame in frames)
        {
            switch (extension)
            {
                case "jpg":
                    ImageSupport.FlattenOnWhite(frame);
                    frame.Quality = 90;
                    break;
                case "webp":
                    frame.Quality = 85;
                    frame.Settings.SetDefine(MagickFormat.WebP, "method", "4");
                    break;
                case "avif":
                    frame.Quality = 60;
                    frame.Settings.SetDefine(MagickFormat.Heic, "speed", "6");
                    break;
                case "tiff":
                    frame.Settings.Compression = CompressionMethod.LZW;
                    break;
                case "bmp":
                    ImageSupport.FlattenOnWhite(frame);
                    ImageSupport.ToSrgbIfProfiled(frame);
                    break;
                case "gif":
                    ImageSupport.ToSrgbIfProfiled(frame);
                    break;
            }
        }

        if (extension == "ico") PrepareIcon(frames);
    }

    /// <summary>Square-fits the first frame to at most 256 px and stores the usual icon sizes.</summary>
    private static void PrepareIcon(MagickImageCollection frames)
    {
        var first = frames[0];
        ImageSupport.ToSrgbIfProfiled(first);
        var side = (uint)Math.Min(256, Math.Max(first.Width, first.Height));
        first.BackgroundColor = MagickColors.Transparent;
        if (first.Width > 256 || first.Height > 256) first.Resize(256, 256);
        first.Extent(side, side, Gravity.Center);

        int[] sizes = [16, 24, 32, 48, 64, 128, 256];
        var wanted = sizes.Where(s => s <= side).ToList();
        // A small picture (say 60 px) keeps its own size as the sharpest entry instead of being shrunk to 48.
        if (!wanted.Contains((int)side)) wanted.Add((int)side);

        var copies = new List<MagickImage>();
        foreach (var size in wanted)
        {
            var copy = (MagickImage)first.Clone();
            if (copy.Width != (uint)size) copy.Resize((uint)size, (uint)size);
            copies.Add(copy);
        }

        frames.Clear(); // disposes the original frame; the copies are independent
        foreach (var copy in copies) frames.Add(copy);
    }
}

/// <summary>All dragged images become one PDF, one image per page in the order given.</summary>
internal sealed class ImagesToPdfOperation : ImageOperation
{
    public override string Id => "image.to-pdf";
    public override WheelKind Wheel => WheelKind.Convert;
    public override string? Badge => "PDF";
    public override string Glyph => "\uEA90";
    public override int Order => 90;

    protected override string ProcessFile(OperationContext context, string source, CancellationToken cancellation) =>
        throw new NotSupportedException();

    public override Task<OperationResult> RunAsync(OperationContext context, CancellationToken cancellation) =>
        Task.Run(() =>
        {
            var paths = context.Input.Paths;
            var target = context.Output.PathFor(paths[0], "pdf", paths.Count > 1 ? "combined" : null);
            using var pages = new MagickImageCollection();
            try
            {
                for (var i = 0; i < paths.Count; i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    context.Report(0.9 * i / paths.Count, Path.GetFileName(paths[i]));
                    using var frames = ImageSupport.Load(paths[i]);
                    var image = (MagickImage)frames[0].Clone();
                    ImageSupport.FlattenOnWhite(image);
                    if (image.Density.X < 1) image.Density = new Density(96);
                    var lossless = FileTypes.ExtensionOf(paths[i]) is "png" or "bmp" or "gif" or "ico";
                    image.Settings.Compression = lossless ? CompressionMethod.Zip : CompressionMethod.JPEG;
                    // For Zip pages Quality is the zlib level times ten, not a picture quality.
                    image.Quality = lossless ? ImageSupport.ZipQuality(image) : 90;
                    pages.Add(image);
                }

                cancellation.ThrowIfCancellationRequested();
                context.Report(0.95, Path.GetFileName(target));
                pages.Write(target, MagickFormat.Pdf);
                context.Report(1);
                return OperationResult.Of(target);
            }
            catch (MagickException)
            {
                ImageSupport.TryDelete(target);
                throw ImageSupport.WriteFailed(paths[0]);
            }
            catch
            {
                ImageSupport.TryDelete(target);
                throw;
            }
        }, cancellation);
}

/// <summary>Re-encodes in the same format at a smaller size; never returns a bigger file.</summary>
internal sealed class CompressImageOperation : ImageOperation
{
    public override string Id => "image.compress";
    public override WheelKind Wheel => WheelKind.Tools;
    public override string Glyph => "\uE9A6";
    public override int Order => 10;

    public override bool Accepts(InputSet input) => ImageSupport.AllRewritable(input);

    protected override string ProcessFile(OperationContext context, string source, CancellationToken cancellation)
    {
        var level = context.Option("level", CompressionLevel.Balanced);
        var extension = FileTypes.ExtensionOf(source);
        var target = context.Output.PathFor(source, extension, "compressed");
        var original = new FileInfo(source).Length;

        // BMP and ICO have no useful compression: keep the original.
        if (extension is "bmp" or "ico")
        {
            File.Copy(source, target);
            return target;
        }

        try
        {
            using var frames = ImageSupport.Load(source);
            Tune(frames, extension, level);
            cancellation.ThrowIfCancellationRequested();
            var bytes = ImageSupport.ToBytes(frames, extension);
            if (bytes.LongLength >= original) File.Copy(source, target);
            else File.WriteAllBytes(target, bytes);
            return target;
        }
        catch (MagickException)
        {
            ImageSupport.TryDelete(target);
            throw ImageSupport.WriteFailed(source);
        }
        catch
        {
            ImageSupport.TryDelete(target);
            throw;
        }
    }

    private static void Tune(MagickImageCollection frames, string extension, CompressionLevel level)
    {
        int Pick(int light, int balanced, int strong) =>
            level switch { CompressionLevel.Light => light, CompressionLevel.Strong => strong, _ => balanced };

        foreach (var frame in frames)
        {
            switch (extension)
            {
                case "jpg":
                case "jpeg":
                    frame.Quality = (uint)Pick(90, 80, 65);
                    frame.Settings.Interlace = Interlace.Plane;
                    frame.Settings.SetDefine(MagickFormat.Jpeg, "optimize-coding", true);
                    if (level != CompressionLevel.Light) frame.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", "4:2:0");
                    break;
                case "webp":
                    frame.Quality = (uint)Pick(90, 80, 65);
                    frame.Settings.SetDefine(MagickFormat.WebP, "method", Pick(4, 5, 6).ToString());
                    break;
                case "avif":
                    frame.Quality = (uint)Pick(70, 55, 40);
                    frame.Settings.SetDefine(MagickFormat.Heic, "speed", "6");
                    break;
                case "png":
                    // Level 9 with the "filtered" strategy took 16-20 s for a 9-12 MP photo; 6 takes about 2 s
                    // on a real photo, 5 about 1.5 s. Strong keeps 9: after reducing to 256 colours there is far
                    // less data, so it stays quick. The tool never returns a file bigger than the original.
                    frame.Settings.SetDefine(MagickFormat.Png, "compression-level", Pick(5, 6, 9).ToString());
                    frame.Settings.SetDefine(MagickFormat.Png, "compression-filter", "5");
                    if (level == CompressionLevel.Strong)
                        frame.Quantize(new QuantizeSettings { Colors = 256, DitherMethod = DitherMethod.FloydSteinberg });
                    break;
                case "tif":
                case "tiff":
                    frame.Settings.Compression = level == CompressionLevel.Light ? CompressionMethod.LZW : CompressionMethod.Zip;
                    break;
                case "gif":
                    if (level == CompressionLevel.Strong)
                        frame.Quantize(new QuantizeSettings { Colors = 128, DitherMethod = DitherMethod.FloydSteinberg });
                    break;
            }
        }

        if (extension == "gif" && frames.Count > 1)
        {
            frames.Optimize();
            frames.OptimizeTransparency();
        }
    }
}

/// <summary>Turns images clockwise by a multiple of any angle (default 90 degrees).</summary>
internal sealed class RotateImageOperation : ImageOperation
{
    public override string Id => "image.rotate";
    public override WheelKind Wheel => WheelKind.Tools;
    public override string Glyph => "\uE7AD";
    public override int Order => 20;

    public override bool Accepts(InputSet input) => ImageSupport.AllRewritable(input);

    protected override string ProcessFile(OperationContext context, string source, CancellationToken cancellation)
    {
        var degrees = context.Option("degrees", 90);
        var extension = FileTypes.ExtensionOf(source);
        var target = context.Output.PathFor(source, extension, "rotated");
        try
        {
            using var frames = ImageSupport.Load(source);
            foreach (var frame in frames)
            {
                if (extension is "jpg" or "jpeg") frame.Quality = 95;
                if (degrees % 360 != 0)
                {
                    if (degrees % 90 != 0) frame.BackgroundColor = MagickColors.Transparent;
                    frame.Rotate(((degrees % 360) + 360) % 360);
                }
                frame.ResetPage();
            }
            cancellation.ThrowIfCancellationRequested();
            ImageSupport.Write(frames, target, extension);
            return target;
        }
        catch (MagickException)
        {
            ImageSupport.TryDelete(target);
            throw ImageSupport.WriteFailed(source);
        }
        catch
        {
            ImageSupport.TryDelete(target);
            throw;
        }
    }
}

/// <summary>Crops to the rectangle in option "rect", or to a centred square when none is given.</summary>
internal sealed class CropImageOperation : ImageOperation
{
    public override string Id => "image.crop";
    public override WheelKind Wheel => WheelKind.Tools;
    public override string Glyph => "\uE7A8";
    public override int Order => 30;
    public override string? EditorId => "image.editor";

    public override bool Accepts(InputSet input) => ImageSupport.AllRewritable(input);

    protected override string ProcessFile(OperationContext context, string source, CancellationToken cancellation)
    {
        var rect = context.Option<CropRect?>("rect", null);
        var extension = FileTypes.ExtensionOf(source);
        var target = context.Output.PathFor(source, extension, "cropped");
        try
        {
            using var frames = ImageSupport.Load(source);
            foreach (var frame in frames)
            {
                var area = Area(frame, rect);
                if (extension is "jpg" or "jpeg") frame.Quality = 95;
                frame.Crop(area);
                frame.ResetPage();
            }
            cancellation.ThrowIfCancellationRequested();
            ImageSupport.Write(frames, target, extension);
            return target;
        }
        catch (MagickException)
        {
            ImageSupport.TryDelete(target);
            throw ImageSupport.WriteFailed(source);
        }
        catch
        {
            ImageSupport.TryDelete(target);
            throw;
        }
    }

    private static MagickGeometry Area(IMagickImage<byte> image, CropRect? rect)
    {
        int width = (int)image.Width, height = (int)image.Height;
        if (rect is null)
        {
            var side = Math.Min(width, height);
            return new MagickGeometry((width - side) / 2, (height - side) / 2, (uint)side, (uint)side);
        }

        var x = Math.Clamp(rect.X, 0, width - 1);
        var y = Math.Clamp(rect.Y, 0, height - 1);
        var w = Math.Clamp(rect.Width, 1, width - x);
        var h = Math.Clamp(rect.Height, 1, height - y);
        return new MagickGeometry(x, y, (uint)w, (uint)h);
    }
}

/// <summary>Removes EXIF, IPTC, XMP and comments; keeps the picture upright and its colour profile.</summary>
internal sealed class StripMetadataOperation : ImageOperation
{
    public override string Id => "image.strip-metadata";
    public override WheelKind Wheel => WheelKind.Tools;
    public override string Glyph => "\uE894";
    public override int Order => 40;

    public override bool Accepts(InputSet input) => ImageSupport.AllRewritable(input);

    protected override string ProcessFile(OperationContext context, string source, CancellationToken cancellation)
    {
        var extension = FileTypes.ExtensionOf(source);
        var target = context.Output.PathFor(source, extension, "clean");
        try
        {
            using var frames = ImageSupport.Load(source); // orientation is applied here
            foreach (var frame in frames)
            {
                var icc = frame.GetColorProfile();
                frame.Strip();
                if (icc is not null) frame.SetProfile(icc);
                frame.Orientation = Orientation.TopLeft;
                if (extension is "jpg" or "jpeg") frame.Quality = 95;
            }
            cancellation.ThrowIfCancellationRequested();
            ImageSupport.Write(frames, target, extension);
            return target;
        }
        catch (MagickException)
        {
            ImageSupport.TryDelete(target);
            throw ImageSupport.WriteFailed(source);
        }
        catch
        {
            ImageSupport.TryDelete(target);
            throw;
        }
    }
}

/// <summary>Registers every image operation.</summary>
public static class ImageModule
{
    public static void Register(OperationRegistry registry)
    {
        registry
            .Add(new ConvertImageOperation("png", "PNG", "\uEB9F", 10))
            .Add(new ConvertImageOperation("jpg", "JPG", "\uE722", 20, "jpeg"))
            .Add(new ConvertImageOperation("webp", "WEBP", "\uE774", 30))
            .Add(new ConvertImageOperation("avif", "AVIF", "\uE9D9", 40))
            .Add(new ConvertImageOperation("tiff", "TIFF", "\uE8A5", 50, "tif"))
            .Add(new ConvertImageOperation("gif", "GIF", "\uF4A9", 60))
            .Add(new ConvertImageOperation("bmp", "BMP", "\uE91B", 70))
            .Add(new ConvertImageOperation("ico", "ICO", "\uE8FC", 80))
            .Add(new ImagesToPdfOperation())
            .Add(new CompressImageOperation())
            .Add(new RotateImageOperation())
            .Add(new CropImageOperation())
            .Add(new StripMetadataOperation())
            .Add(new ImageEditOperation())
            .Add(new ImageOcrOperation())
            .Add(new CollageOperation());
    }
}

