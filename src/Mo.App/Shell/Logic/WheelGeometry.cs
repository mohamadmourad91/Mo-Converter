namespace Mo.App.Shell.Logic;

/// <summary>Where a point on the wheel landed.</summary>
public static class WheelHit
{
    /// <summary>The point is on the centre hub (drop there cancels).</summary>
    public const int Hub = -1;

    /// <summary>The point is outside the ring (drop there cancels).</summary>
    public const int Outside = -2;
}

/// <summary>Angles (degrees) of one petal's four corners. 0° is 12 o'clock, angles grow clockwise on screen.</summary>
public readonly record struct PetalOutline(double OuterStart, double OuterEnd, double InnerStart, double InnerEnd);

/// <summary>
/// Pure geometry of the wheel: a ring of equal petal segments around a round hub.
/// Coordinates are relative to the wheel centre in screen orientation (x right, y down), in DIPs.
/// Petal 0 sits at the top; the rest follow clockwise, or counter-clockwise for right-to-left languages.
/// </summary>
public sealed class WheelGeometry
{
    public WheelGeometry(int petalCount, double outerRadius, double innerRadius, bool clockwise = true, double outsideTolerance = 10)
    {
        if (petalCount < 1) throw new ArgumentOutOfRangeException(nameof(petalCount));
        if (innerRadius <= 0 || outerRadius <= innerRadius) throw new ArgumentOutOfRangeException(nameof(outerRadius));
        PetalCount = petalCount;
        OuterRadius = outerRadius;
        InnerRadius = innerRadius;
        Clockwise = clockwise;
        OutsideTolerance = outsideTolerance;
    }

    public int PetalCount { get; }
    public double OuterRadius { get; }
    public double InnerRadius { get; }
    public bool Clockwise { get; }

    /// <summary>Extra distance beyond the outer edge that still counts as the petal (forgiving aim).</summary>
    public double OutsideTolerance { get; }

    public double SweepDegrees => 360.0 / PetalCount;
    public double MidRadius => (OuterRadius + InnerRadius) / 2;
    public double RingThickness => OuterRadius - InnerRadius;

    /// <summary>Centre angle of petal <paramref name="index"/> in screen degrees (0..360).</summary>
    public double CenterAngle(int index)
    {
        var angle = index * SweepDegrees;
        return Normalize(Clockwise ? angle : -angle);
    }

    /// <summary>Point at an angle and radius, relative to the centre.</summary>
    public static (double X, double Y) PointAt(double angleDegrees, double radius)
    {
        var rad = angleDegrees * Math.PI / 180.0;
        return (radius * Math.Sin(rad), -radius * Math.Cos(rad));
    }

    /// <summary>Corner angles of a petal so that neighbouring petals are separated by a gap of constant width in DIPs.</summary>
    public PetalOutline Outline(int index, double gap, double outerRadius = double.NaN)
    {
        var outer = double.IsNaN(outerRadius) ? OuterRadius : outerRadius;
        var centre = CenterAngle(index);
        var half = SweepDegrees / 2;
        if (PetalCount == 1) return new PetalOutline(centre - 180, centre + 180, centre - 180, centre + 180);
        var outerInset = Degrees(gap / 2 / outer);
        var innerInset = Math.Min(Degrees(gap / 2 / InnerRadius), half * 0.45);
        return new PetalOutline(centre - half + outerInset, centre + half - outerInset,
                                centre - half + innerInset, centre + half - innerInset);
    }

    /// <summary>Centre of the label of a petal.</summary>
    public (double X, double Y) LabelCenter(int index) =>
        PetalCount == 1 ? PointAt(0, MidRadius) : PointAt(CenterAngle(index), MidRadius);

    /// <summary>Widest straight text that fits inside one petal around its label centre.</summary>
    public double LabelMaxWidth()
    {
        var arc = MidRadius * SweepDegrees * Math.PI / 180.0;
        return Math.Min(arc * 0.78, RingThickness * 1.5);
    }

    /// <summary>Maps a point (relative to the centre) to a petal index, <see cref="WheelHit.Hub"/> or <see cref="WheelHit.Outside"/>.</summary>
    public int HitTest(double dx, double dy)
    {
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < InnerRadius) return WheelHit.Hub;
        if (distance > OuterRadius + OutsideTolerance) return WheelHit.Outside;
        if (PetalCount == 1) return 0;

        var angle = Normalize(Math.Atan2(dx, -dy) * 180.0 / Math.PI);
        if (!Clockwise) angle = Normalize(-angle);
        var index = (int)Math.Floor((angle + SweepDegrees / 2) / SweepDegrees);
        return index % PetalCount;
    }

    public static double Normalize(double degrees)
    {
        var d = degrees % 360.0;
        return d < 0 ? d + 360.0 : d;
    }

    private static double Degrees(double radians) => radians * 180.0 / Math.PI;
}

/// <summary>Shrink-to-fit sizing for single-line labels (text never wraps and is never clipped).</summary>
public static class TextFit
{
    /// <summary>
    /// Font size that makes a label measured as <paramref name="measuredWidth"/> at <paramref name="fontSize"/>
    /// fit into <paramref name="maxWidth"/>. Labels that already fit keep their size.
    /// </summary>
    public static double FontSize(double measuredWidth, double fontSize, double maxWidth)
    {
        if (measuredWidth <= maxWidth || measuredWidth <= 0 || maxWidth <= 0) return fontSize;
        // Text width scales linearly with font size; 0.98 leaves room for rounding in glyph advance widths.
        return Math.Max(1, fontSize * maxWidth / measuredWidth * 0.98);
    }
}
