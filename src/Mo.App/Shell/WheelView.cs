using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Mo.App.Shell.Logic;
using Mo.Engine;

namespace Mo.App.Shell;

/// <summary>What one petal shows. <see cref="Operation"/> is null for the "more" petal.</summary>
public sealed record WheelPetal(IOperation? Operation, string Title, string Label, string? Badge, string Glyph, bool IsMore = false);

/// <summary>
/// Draws the wheel: a soft round plate, a ring of petal segments and a centre hub. Rendering is done in
/// <see cref="OnRender"/> so 1–12 petals of any length stay crisp and every label is shrunk to fit on one line.
/// All geometry and hit-testing comes from <see cref="WheelGeometry"/>.
/// </summary>
public sealed class WheelView : FrameworkElement
{
    public const double OuterRadius = 146;
    public const double InnerRadius = 58;
    public const double Gap = 4;
    public const double HoverPop = 7;
    public const double PlatePadding = 6;

    private static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI");
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private WheelGeometry _geometry = new(1, OuterRadius, InnerRadius);

    public WheelView()
    {
        Width = Height = 2 * (OuterRadius + HoverPop + PlatePadding + 4);
        SnapsToDevicePixels = true;
        SetResourceReference(PlateBrushProperty, "Mo.Wheel.Plate");
        SetResourceReference(PlateBorderBrushProperty, "Mo.Wheel.PlateBorder");
        SetResourceReference(PetalBrushProperty, "Mo.Wheel.Petal");
        SetResourceReference(TextBrushProperty, "Mo.Wheel.Text");
        SetResourceReference(SubtleTextBrushProperty, "Mo.Wheel.SubtleText");
        SetResourceReference(AccentBrushProperty, "Mo.Accent");
        SetResourceReference(AccentTextBrushProperty, "Mo.AccentText");
        SetResourceReference(HubBrushProperty, "Mo.Wheel.Hub");
    }

    #region Dependency properties

    public static readonly DependencyProperty PetalsProperty = Register<IReadOnlyList<WheelPetal>>(nameof(Petals), Array.Empty<WheelPetal>());
    public static readonly DependencyProperty HighlightIndexProperty = Register(nameof(HighlightIndex), WheelHit.Outside);
    public static readonly DependencyProperty CaptionProperty = Register(nameof(Caption), "");
    public static readonly DependencyProperty HubTextProperty = Register(nameof(HubText), "");
    public static readonly DependencyProperty ClockwiseProperty = Register(nameof(Clockwise), true);
    public static readonly DependencyProperty RightToLeftTextProperty = Register(nameof(RightToLeftText), false);
    public static readonly DependencyProperty PlateBrushProperty = Register<Brush?>(nameof(PlateBrush), null);
    public static readonly DependencyProperty PlateBorderBrushProperty = Register<Brush?>(nameof(PlateBorderBrush), null);
    public static readonly DependencyProperty PetalBrushProperty = Register<Brush?>(nameof(PetalBrush), null);
    public static readonly DependencyProperty TextBrushProperty = Register<Brush?>(nameof(TextBrush), null);
    public static readonly DependencyProperty SubtleTextBrushProperty = Register<Brush?>(nameof(SubtleTextBrush), null);
    public static readonly DependencyProperty AccentBrushProperty = Register<Brush?>(nameof(AccentBrush), null);
    public static readonly DependencyProperty AccentTextBrushProperty = Register<Brush?>(nameof(AccentTextBrush), null);
    public static readonly DependencyProperty HubBrushProperty = Register<Brush?>(nameof(HubBrush), null);

    public IReadOnlyList<WheelPetal> Petals { get => (IReadOnlyList<WheelPetal>)GetValue(PetalsProperty); set => SetValue(PetalsProperty, value); }

    /// <summary>Hovered petal index, or <see cref="WheelHit.Hub"/> / <see cref="WheelHit.Outside"/>.</summary>
    public int HighlightIndex { get => (int)GetValue(HighlightIndexProperty); set => SetValue(HighlightIndexProperty, value); }

    /// <summary>Small accent text at the top of the hub (wheel name).</summary>
    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }

    /// <summary>Main hub text (hovered petal's full title, file count, or "Cancel").</summary>
    public string HubText { get => (string)GetValue(HubTextProperty); set => SetValue(HubTextProperty, value); }

    public bool Clockwise { get => (bool)GetValue(ClockwiseProperty); set => SetValue(ClockwiseProperty, value); }
    public bool RightToLeftText { get => (bool)GetValue(RightToLeftTextProperty); set => SetValue(RightToLeftTextProperty, value); }
    public Brush? PlateBrush { get => (Brush?)GetValue(PlateBrushProperty); set => SetValue(PlateBrushProperty, value); }
    public Brush? PlateBorderBrush { get => (Brush?)GetValue(PlateBorderBrushProperty); set => SetValue(PlateBorderBrushProperty, value); }
    public Brush? PetalBrush { get => (Brush?)GetValue(PetalBrushProperty); set => SetValue(PetalBrushProperty, value); }
    public Brush? TextBrush { get => (Brush?)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public Brush? SubtleTextBrush { get => (Brush?)GetValue(SubtleTextBrushProperty); set => SetValue(SubtleTextBrushProperty, value); }
    public Brush? AccentBrush { get => (Brush?)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public Brush? AccentTextBrush { get => (Brush?)GetValue(AccentTextBrushProperty); set => SetValue(AccentTextBrushProperty, value); }
    public Brush? HubBrush { get => (Brush?)GetValue(HubBrushProperty); set => SetValue(HubBrushProperty, value); }

    private static DependencyProperty Register<T>(string name, T fallback) =>
        DependencyProperty.Register(name, typeof(T), typeof(WheelView),
            new FrameworkPropertyMetadata(fallback, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((WheelView)d).Rebuild()));

    #endregion

    /// <summary>Geometry of the current petals; used by the overlay for hit-testing.</summary>
    public WheelGeometry Geometry => _geometry;

    /// <summary>Petal index under a point given relative to this element.</summary>
    public int HitTestPoint(Point p) => _geometry.HitTest(p.X - ActualWidth / 2, p.Y - ActualHeight / 2);

    private void Rebuild() =>
        _geometry = new WheelGeometry(Math.Max(1, Petals.Count), OuterRadius, InnerRadius, Clockwise);

    protected override void OnRender(DrawingContext dc)
    {
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        var platePen = PlateBorderBrush is null ? null : new Pen(PlateBorderBrush, 1);
        dc.DrawEllipse(PlateBrush, platePen, c, OuterRadius + PlatePadding, OuterRadius + PlatePadding);

        var petals = Petals;
        for (var i = 0; i < petals.Count; i++)
        {
            var hot = i == HighlightIndex;
            var outer = hot ? OuterRadius + HoverPop : OuterRadius;
            dc.DrawGeometry(hot ? AccentBrush : PetalBrush, null, PetalGeometry(i, c, outer));
            DrawPetalContent(dc, petals[i], i, c, hot, dpi);
        }

        var hubHot = HighlightIndex == WheelHit.Hub;
        var hubPen = new Pen(hubHot ? AccentBrush : PlateBorderBrush, hubHot ? 2 : 1);
        dc.DrawEllipse(HubBrush, hubPen, c, InnerRadius - Gap, InnerRadius - Gap);
        DrawHub(dc, c, dpi);
    }

    private Geometry PetalGeometry(int index, Point c, double outer)
    {
        if (_geometry.PetalCount == 1)
        {
            // A single petal is a full ring: outer disc minus the hub hole.
            return new CombinedGeometry(GeometryCombineMode.Exclude,
                new EllipseGeometry(c, outer, outer), new EllipseGeometry(c, InnerRadius, InnerRadius));
        }

        var o = _geometry.Outline(index, Gap, outer);
        Point At(double angle, double r)
        {
            var (x, y) = WheelGeometry.PointAt(angle, r);
            return new Point(c.X + x, c.Y + y);
        }

        var figure = new StreamGeometry();
        using (var ctx = figure.Open())
        {
            ctx.BeginFigure(At(o.OuterStart, outer), true, true);
            ctx.ArcTo(At(o.OuterEnd, outer), new Size(outer, outer), 0, o.OuterEnd - o.OuterStart > 180, SweepDirection.Clockwise, true, true);
            ctx.LineTo(At(o.InnerEnd, InnerRadius), true, true);
            ctx.ArcTo(At(o.InnerStart, InnerRadius), new Size(InnerRadius, InnerRadius), 0, o.InnerEnd - o.InnerStart > 180, SweepDirection.Counterclockwise, true, true);
        }
        figure.Freeze();
        return figure;
    }

    private void DrawPetalContent(DrawingContext dc, WheelPetal petal, int index, Point c, bool hot, double dpi)
    {
        var (lx, ly) = _geometry.LabelCenter(index);
        if (hot)
        {
            // Follow the petal outwards a little when it pops.
            var (px, py) = WheelGeometry.PointAt(_geometry.CenterAngle(index), HoverPop / 2);
            if (_geometry.PetalCount > 1) { lx += px; ly += py; }
        }
        var x = c.X + lx;
        var y = c.Y + ly;
        var maxWidth = _geometry.LabelMaxWidth();
        var brush = hot ? AccentTextBrush : TextBrush;

        if (petal.Badge is { Length: > 0 } badge && !petal.IsMore)
        {
            var size = _geometry.PetalCount <= 6 ? 18 : 15;
            DrawCentered(dc, Text(badge, size, FontWeights.SemiBold, brush, TextFont, maxWidth, dpi), x, y);
            return;
        }

        DrawCentered(dc, Text(petal.Glyph, 20, FontWeights.Normal, brush, IconFont, maxWidth, dpi, ltr: true), x, y - 9);
        DrawCentered(dc, Text(petal.Label, 11, FontWeights.Normal, brush, TextFont, maxWidth, dpi), x, y + 13);
    }

    private void DrawHub(DrawingContext dc, Point c, double dpi)
    {
        var width = 2 * (InnerRadius - Gap - 10);
        var caption = Text(Caption, 10.5, FontWeights.SemiBold, AccentBrush, TextFont, width * 0.86, dpi);
        if (string.IsNullOrEmpty(HubText))
        {
            DrawCentered(dc, caption, c.X, c.Y);
            return;
        }
        DrawCentered(dc, caption, c.X, c.Y - 11);
        DrawCentered(dc, Text(HubText, 13, FontWeights.SemiBold, TextBrush, TextFont, width, dpi), c.X, c.Y + 8);
    }

    private FormattedText Text(string text, double size, FontWeight weight, Brush? brush, FontFamily family,
                               double maxWidth, double dpi, bool ltr = false)
    {
        var flow = RightToLeftText && !ltr ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, flow,
            new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal), size, brush ?? Brushes.Gray, dpi)
        {
            MaxLineCount = 1,
            Trimming = TextTrimming.None,
        };
        var fitted = TextFit.FontSize(ft.WidthIncludingTrailingWhitespace, size, maxWidth);
        if (fitted < size) ft.SetFontSize(fitted);
        return ft;
    }

    /// <summary>
    /// Draws text centred on a point. The ink box is measured instead of assuming where the origin sits,
    /// because right-to-left text is laid out leftwards from its origin.
    /// </summary>
    private static void DrawCentered(DrawingContext dc, FormattedText ft, double x, double y)
    {
        if (ft.Text.Length == 0) return;
        var box = ft.BuildHighlightGeometry(new Point(0, 0))?.Bounds ?? Rect.Empty;
        if (box.IsEmpty) box = new Rect(0, 0, ft.Width, ft.Height);
        dc.DrawText(ft, new Point(x - (box.X + box.Width / 2), y - (box.Y + box.Height / 2)));
    }
}
