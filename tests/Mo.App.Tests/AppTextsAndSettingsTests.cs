using Mo.App.Services;
using Mo.App.ViewModels;
using Mo.Engine;

namespace Mo.App.Tests;

public class AppTextsTests
{
    private static Localizer For(string language) => new(language, typeof(Mo.App.App).Assembly);

    [Theory]
    [InlineData("en", "Settings…")]
    [InlineData("de", "Einstellungen…")]
    [InlineData("ar", "\u0627\u0644\u0625\u0639\u062F\u0627\u062F\u0627\u062A…")]
    public void App_texts_are_embedded_in_the_main_assembly(string language, string expected)
    {
        var t = For(language);
        Assert.Equal("Mo Converter", t.T("app.name"));
        Assert.Equal(expected, t.T("tray.settings"));
    }

    [Theory]
    [InlineData("de")]
    [InlineData("ar")]
    public void Every_english_app_key_is_translated(string language)
    {
        var t = For(language);
        var missing = t.Keys("en").Where(k => !t.Has(k, language)).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void Arabic_is_right_to_left() => Assert.True(For("ar").IsRightToLeft);

    [Fact]
    public void Compression_preference_reaches_the_operations()
    {
        var options = OperationRunner.OptionsFor(new AppSettings { ImageCompression = CompressionLevel.Strong });
        var context = new OperationContext(new InputSet(["a.jpg"]), new OutputPlanner(), Options: options);
        Assert.Equal(CompressionLevel.Strong, context.Option("level", CompressionLevel.Balanced));
    }

    [Fact]
    public void Results_are_described_in_words()
    {
        Localizer.Current = For("en");
        Assert.Equal("Saved photo.png", OperationRunner.Describe(OperationResult.Of(@"C:\x\photo.png")));
        Assert.Equal("Saved 3 files", OperationRunner.Describe(OperationResult.Of("a", "b", "c")));
        Assert.Equal("2 paths copied", OperationRunner.Describe(OperationResult.Message("result.copied-paths", 2)));
    }
}

public class SettingsViewModelTests
{
    private sealed class FakeDialogs : ISettingsDialogs
    {
        public bool Closed;
        public bool RestartAnswer { get; set; }
        public string? PickFolder(string? current) => @"C:\Out";
        public bool AskRestart() => RestartAnswer;
        public void Close() => Closed = true;
    }

    private static SettingsViewModel Create(AppSettings settings, FakeDialogs dialogs, Action<AppSettings, bool>? saved = null) =>
        new(settings, dialogs, startWithWindows: false, _ => { }, saved ?? ((_, _) => { }));

    [Fact]
    public void Choosing_a_taken_key_swaps_so_keys_stay_distinct()
    {
        Localizer.Current = new Localizer("en", typeof(Mo.App.App).Assembly);
        var vm = Create(new AppSettings(), new FakeDialogs());
        vm.ToolsKey = ModifierKey.Shift;
        Assert.Equal(ModifierKey.Shift, vm.ToolsKey);
        Assert.Equal(ModifierKey.Ctrl, vm.ConvertKey);
        Assert.Equal(ModifierKey.Alt, vm.ActionsKey);
        Assert.True(vm.HasDistinctKeys);

        vm.ActionsKey = ModifierKey.Ctrl;
        Assert.Equal(ModifierKey.Alt, vm.ConvertKey);
        Assert.True(vm.HasDistinctKeys);
    }

    [Fact]
    public void Broken_saved_keys_are_reset_to_distinct_defaults()
    {
        Localizer.Current = new Localizer("en", typeof(Mo.App.App).Assembly);
        var settings = new AppSettings { ConvertKey = ModifierKey.Alt, ToolsKey = ModifierKey.Alt };
        var vm = Create(settings, new FakeDialogs());
        Assert.True(vm.HasDistinctKeys);
    }

    [Fact]
    public void Folder_output_needs_a_full_path_before_saving()
    {
        Localizer.Current = new Localizer("en", typeof(Mo.App.App).Assembly);
        var dialogs = new FakeDialogs();
        var vm = Create(new AppSettings(), dialogs);
        vm.UseFolder = true;
        vm.OutputFolder = "relative";
        vm.SaveCommand.Execute(null);
        Assert.True(vm.HasError);
        Assert.False(dialogs.Closed);
    }

    [Fact]
    public void Browse_selects_the_folder_option()
    {
        Localizer.Current = new Localizer("en", typeof(Mo.App.App).Assembly);
        var vm = Create(new AppSettings(), new FakeDialogs());
        vm.BrowseCommand.Execute(null);
        Assert.True(vm.UseFolder);
        Assert.Equal(@"C:\Out", vm.OutputFolder);
    }

    [Fact]
    public void Language_change_is_detected()
    {
        Localizer.Current = new Localizer("en", typeof(Mo.App.App).Assembly);
        var vm = Create(new AppSettings { Language = null }, new FakeDialogs());
        Assert.False(vm.LanguageChanged);
        vm.Language = "de";
        Assert.True(vm.LanguageChanged);
        vm.Language = SettingsViewModel.SystemLanguage;
        Assert.False(vm.LanguageChanged);
    }
}
