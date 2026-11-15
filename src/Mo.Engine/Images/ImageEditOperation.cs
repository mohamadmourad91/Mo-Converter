using ImageMagick;

namespace Mo.Engine.Images;

/// <summary>Everything the image editor can change, applied in this order: blur areas, colours, crop, turn, mirror.</summary>
public sealed record ImageEdit
{
    /// <summary>Areas to hide (faces, number plates, addresses), in pixels of the upright original.</summary>
    public IReadOnlyList<CropRect> Redactions { get; init; } = [];

    /// <summary>"pixelate" (default) or "black".</summary>
    public string RedactionStyle { get; init; } = "pixelate";

    /// <summary>−100 … 100; 0 leaves the picture as it is.</summary>
    public int Brightness { get; init; }
    public int Contrast { get; init; }
    public int Saturation { get; init; }

    /// <summary>Crop area in pixels of the upright original, or null.</summary>
    public CropRect? Crop { get; init; }

    /// <summary>Clockwise: 0, 90, 180 or 270.</summary>
    public int Rotation { get; init; }
    public bool FlipHorizontal { get; init; }
    public bool FlipVertical { get; init; }

    public bool IsEmpty => Redactions.Count == 0 && Brightness == 0 && Contrast == 0 && Saturation == 0
                           && Crop is null && Rotation % 360 == 0 && !FlipHorizontal && !FlipVertical;

    public static void Apply(IMagickImage<byte> image, ImageEdit edit)
    {
        var width = (int)image.Width;
        var height = (int)image.Height;
        foreach (var area in edit.Redactions)
        {
            var x = Math.Clamp(area.X, 0, width - 1);
            var y = Math.Clamp(area.Y, 0, height - 1);
            var w = Math.Clamp(area.Width, 1, width - x);
            var h = Math.Clamp(area.Height, 1, height - y);
            using var part = (MagickImage)image.Clone();
            part.Crop(new MagickGeometry(x, y, (uint)w, (uint)h));
            part.ResetPage();
            if (edit.RedactionStyle == "black")
            {
                part.Colorize(MagickColors.Black, new Percentage(100));
            }
            else
            {
                // Big blocks: about 12 across the area, never smaller than 8 px, so nothing can be read back.
                var block = Math.Max(8, Math.Max(w, h) / 12);
                part.Scale(new MagickGeometry((uint)Math.Max(1, w / block), (uint)Math.Max(1, h / block)) { IgnoreAspectRatio = true });
                part.Scale(new MagickGeometry((uint)w, (uint)h) { IgnoreAspectRatio = true });
            }
            image.Composite(part, x, y, CompositeOperator.Over);
        }

        if (edit.Brightness != 0 || edit.Contrast != 0)
            image.BrightnessContrast(new Percentage(Math.Clamp(edit.Brightness, -100, 100)), new Percentage(Math.Clamp(edit.Contrast, -100, 100)));
        if (edit.Saturation != 0)
            image.Modulate(new Percentage(100), new Percentage(100 + Math.Clamp(edit.Saturation, -100, 100)), new Percentage(100));

        if (edit.Crop is { } crop)
        {
            var x = Math.Clamp(crop.X, 0, width - 1);
            var y = Math.Clamp(crop.Y, 0, height - 1);
            image.Crop(new MagickGeometry(x, y, (uint)Math.Clamp(crop.Width, 1, width - x), (uint)Math.Clamp(crop.Height, 1, height - y)));
            image.ResetPage();
        }

        var turn = ((edit.Rotation % 360) + 360) % 360;
        if (turn != 0) image.Rotate(turn);
        if (edit.FlipHorizontal) image.Flop();
        if (edit.FlipVertical) image.Flip();
        image.ResetPage();
    }
}

/// <summary>
/// Tools wheel: opens the image editor (crop, turn, colours, hide areas). The editor runs this operation with the
/// option "edit" (an <see cref="ImageEdit"/>); the result keeps the picture's own format.
/// </summary>
internal sealed class ImageEditOperation : ImageOperation
{
    public override string Id => "image.edit";
    public override WheelKind Wheel => WheelKind.Tools;
    public override string Glyph => "\uE70F";
    public override int Order => 1;
    public override string? EditorId => "image.editor";

    public override bool Accepts(InputSet input) => input.IsSingle && ImageSupport.AllRewritable(input);

    protected override string ProcessFile(OperationContext context, string source, CancellationToken cancellation)
    {
        var edit = context.Option<ImageEdit?>("edit", null) ?? throw new OperationException("error.edit-needs-editor");
        var extension = FileTypes.ExtensionOf(source);
        var target = context.Output.PathFor(source, extension, "edited");
        try
        {
            using var frames = ImageSupport.Load(source);
            if (frames.Count > 1 && extension is "gif" or "webp")
            {
                foreach (var frame in frames) ImageEdit.Apply(frame, edit);
            }
            else
            {
                while (frames.Count > 1) frames.RemoveAt(frames.Count - 1);
                ImageEdit.Apply(frames[0], edit);
            }
            if (extension is "jpg" or "jpeg") foreach (var frame in frames) frame.Quality = 95;
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
