namespace Mo.Engine.Tests;

public class FoundationTests
{
    [Theory]
    [InlineData("a.PNG", FileCategory.Image)]
    [InlineData("report.pdf", FileCategory.Pdf)]
    [InlineData("clip.mkv", FileCategory.Video)]
    [InlineData("song.flac", FileCategory.Audio)]
    [InlineData("table.csv", FileCategory.Spreadsheet)]
    [InlineData("unknown.xyz", FileCategory.Other)]
    public void Classifies_by_extension(string path, FileCategory expected) =>
        Assert.Equal(expected, FileTypes.Classify(path));

    [Fact]
    public void InputSet_reports_common_category_only_when_uniform()
    {
        Assert.Equal(FileCategory.Image, new InputSet(["a.png", "b.jpg"]).CommonCategory);
        Assert.Null(new InputSet(["a.png", "b.pdf"]).CommonCategory);
        Assert.True(new InputSet(["a.jpg", "b.jpeg"]).AllHaveExtension("jpg", "jpeg"));
    }

    [Fact]
    public void OutputPlanner_never_reuses_a_name()
    {
        var folder = Directory.CreateTempSubdirectory("mo-tests-").FullName;
        try
        {
            var source = Path.Combine(folder, "photo.jpg");
            File.WriteAllText(source, "");
            File.WriteAllText(Path.Combine(folder, "photo.png"), "");
            var planner = new OutputPlanner();
            Assert.Equal(Path.Combine(folder, "photo (2).png"), planner.PathFor(source, "png"));
            Assert.Equal(Path.Combine(folder, "photo (3).png"), planner.PathFor(source, "png"));
            Assert.Equal(Path.Combine(folder, "photo-small.jpg"), planner.PathFor(source, "jpg", "small"));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Registry_matches_by_wheel_and_orders()
    {
        var registry = new OperationRegistry()
            .Add(new Fake("b", WheelKind.Convert, 2))
            .Add(new Fake("a", WheelKind.Convert, 1))
            .Add(new Fake("t", WheelKind.Tools, 1));
        var matched = registry.Match(WheelKind.Convert, new InputSet(["x.png"]));
        Assert.Equal(["a", "b"], matched.Select(o => o.Id));
        Assert.Throws<InvalidOperationException>(() => registry.Add(new Fake("a", WheelKind.Tools, 9)));
    }

    [Fact]
    public void Every_language_has_every_engine_key()
    {
        var loc = new Localizer("en");
        var english = loc.Keys("en").ToHashSet();
        foreach (var language in Localizer.Languages)
            Assert.Empty(english.Except(loc.Keys(language)));
    }

    [Fact]
    public void Settings_round_trip()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mo-settings-{Guid.NewGuid():N}.json");
        try
        {
            new AppSettings { Language = "de", ToolsKey = ModifierKey.Alt }.Save(path);
            var loaded = AppSettings.Load(path);
            Assert.Equal("de", loaded.Language);
            Assert.Equal(ModifierKey.Alt, loaded.ToolsKey);
        }
        finally { File.Delete(path); }
    }

    private sealed class Fake(string id, WheelKind wheel, int order) : IOperation
    {
        public string Id => id;
        public WheelKind Wheel => wheel;
        public string TitleKey => id;
        public string? Badge => null;
        public string Glyph => "";
        public int Order => order;
        public bool Accepts(InputSet input) => true;
        public Task<OperationResult> RunAsync(OperationContext context, CancellationToken cancellation) =>
            Task.FromResult(OperationResult.Of());
    }
}
