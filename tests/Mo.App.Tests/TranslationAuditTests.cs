using System.IO;
using System.Text.RegularExpressions;
using Mo.Engine;
using Mo.Engine.Pdf.Layout;

namespace Mo.App.Tests;

/// <summary>
/// Texts are the product's face in three languages. These tests read the source and fail when a key is used
/// but missing, translated with different placeholders, left empty, or not written in its own script.
/// </summary>
public class TranslationAuditTests
{
    private static string Root
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "MoConverter.slnx"))) return dir.FullName;
            throw new InvalidOperationException("Solution folder not found.");
        }
    }

    private static Localizer For(string language) => new(language, typeof(App).Assembly);

    private static IEnumerable<string> Sources(string pattern) =>
        Directory.EnumerateFiles(Path.Combine(Root, "src"), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains(@"\obj\") && !f.Contains(@"\bin\"));

    /// <summary>Keys written in code: T("key"), TitleKey => "op.x", {v:T key}, and keys built from an operation id.</summary>
    private static HashSet<string> UsedKeys()
    {
        var keys = new HashSet<string>();
        foreach (var file in Sources("*.cs"))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"\.T\(\s*""([a-z][a-z0-9.\-]*)"""))
                keys.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, @"new OperationException\(\s*""([a-z][a-z0-9.\-]*)"""))
                keys.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, @"TitleKey\s*=>\s*""([a-z][a-z0-9.\-]*)"""))
                keys.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(text, @"Message\(\s*""([a-z][a-z0-9.\-]*)"""))
                keys.Add(m.Groups[1].Value);
        }
        foreach (var file in Sources("*.xaml"))
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"\{v:T\s+([a-z][a-z0-9.\-]*)\}"))
                keys.Add(m.Groups[1].Value);
        return keys;
    }

    [Fact]
    public void Every_key_used_in_the_code_exists_in_every_language()
    {
        var missing = new List<string>();
        foreach (var language in Localizer.Languages)
        {
            var t = For(language);
            // A key ending in a dot is the start of a name built in code ("key." + "shift"); the real keys are checked below.
            foreach (var key in UsedKeys().Where(k => !k.EndsWith('.')))
                if (!t.Has(key, language)) missing.Add($"{language}: {key}");
        }
        Assert.True(missing.Count == 0, string.Join("\n", missing.Order().Take(40)));
    }

    [Theory]
    [InlineData("key.shift")]
    [InlineData("key.ctrl")]
    [InlineData("key.alt")]
    public void Keys_built_in_code_exist_in_every_language(string key)
    {
        foreach (var language in Localizer.Languages) Assert.True(For(language).Has(key, language), $"{language}: {key}");
    }

    [Fact]
    public void Every_operation_in_the_registry_has_a_title_in_every_language()
    {
        var registry = OperationRegistry.CreateDefault();
        Mo.App.Services.Demo.DemoOperations.Register(registry);
        // Collect operations by feeding typical files of every kind to every wheel.
        string[][] samples =
        [
            ["a.png"], ["a.jpg", "b.png"], ["a.pdf"], ["a.pdf", "b.pdf"], ["a.mp4"], ["a.mp4", "b.mov"], ["a.mp3"], ["a.wav", "b.mp3"],
            ["a.docx"], ["a.md"], ["a.txt"], ["a.csv"], ["a.xlsx"], ["a.pptx"], ["a.zip"], ["a.srt"], ["a.unknown"],
        ];
        var all = samples.SelectMany(s => Enum.GetValues<WheelKind>().SelectMany(w => registry.Match(w, new InputSet(s)))).DistinctBy(o => o.Id).ToList();
        Assert.True(all.Count > 60, $"only {all.Count} operations found");

        var missing = new List<string>();
        foreach (var language in Localizer.Languages)
        {
            var t = For(language);
            foreach (var operation in all)
                if (!t.Has(operation.TitleKey, language)) missing.Add($"{language}: {operation.TitleKey}");
        }
        Assert.True(missing.Count == 0, string.Join("\n", missing.Take(40)));
    }

    [Fact]
    public void Placeholders_match_between_languages()
    {
        var english = For("en");
        var problems = new List<string>();
        foreach (var key in english.Keys("en"))
        {
            var expected = Placeholders(english.T(key));
            foreach (var language in new[] { "de", "ar" })
            {
                var other = Placeholders(For(language).T(key));
                if (!expected.SetEquals(other)) problems.Add($"{language}: {key}");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(40)));
    }

    [Fact]
    public void No_text_is_empty_and_none_still_looks_like_a_key()
    {
        var problems = new List<string>();
        foreach (var language in Localizer.Languages)
        {
            var t = For(language);
            foreach (var key in t.Keys("en"))
            {
                var text = t.T(key);
                if (string.IsNullOrWhiteSpace(text)) problems.Add($"{language}: {key} is empty");
                else if (text == key) problems.Add($"{language}: {key} shows its own key");
            }
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(40)));
    }

    [Fact]
    public void Arabic_texts_are_written_in_arabic_and_german_ones_are_not_english()
    {
        var arabic = For("ar");
        var latinOnly = new List<string>();
        foreach (var key in arabic.Keys("ar"))
        {
            var text = arabic.T(key);
            // Product names, formats and short codes may stay Latin; sentences must contain Arabic letters.
            var words = Regex.Matches(text, @"\p{L}{4,}").Count;
            var hasArabic = text.Any(Script.IsRightToLeftLetter);
            if (words >= 3 && !hasArabic) latinOnly.Add(key);
        }
        Assert.True(latinOnly.Count == 0, "Arabic file has English sentences:\n" + string.Join("\n", latinOnly.Take(30)));

        // Common English words must not survive in the German file as whole sentences.
        var german = For("de");
        var english = For("en");
        var same = german.Keys("de").Where(k => german.T(k) == english.T(k) && german.T(k).Count(char.IsLetter) > 12
                                                 && german.T(k).Contains(' ')).ToList();
        Assert.True(same.Count == 0, "German texts identical to English:\n" + string.Join("\n", same.Take(30)));
    }

    private static HashSet<string> Placeholders(string text) =>
        Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).ToHashSet();
}
