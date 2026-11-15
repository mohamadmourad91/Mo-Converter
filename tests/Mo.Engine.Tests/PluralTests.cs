namespace Mo.Engine.Tests;

/// <summary>Counts must read correctly in every language: "1 file", "ملفان", "11 ملفًا".</summary>
public class PluralTests
{
    [Theory]
    [InlineData(0, "other")]
    [InlineData(1, "one")]
    [InlineData(2, "other")]
    [InlineData(21, "other")]
    public void English_and_german_tell_one_from_the_rest(int count, string category)
    {
        Assert.Equal(category, new Localizer("en").Category(count));
        Assert.Equal(category, new Localizer("de").Category(count));
    }

    [Theory]
    [InlineData(1, "one")]
    [InlineData(2, "two")]
    [InlineData(3, "few")]
    [InlineData(10, "few")]
    [InlineData(11, "many")]
    [InlineData(99, "many")]
    [InlineData(100, "other")]
    [InlineData(102, "other")]
    [InlineData(103, "few")]
    [InlineData(111, "many")]
    public void Arabic_has_five_forms(int count, string category) => Assert.Equal(category, new Localizer("ar").Category(count));

    [Theory]
    [InlineData("en", 1, "Saved 1 file")]
    [InlineData("en", 2, "Saved 2 files")]
    [InlineData("de", 1, "1 Datei gespeichert")]
    [InlineData("de", 5, "5 Dateien gespeichert")]
    [InlineData("ar", 1, "تم حفظ ملف واحد")]
    [InlineData("ar", 2, "تم حفظ ملفين")]
    [InlineData("ar", 3, "تم حفظ 3 ملفات")]
    [InlineData("ar", 11, "تم حفظ 11 ملفًا")]
    [InlineData("ar", 100, "تم حفظ 100 ملف")]
    public void Saved_files_read_correctly(string language, int count, string expected) =>
        Assert.Equal(expected, new Localizer(language).N("result.saved-many", count));

    [Fact]
    public void A_key_without_forms_falls_back_to_its_plain_text()
    {
        // "export.table" has no plural forms: the count is just {0}.
        Assert.Equal("Table 7", new Localizer("en").N("export.table", 7));
    }

    [Fact]
    public void Every_plural_form_keeps_the_count_placeholder_when_it_has_one()
    {
        // Singular forms may say "one file" in words; the others must show the number.
        foreach (var language in Localizer.Languages)
        {
            var t = new Localizer(language);
            foreach (var key in t.Keys(language).Where(k => k.EndsWith(".many") || k.EndsWith(".other") || k.EndsWith(".few")))
                Assert.Contains("{0}", t.T(key));
        }
    }
}
