using System.IO;
using System.IO.Compression;
using Mo.App.Services.Demo;
using Mo.Engine;

namespace Mo.App.Tests;

public sealed class DemoOperationsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("mo-demo-").FullName;

    public DemoOperationsTests() => Localizer.Current = new Localizer("en", typeof(App).Assembly);

    public void Dispose() => Directory.Delete(_folder, true);

    private string NewFile(string name, string content = "hello")
    {
        var path = Path.Combine(_folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static Task<OperationResult> Run(IOperation operation, params string[] paths) =>
        operation.RunAsync(new OperationContext(new InputSet(paths), new OutputPlanner()), CancellationToken.None);

    [Fact]
    public async Task Zip_of_one_file_sits_next_to_it()
    {
        var file = NewFile("notes.txt");
        var result = await Run(new ZipOperation(), file);

        var zip = Assert.Single(result.Outputs);
        Assert.Equal(Path.Combine(_folder, "notes.zip"), zip);
        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal("notes.txt", Assert.Single(archive.Entries).FullName);
    }

    [Fact]
    public async Task Zip_of_files_and_a_folder_keeps_the_folder_structure()
    {
        var a = NewFile("a.txt");
        NewFile(Path.Combine("photos", "one.jpg"));
        NewFile(Path.Combine("photos", "trip", "two.jpg"));
        var folder = Path.Combine(_folder, "photos");

        var result = await Run(new ZipOperation(), a, folder);

        var zip = Assert.Single(result.Outputs);
        Assert.Equal(Path.Combine(_folder, "Archive.zip"), zip);
        using var archive = ZipFile.OpenRead(zip);
        Assert.Equal(["a.txt", "photos/one.jpg", "photos/trip/two.jpg"], archive.Entries.Select(e => e.FullName).Order());
    }

    [Fact]
    public async Task Zip_never_overwrites_an_existing_archive()
    {
        var file = NewFile("notes.txt");
        NewFile("notes.zip", "keep me");

        var result = await Run(new ZipOperation(), file);

        Assert.Equal(Path.Combine(_folder, "notes (2).zip"), Assert.Single(result.Outputs));
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(_folder, "notes.zip")));
    }

    [Fact]
    public async Task Zip_reports_a_missing_file_by_name()
    {
        var error = await Assert.ThrowsAsync<OperationException>(() => Run(new ZipOperation(), Path.Combine(_folder, "gone.txt")));
        Assert.Equal("error.file-missing", error.Key);
        Assert.Equal("gone.txt", error.Args[0]);
    }

    [Fact]
    public async Task Cancelled_zip_leaves_no_half_written_file()
    {
        var file = NewFile("notes.txt");
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ZipOperation().RunAsync(new OperationContext(new InputSet([file]), new OutputPlanner()), cancel.Token));
        Assert.False(File.Exists(Path.Combine(_folder, "notes.zip")));
    }

    [Fact]
    public void Zip_is_not_offered_for_a_single_archive()
    {
        var zip = new ZipOperation();
        Assert.False(zip.Accepts(new InputSet(["backup.zip"])));
        Assert.True(zip.Accepts(new InputSet(["backup.zip", "notes.txt"])));
        Assert.False(zip.Accepts(new InputSet([])));
    }

    [Fact]
    public async Task Show_in_explorer_refuses_missing_files_without_opening_anything()
    {
        var error = await Assert.ThrowsAsync<OperationException>(() =>
            Run(new ShowInExplorerOperation(), Path.Combine(_folder, "nope.png")));
        Assert.Equal("error.file-missing", error.Key);
    }

    [Fact]
    public void Registering_twice_adds_each_action_once()
    {
        var registry = new OperationRegistry();
        DemoOperations.Register(registry);
        DemoOperations.Register(registry);
        Assert.Equal(2, registry.Match(WheelKind.Actions, new InputSet(["x.txt"])).Count);
        Assert.Single(registry.Match(WheelKind.Tools, new InputSet(["x.txt"])));
    }

    [Fact]
    public void Every_wheel_offers_something_for_any_file()
    {
        var registry = OperationRegistry.CreateDefault();
        DemoOperations.Register(registry);
        var input = new InputSet(["contract.unknownext"]);
        Assert.NotEmpty(registry.Match(WheelKind.Actions, input));
        Assert.NotEmpty(registry.Match(WheelKind.Tools, input));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("ar")]
    public void App_operation_titles_are_translated(string language)
    {
        var t = new Localizer(language, typeof(App).Assembly);
        foreach (IOperation operation in new IOperation[] { new CopyPathOperation(), new ShowInExplorerOperation(), new ZipOperation() })
            Assert.True(t.Has(operation.TitleKey, language), $"{operation.Id} ({language})");
    }
}
