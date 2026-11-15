using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mo.App.Shell;
using Mo.App.Shell.Logic;

namespace Mo.App.Tests;

/// <summary>Renders the wheel and the icon off-screen (nothing is shown on the desktop).</summary>
public class RenderingTests
{
    private static void OnSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
    }

    private static int InkPixels(FrameworkElement element)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        var ink = 0;
        for (var i = 3; i < pixels.Length; i += 4) if (pixels[i] > 0) ink++;
        return ink;
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(5, true)]
    [InlineData(12, false)]
    public void Wheel_renders_any_petal_count_in_both_directions(int count, bool rightToLeft)
    {
        OnSta(() =>
        {
            var petals = Enumerable.Range(0, count)
                .Select(i => i % 2 == 0
                    ? new WheelPetal(null, "In PNG umwandeln", "KONVERTIEREN", "PNG", "\uE8B9")
                    : new WheelPetal(null, "\u0646\u0633\u062E \u0627\u0644\u0645\u0633\u0627\u0631 \u0625\u0644\u0649 \u0627\u0644\u062D\u0627\u0641\u0638\u0629", "\u0646\u0633\u062E \u0627\u0644\u0645\u0633\u0627\u0631", null, "\uE8C8"))
                .ToList();
            var view = new WheelView
            {
                Petals = petals,
                Caption = "UMWANDELN",
                HubText = "Ein sehr langer Titel, der schrumpfen muss",
                HighlightIndex = 0,
                RightToLeftText = rightToLeft,
                Clockwise = !rightToLeft,
                PetalBrush = Brushes.White,
                PlateBrush = Brushes.LightGray,
                AccentBrush = Brushes.SlateBlue,
                TextBrush = Brushes.Black,
                AccentTextBrush = Brushes.White,
                HubBrush = Brushes.WhiteSmoke,
            };
            Assert.True(InkPixels(view) > 10_000);
            Assert.Equal(count, view.Geometry.PetalCount);
            Assert.Equal(WheelHit.Hub, view.HitTestPoint(new Point(view.ActualWidth / 2, view.ActualHeight / 2)));
            Assert.Equal(0, view.HitTestPoint(new Point(view.ActualWidth / 2, view.ActualHeight / 2 - 100)));
        });
    }

    [Fact]
    public void Tray_icon_is_generated()
    {
        OnSta(() =>
        {
            using var icon = IconFactory.CreateTrayIcon();
            Assert.True(icon.Width >= 16);
        });
    }
}
