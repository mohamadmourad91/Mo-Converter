using System.IO;
using Mo.App.ViewModels;
using Mo.Engine;

namespace Mo.App.Tests;

public class ShelfViewModelTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("mo-shelf-").FullName;

    public ShelfViewModelTests() => Localizer.Current = new Localizer("en", typeof(App).Assembly);

    public void Dispose() => Directory.Delete(_folder, true);

    private string NewFile(string name)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, name);
        return path;
    }

    [Fact]
    public void Only_existing_files_are_added_newest_first()
    {
        var a = NewFile("a.txt");
        var b = NewFile("b.txt");
        var model = new ShelfViewModel();
        Assert.True(model.IsEmpty);

        var added = model.Add([a, Path.Combine(_folder, "missing.txt"), b]);

        Assert.Equal(2, added);
        Assert.Equal(["b.txt", "a.txt"], model.Items.Select(i => i.Name));
        Assert.Equal("2 files", model.CountText);
    }

    [Fact]
    public void A_file_added_again_moves_to_the_top_instead_of_repeating()
    {
        var a = NewFile("a.txt");
        var b = NewFile("b.txt");
        var model = new ShelfViewModel();
        model.Add([a, b]);
        model.Add([a]);
        Assert.Equal(["a.txt", "b.txt"], model.Items.Select(i => i.Name));
    }

    [Fact]
    public void The_shelf_holds_at_most_its_capacity_and_drops_the_oldest()
    {
        var model = new ShelfViewModel();
        var files = Enumerable.Range(0, ShelfViewModel.Capacity + 5).Select(i => NewFile($"f{i:000}.txt")).ToList();
        model.Add(files);
        Assert.Equal(ShelfViewModel.Capacity, model.Items.Count);
        Assert.Equal("f044.txt", model.Items[0].Name);
    }

    [Fact]
    public void Removed_files_are_pruned_and_remove_and_clear_work()
    {
        var a = NewFile("a.txt");
        var b = NewFile("b.txt");
        var model = new ShelfViewModel();
        model.Add([a, b]);

        File.Delete(a);
        Assert.Equal(1, model.Prune());
        Assert.Equal(["b.txt"], model.Items.Select(i => i.Name));

        model.RemoveCommand.Execute(model.Items[0]);
        Assert.True(model.IsEmpty);

        model.Add([b]);
        Assert.True(model.ClearCommand.CanExecute(null));
        model.ClearCommand.Execute(null);
        Assert.True(model.IsEmpty);
    }

    [Fact]
    public void Folders_can_be_kept_on_the_shelf()
    {
        var folder = Path.Combine(_folder, "photos");
        Directory.CreateDirectory(folder);
        var model = new ShelfViewModel();
        Assert.Equal(1, model.Add([folder]));
        Assert.Equal("photos", model.Items[0].Name);
    }
}

public class WelcomeSettingsTests
{
    [Fact]
    public void The_guide_is_shown_again_until_its_box_is_ticked()
    {
        var settings = new AppSettings();
        Assert.False(settings.WelcomeShown);
        var path = Path.Combine(Path.GetTempPath(), $"mo-welcome-{Guid.NewGuid():N}.json");
        try
        {
            settings.WelcomeShown = true;
            settings.Save(path);
            Assert.True(AppSettings.Load(path).WelcomeShown);
        }
        finally { File.Delete(path); }
    }
}
