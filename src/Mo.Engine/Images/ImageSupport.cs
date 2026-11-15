using ImageMagick;

namespace Mo.Engine.Images;

/// <summary>A pixel rectangle chosen in the crop tool.</summary>
public sealed record CropRect(int X, int Y, int Width, int Height);

/// <summary>Shared helpers: format table, safe loading, flattening, writing.</summary>
internal static class ImageSupport
{
    private static readonly Dictionary<string, MagickFormat> Writable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["png"] = MagickFormat.Png,
        ["jpg"] = MagickFormat.Jpeg,
        ["jpeg"] = MagickFormat.Jpeg,
        ["webp"] = MagickFormat.WebP,
        ["avif"] = MagickFormat.Avif,
        ["tif"] = MagickFormat.Tiff,
        ["tiff"] = MagickFormat.Tiff,
        ["gif"] = MagickFormat.Gif,
        ["bmp"] = MagickFormat.Bmp,
        ["ico"] = MagickFormat.Ico,
    };

    /// <summary>Formats that can hold several frames or sizes in one file.</summary>
    private static readonly HashSet<string> MultiFrame = new(StringComparer.OrdinalIgnoreCase) { "gif", "tif", "tiff", "ico", "webp" };

    public static bool CanWrite(string extension) => Writable.ContainsKey(extension);
    public static MagickFormat FormatOf(string extension) => Writable[extension];
    public static bool IsMultiFrame(string extension) => MultiFrame.Contains(extension);

    /// <summary>True when every file is an image this engine can read and write back in its own format.</summary>
    public static bool AllRewritable(InputSet input) =>
        input.AllAre(FileCategory.Image) && input.Extensions.All(CanWrite);

    /// <summary>Opens an image, applies EXIF orientation to every frame and keeps animations whole.</summary>
    public static MagickImageCollection Load(string path)
    {
        MagickImageCollection? frames = null;
        try
        {
            frames = new MagickImageCollection(path);
            if (frames.Count == 0) throw new OperationException("error.image-unreadable", Path.GetFileName(path));
            if (frames.Count > 1 && FileTypes.ExtensionOf(path) is "gif" or "webp") frames.Coalesce();
            foreach (var frame in frames) frame.AutoOrient();
            return frames;
        }
        catch (MagickException)
        {
            frames?.Dispose();
            throw new OperationException("error.image-unreadable", Path.GetFileName(path));
        }
        catch (Exception e) when (e is not OperationException and not OperationCanceledException)
        {
            frames?.Dispose();
            throw new OperationException("error.image-unreadable", Path.GetFileName(path));
        }
    }

    /// <summary>Replaces transparency with white and drops the alpha channel.</summary>
    public static void FlattenOnWhite(IMagickImage<byte> image)
    {
        if (!image.HasAlpha) return;
        image.ColorAlpha(MagickColors.White);
        image.Alpha(AlphaAction.Off);
    }

    /// <summary>Formats without ICC support get plain sRGB pixels so colours stay right.</summary>
    public static void ToSrgbIfProfiled(IMagickImage<byte> image)
    {
        try
        {
            if (image.GetColorProfile() is not null) image.TransformColorSpace(ColorProfiles.SRGB);
        }
        catch (MagickException) { }
    }

    /// <summary>Pictures with at least this many pixels get the faster zlib level.</summary>
    internal const long LargePicture = 4_000_000;

    /// <summary>
    /// Sets the PNG zlib level unless the caller already chose one (the compress tool does).
    /// Without this the encoder derives the level from the picture's Quality, which is 75 or what the
    /// source file had (90 for most JPGs): level 7 to 9. Measured on a 3840x2400 photo: level 9 took 16 s,
    /// 7 took 3.1 s, 6 took 2.1 s, 4 took 0.9 s at a 14 % bigger file than level 9; on a 1920x1080
    /// screenshot level 6 took 0.16 s at 12 % bigger. So: 6 for normal pictures, 4 for large ones.
    /// </summary>
    public static void PreparePng(IMagickImage<byte> image)
    {
        if (image.Settings.GetDefine(MagickFormat.Png, "compression-level") is not null) return;
        image.Settings.SetDefine(MagickFormat.Png, "compression-level", IsLarge(image) ? "4" : "6");
        image.Settings.SetDefine(MagickFormat.Png, "compression-filter", "5");
    }

    /// <summary>Lowest zlib level, for PNGs that only live in memory (previews, text recognition input).</summary>
    public static void FastPng(IMagickImage<byte> image)
    {
        image.Settings.SetDefine(MagickFormat.Png, "compression-level", "1");
        image.Settings.SetDefine(MagickFormat.Png, "compression-filter", "5");
    }

    /// <summary>
    /// Quality for lossless (Zip) pages in a PDF: the PDF writer takes its zlib level from Quality / 10,
    /// so the usual 90 meant level 9. Same levels as <see cref="PreparePng"/>.
    /// </summary>
    public static uint ZipQuality(IMagickImage<byte> image) => IsLarge(image) ? 40u : 60u;

    private static bool IsLarge(IMagickImage<byte> image) => (long)image.Width * image.Height >= LargePicture;

    /// <summary>Writes the frames in the given format; single-image formats get the first frame only.</summary>
    public static void Write(MagickImageCollection frames, string path, string extension)
    {
        var format = Prepare(frames, extension);
        if (IsMultiFrame(extension) && frames.Count > 1) frames.Write(path, format);
        else frames[0].Write(path, format);
    }

    public static byte[] ToBytes(MagickImageCollection frames, string extension)
    {
        var format = Prepare(frames, extension);
        return IsMultiFrame(extension) && frames.Count > 1 ? frames.ToByteArray(format) : frames[0].ToByteArray(format);
    }

    private static MagickFormat Prepare(MagickImageCollection frames, string extension)
    {
        var format = FormatOf(extension);
        if (format == MagickFormat.Png) PreparePng(frames[0]);
        return format;
    }

    public static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }

    public static OperationException WriteFailed(string source) => new("error.image-write-failed", Path.GetFileName(source));
}

