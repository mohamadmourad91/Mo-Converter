using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mo.App.Shell;

/// <summary>
/// Draws the app icon in code: an indigo rounded square holding an open ring split into three arcs
/// (one per wheel) around a small dot.
/// </summary>
internal static class IconFactory
{
    private const int SM_CXSMICON = 49;

    /// <summary>
    /// Tray icon at the small-icon size of the current DPI (16 px at 100 %, 24 px at 150 %…).
    /// The handle is owned by the returned Icon's caller for the app's lifetime (one icon per run).
    /// </summary>
    public static System.Drawing.Icon CreateTrayIcon()
    {
        var size = Math.Max(16, Native.GetSystemMetrics(SM_CXSMICON));
        using var png = new MemoryStream(RenderPng(size));
        using var bitmap = new System.Drawing.Bitmap(png);
        return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
    }
    private static byte[] RenderPng(int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) Draw(dc, size);
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void Draw(DrawingContext dc, double size)
    {
        var s = size / 32.0; // design grid is 32×32
        var background = new LinearGradientBrush(Color.FromRgb(0x5B, 0x55, 0xF0), Color.FromRgb(0x3A, 0x34, 0xC4), 45);
        var inset = size <= 20 ? 0.5 : 1.0;
        dc.DrawRoundedRectangle(background, null, new Rect(inset * s, inset * s, (32 - 2 * inset) * s, (32 - 2 * inset) * s), 8 * s, 8 * s);

        // Three arcs with gaps: the wheel, abstracted.
        var centre = new Point(16 * s, 16 * s);
        var radius = 9.0 * s;
        var pen = new Pen(Brushes.White, Math.Max(1.6, 3.2 * s)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        for (var i = 0; i < 3; i++)
        {
            var start = -90 + i * 120 + 18;
            var end = start + 120 - 36;
            dc.DrawGeometry(null, pen, Arc(centre, radius, start, end));
        }
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xC9, 0xC6, 0xFF)), null, centre, 2.6 * s, 2.6 * s);
    }

    private static Geometry Arc(Point c, double r, double startDeg, double endDeg)
    {
        Point At(double deg) => new(c.X + r * Math.Cos(deg * Math.PI / 180), c.Y + r * Math.Sin(deg * Math.PI / 180));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(At(startDeg), false, false);
            ctx.ArcTo(At(endDeg), new Size(r, r), 0, endDeg - startDeg > 180, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }
}
