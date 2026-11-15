using Mo.App.ViewModels;
using Mo.App.Services;
using Mo.Engine;

namespace Mo.App.Tests;

public class PluralAppTests
{
    [Theory]
    [InlineData("en", 1, "1 page")]
    [InlineData("en", 3, "3 pages")]
    [InlineData("de", 1, "1 Seite")]
    [InlineData("de", 4, "4 Seiten")]
    [InlineData("ar", 1, "صفحة واحدة")]
    [InlineData("ar", 2, "صفحتان")]
    [InlineData("ar", 7, "7 صفحات")]
    [InlineData("ar", 12, "12 صفحة")]
    public void Page_counts_read_correctly(string language, int count, string expected)
    {
        Localizer.Current = new Localizer(language, typeof(App).Assembly);
        Assert.Equal(expected, new PdfPagesViewModel(count).CountText);
    }

    [Theory]
    [InlineData("en", 2, "2 paths copied")]
    [InlineData("ar", 2, "نُسخ مساران")]
    [InlineData("ar", 5, "نُسخت 5 مسارات")]
    public void Result_messages_with_a_count_use_the_right_form(string language, int count, string expected)
    {
        Localizer.Current = new Localizer(language, typeof(App).Assembly);
        Assert.Equal(expected, OperationRunner.Describe(OperationResult.Message("result.copied-paths", count)));
    }

    [Fact]
    public void Several_saved_files_use_the_plural_form()
    {
        Localizer.Current = new Localizer("ar", typeof(App).Assembly);
        Assert.Equal("تم حفظ ملفين", OperationRunner.Describe(OperationResult.Of("a", "b")));
        Assert.Equal("تم حفظ 12 ملفًا", OperationRunner.Describe(OperationResult.Of(Enumerable.Range(0, 12).Select(i => $"f{i}").ToArray())));
    }
}
