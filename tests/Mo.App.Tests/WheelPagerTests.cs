using Mo.App.Shell.Logic;

namespace Mo.App.Tests;

public class WheelPagerTests
{
    private static List<int> Items(int n) => Enumerable.Range(1, n).ToList();

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    public void Up_to_twelve_fit_on_one_page_without_more(int n)
    {
        var page = WheelPager.Page(Items(n), 0);
        Assert.Equal(n, page.Count);
        Assert.DoesNotContain(page, s => s.IsMore);
        Assert.Equal(1, WheelPager.PageCount(n));
    }

    [Fact]
    public void Thirteen_items_make_two_pages_of_eleven_plus_more()
    {
        var items = Items(13);
        Assert.Equal(2, WheelPager.PageCount(13));

        var first = WheelPager.Page(items, 0);
        Assert.Equal(12, first.Count);
        Assert.True(first[^1].IsMore);
        Assert.Equal(Enumerable.Range(1, 11), first.Take(11).Select(s => s.Item));

        var second = WheelPager.Page(items, 1);
        Assert.Equal([12, 13], second.Where(s => !s.IsMore).Select(s => s.Item));
        Assert.True(second[^1].IsMore);
    }

    [Fact]
    public void Paging_wraps_around()
    {
        var items = Items(23); // 11 + 11 + 1
        Assert.Equal(3, WheelPager.PageCount(23));
        Assert.Equal(WheelPager.Page(items, 0).Select(s => s.Item), WheelPager.Page(items, 3).Select(s => s.Item));
    }
}
