using Mo.App.Shell.Logic;

namespace Mo.App.Tests;

public class WheelGeometryTests
{
    private const double Outer = 146, Inner = 58;

    [Fact]
    public void Centre_is_the_hub_and_far_away_is_outside()
    {
        var g = new WheelGeometry(6, Outer, Inner);
        Assert.Equal(WheelHit.Hub, g.HitTest(0, 0));
        Assert.Equal(WheelHit.Hub, g.HitTest(Inner - 1, 0));
        Assert.Equal(WheelHit.Outside, g.HitTest(0, -(Outer + g.OutsideTolerance + 1)));
        Assert.Equal(0, g.HitTest(0, -(Outer + g.OutsideTolerance - 1)));
    }

    [Fact]
    public void Clockwise_starts_at_top_and_goes_right()
    {
        var g = new WheelGeometry(4, Outer, Inner);
        Assert.Equal(0, g.HitTest(0, -100));  // up
        Assert.Equal(1, g.HitTest(100, 0));   // right
        Assert.Equal(2, g.HitTest(0, 100));   // down
        Assert.Equal(3, g.HitTest(-100, 0));  // left
    }

    [Fact]
    public void Counter_clockwise_for_right_to_left_goes_left_first()
    {
        var g = new WheelGeometry(4, Outer, Inner, clockwise: false);
        Assert.Equal(0, g.HitTest(0, -100));
        Assert.Equal(1, g.HitTest(-100, 0));
        Assert.Equal(3, g.HitTest(100, 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(12)]
    public void Every_label_centre_hits_its_own_petal(int count)
    {
        foreach (var clockwise in new[] { true, false })
        {
            var g = new WheelGeometry(count, Outer, Inner, clockwise);
            for (var i = 0; i < count; i++)
            {
                var (x, y) = g.LabelCenter(i);
                Assert.Equal(i, g.HitTest(x, y));
            }
        }
    }

    [Fact]
    public void Petal_boundaries_split_between_neighbours()
    {
        var g = new WheelGeometry(12, Outer, Inner);
        // 15° is the border between petal 0 (centred at 0°) and petal 1 (centred at 30°).
        var (x1, y1) = WheelGeometry.PointAt(14.5, 100);
        var (x2, y2) = WheelGeometry.PointAt(15.5, 100);
        Assert.Equal(0, g.HitTest(x1, y1));
        Assert.Equal(1, g.HitTest(x2, y2));
        var (x3, y3) = WheelGeometry.PointAt(-14.5, 100);
        Assert.Equal(0, g.HitTest(x3, y3));
        var (x4, y4) = WheelGeometry.PointAt(-15.5, 100);
        Assert.Equal(11, g.HitTest(x4, y4));
    }

    [Fact]
    public void Outline_leaves_a_gap_and_stays_inside_the_petal()
    {
        var g = new WheelGeometry(8, Outer, Inner);
        for (var i = 0; i < 8; i++)
        {
            var o = g.Outline(i, gap: 4);
            var centre = g.CenterAngle(i);
            Assert.True(o.OuterStart > centre - 22.5 && o.OuterEnd < centre + 22.5);
            Assert.True(o.InnerStart > centre - 22.5 && o.InnerEnd < centre + 22.5);
            // The inner corners need a larger angular inset for the same gap width.
            Assert.True(o.InnerStart - (centre - 22.5) > o.OuterStart - (centre - 22.5));
            Assert.Equal(centre, (o.OuterStart + o.OuterEnd) / 2, 6);
        }
    }

    [Fact]
    public void Label_width_shrinks_with_more_petals()
    {
        var few = new WheelGeometry(3, Outer, Inner).LabelMaxWidth();
        var many = new WheelGeometry(12, Outer, Inner).LabelMaxWidth();
        Assert.True(many < few);
        Assert.True(many > 30);
    }

    [Fact]
    public void Invalid_arguments_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WheelGeometry(0, Outer, Inner));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WheelGeometry(3, Inner, Outer));
    }

    [Theory]
    [InlineData(50, 12, 100, 12)]     // fits: unchanged
    [InlineData(200, 12, 100, 5.88)]  // twice too wide: about half the size
    public void Text_shrinks_to_fit(double measured, double size, double max, double expected)
    {
        Assert.Equal(expected, TextFit.FontSize(measured, size, max), 2);
    }
}
