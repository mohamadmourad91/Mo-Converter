using ImageMagick;
using Mo.Engine;

namespace Mo.Cli.Tests;

/// <summary>The command line's rules, run in-process so each case is quick and its output can be read back.</summary>
public sealed class CliRulesTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("mo-cli-rules-").FullName;

    public CliRulesTests() => Localizer.Current = new Localizer("en");

    public void Dispose() => Directory.Delete(_folder, true);

    private string NewPng()
    {
        var path = Path.Combine(_folder, "logo.png");
        using var image = new MagickImage(MagickColors.Orange, 20, 20);
        image.Write(path, MagickFormat.Png);
        return path;
    }

    private static async Task<(int Code, string Out, string Err)> Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = await Mo.Cli.Cli.RunAsync(args, output, error);
        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task Help_is_shown_on_request_and_exits_ok()
    {
        var (code, output, _) = await Run("help");
        Assert.Equal(0, code);
        Assert.Contains("mo run <operation-id>", output);
        Assert.Contains("--out", output);
    }

    [Fact]
    public async Task Running_without_arguments_shows_usage_and_exits_with_usage_code()
    {
        var (code, _, error) = await Run();
        Assert.Equal(Mo.Cli.Cli.Usage, code);
        Assert.Contains("mo list", error);
    }

    [Fact]
    public async Task Version_prints_a_version_number()
    {
        var (code, output, _) = await Run("version");
        Assert.Equal(0, code);
        Assert.Matches(@"^\d+\.\d+\.\d+\s*$", output.Trim() + "\n");
    }

    [Fact]
    public async Task Run_without_files_is_a_usage_error()
    {
        var (code, _, error) = await Run("run", "image.to-jpg");
        Assert.Equal(Mo.Cli.Cli.Usage, code);
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task Out_without_a_folder_is_a_usage_error()
    {
        var (code, _, error) = await Run("run", "image.to-jpg", NewPng(), "--out");
        Assert.Equal(Mo.Cli.Cli.Usage, code);
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task Run_writes_the_result_path_and_no_error_text()
    {
        var output = Path.Combine(_folder, "out");
        var (code, stdout, _) = await Run("run", "image.to-jpg", NewPng(), "--out", output);
        Assert.Equal(0, code);
        Assert.Contains(Path.Combine(output, "logo.jpg"), stdout);
        Assert.True(File.Exists(Path.Combine(output, "logo.jpg")));
    }

    [Fact]
    public async Task A_relative_output_folder_is_resolved_against_the_current_folder()
    {
        var (code, stdout, _) = await Run("run", "image.to-jpg", NewPng(), "--out", "relative-out");
        Assert.Equal(0, code);
        var expected = Path.GetFullPath(Path.Combine("relative-out", "logo.jpg"));
        Assert.Contains(expected, stdout);
        File.Delete(expected);
        Directory.Delete(Path.GetDirectoryName(expected)!);
    }

    [Fact]
    public async Task Missing_file_is_reported_with_its_name()
    {
        var (code, _, error) = await Run("run", "image.to-jpg", Path.Combine(_folder, "gone.png"));
        Assert.Equal(Mo.Cli.Cli.Failed, code);
        Assert.Contains("gone.png", error);
    }

    [Fact]
    public async Task List_for_nothing_is_a_usage_error()
    {
        var (code, _, _) = await Run("list");
        Assert.Equal(Mo.Cli.Cli.Usage, code);
    }
}
