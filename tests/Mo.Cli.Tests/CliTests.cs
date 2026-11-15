using System.Diagnostics;
using ImageMagick;

namespace Mo.Cli.Tests;

/// <summary>Runs the real "mo" command line tool as a separate process.</summary>
public sealed class CliTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("mo-cli-").FullName;

    public void Dispose() => Directory.Delete(_folder, true);

    private string NewPng(string name)
    {
        var path = Path.Combine(_folder, name);
        using var image = new MagickImage(MagickColors.Orange, 40, 30);
        image.Write(path, MagickFormat.Png);
        return path;
    }

    /// <summary>mo.dll from the Mo.Cli build that matches this test build (same configuration and framework).</summary>
    private static string CliDll()
    {
        var bin = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var framework = bin.Name;
        var configuration = bin.Parent!.Name;
        var root = bin;
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MoConverter.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var dll = Path.Combine(root.FullName, "src", "Mo.Cli", "bin", configuration, framework, "mo.dll");
        Assert.True(File.Exists(dll), $"Build Mo.Cli first: {dll}");
        return dll;
    }

    private static (int Exit, string Out, string Err) Mo(params string[] args)
    {
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet";
        var start = new ProcessStartInfo(dotnet)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(CliDll());
        foreach (var arg in args) start.ArgumentList.Add(arg);

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(60_000), "mo did not finish within a minute");
        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    [Fact]
    public void Without_arguments_it_prints_usage()
    {
        // Usage goes to stderr when no command is given (exit 2), as command-line tools usually do.
        var (exit, _, error) = Mo();
        Assert.Equal(2, exit);
        Assert.Contains("mo list", error);
        Assert.Contains("mo run", error);
    }

    [Fact]
    public void List_shows_what_can_be_done_with_an_image()
    {
        var png = NewPng("logo.png");
        var (exit, output, _) = Mo("list", png);
        Assert.Equal(0, exit);
        Assert.Contains("image.to-jpg", output);
        Assert.Contains("image.compress", output);
        Assert.DoesNotContain("image.to-png ", output);
    }

    [Fact]
    public void Run_converts_into_the_chosen_folder()
    {
        var png = NewPng("logo.png");
        var outFolder = Path.Combine(_folder, "out");
        var (exit, output, error) = Mo("run", "image.to-webp", png, "--out", outFolder);

        Assert.True(exit == 0, error);
        var expected = Path.Combine(outFolder, "logo.webp");
        Assert.Contains(expected, output);
        using var result = new MagickImage(expected);
        Assert.Equal(MagickFormat.WebP, result.Format);
        Assert.Equal(40u, result.Width);
    }

    [Fact]
    public void Run_handles_several_files_at_once()
    {
        var a = NewPng("a.png");
        var b = NewPng("b.png");
        var (exit, output, error) = Mo("run", "image.to-jpg", a, b);

        Assert.True(exit == 0, error);
        Assert.True(File.Exists(Path.Combine(_folder, "a.jpg")));
        Assert.True(File.Exists(Path.Combine(_folder, "b.jpg")));
        Assert.Equal(2, output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void Unknown_operation_fails()
    {
        var (exit, _, error) = Mo("run", "image.to-banana", NewPng("x.png"));
        Assert.Equal(1, exit);
        Assert.Contains("image.to-banana", error);
    }

    [Fact]
    public void Missing_file_fails_without_output()
    {
        var (exit, output, _) = Mo("run", "image.to-jpg", Path.Combine(_folder, "missing.png"));
        Assert.Equal(1, exit);
        Assert.True(string.IsNullOrWhiteSpace(output));
    }

    [Fact]
    public void Operation_refuses_files_it_does_not_handle()
    {
        var text = Path.Combine(_folder, "notes.txt");
        File.WriteAllText(text, "hello");
        var (exit, _, error) = Mo("run", "image.to-jpg", text);
        Assert.Equal(1, exit);
        // The text follows the PC language; the operation id is the same in every language.
        Assert.Contains("image.to-jpg", error);
    }
}
