namespace Mo.Engine.Tests;

/// <summary>Rules every part of the engine relies on: output naming, texts, file classification, the default registry.</summary>
public class EngineContractTests
{
    [Fact]
    public void OutputPlanner_writes_into_a_fixed_folder_and_creates_it()
    {
        var root = Directory.CreateTempSubdirectory("mo-out-").FullName;
        try
        {
            var target = Path.Combine(root, "results", "deep");
            var planner = new OutputPlanner(target);
            var path = planner.PathFor(@"C:\somewhere\holiday.heic", "jpg");
            Assert.Equal(Path.Combine(target, "holiday.jpg"), path);
            Assert.True(Directory.Exists(target));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void OutputPlanner_hands_out_unique_names_under_parallel_load()
    {
        var root = Directory.CreateTempSubdirectory("mo-out-").FullName;
        try
        {
            var planner = new OutputPlanner(root);
            var paths = new System.Collections.Concurrent.ConcurrentBag<string>();
            Parallel.For(0, 64, _ => paths.Add(planner.PathFor("photo.png", "webp")));
            Assert.Equal(64, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Contains(Path.Combine(root, "photo.webp"), paths);
            Assert.Contains(Path.Combine(root, "photo (64).webp"), paths);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void OutputPlanner_folders_never_collide_with_existing_ones()
    {
        var root = Directory.CreateTempSubdirectory("mo-out-").FullName;
        try
        {
            var source = Path.Combine(root, "report.pdf");
            File.WriteAllText(source, "");
            var planner = new OutputPlanner();
            var first = planner.FolderFor(source, "pages");
            var second = planner.FolderFor(source, "pages");
            Assert.Equal(Path.Combine(root, "report-pages"), first);
            Assert.Equal(Path.Combine(root, "report-pages (2)"), second);
            Assert.True(Directory.Exists(second));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Localizer_falls_back_to_english_then_to_the_key()
    {
        var french = new Localizer("fr");
        Assert.Equal("en", french.Language);
        Assert.False(french.IsRightToLeft);
        Assert.Equal("no.such.key", new Localizer("de").T("no.such.key"));
    }

    [Fact]
    public void Localizer_fills_placeholders()
    {
        var key = new Localizer("en").Keys("en").First(k => new Localizer("en").T(k).Contains("{0}"));
        var text = new Localizer("en").T(key, "XYZ-42");
        Assert.Contains("XYZ-42", text);
        Assert.DoesNotContain("{0}", text);
    }

    [Fact]
    public void Placeholders_match_across_languages()
    {
        var loc = new Localizer("en");
        foreach (var key in loc.Keys("en"))
        {
            var english = Placeholders(new Localizer("en").T(key));
            foreach (var language in new[] { "de", "ar" })
                Assert.True(english.SetEquals(Placeholders(new Localizer(language).T(key))), $"{language}: {key}");
        }
    }

    private static HashSet<string> Placeholders(string text) =>
        System.Text.RegularExpressions.Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).ToHashSet();

    [Fact]
    public void Empty_selection_matches_nothing()
    {
        var empty = new InputSet([]);
        Assert.Null(empty.CommonCategory);
        Assert.False(empty.AllAre(FileCategory.Image));
        Assert.False(empty.AllHaveExtension("png"));
        foreach (var wheel in Enum.GetValues<WheelKind>())
            Assert.Empty(OperationRegistry.CreateDefault().Match(wheel, empty));
    }

    [Theory]
    [InlineData("Photo.JPEG", "jpeg", FileCategory.Image)]
    [InlineData("archive.tar.gz", "gz", FileCategory.Archive)]
    [InlineData("README", "", FileCategory.Other)]
    [InlineData(@"C:\folder.with.dots\file.WebP", "webp", FileCategory.Image)]
    public void Extension_is_lower_case_without_the_dot(string path, string extension, FileCategory category)
    {
        Assert.Equal(extension, FileTypes.ExtensionOf(path));
        Assert.Equal(category, FileTypes.Classify(path));
    }

    [Fact]
    public void Every_default_operation_is_complete_and_translated()
    {
        var registry = OperationRegistry.CreateDefault();
        var all = Enum.GetValues<WheelKind>()
            .SelectMany(w => new[] { new InputSet(["a.png"]), new InputSet(["a.jpg", "b.webp"]), new InputSet(["a.pdf"]), new InputSet(["a.pdf", "b.pdf"]) }
                .SelectMany(sample => registry.Match(w, sample)))
            .DistinctBy(o => o.Id)
            .ToList();
        Assert.NotEmpty(all);

        var loc = new Localizer("en");
        foreach (var operation in all)
        {
            // One icon-font character from the private use area (a damaged encoding once turned these into 3 characters).
            Assert.True(operation.Glyph.Length == 1 && operation.Glyph[0] is >= '\uE000' and <= '\uF8FF',
                $"{operation.Id} glyph: {string.Join(" ", operation.Glyph.Select(c => ((int)c).ToString("X4")))}");
            foreach (var language in Localizer.Languages)
                Assert.True(loc.Has(operation.TitleKey, language), $"{operation.Id} has no {language} title");
        }
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("report.pdf")]
    [InlineData("movie.mp4")]
    public void Image_operations_ignore_files_that_are_not_images(string file)
    {
        var registry = OperationRegistry.CreateDefault();
        foreach (var wheel in Enum.GetValues<WheelKind>())
            Assert.DoesNotContain(registry.Match(wheel, new InputSet([file])), o => o.Id.StartsWith("image."));
    }
}
