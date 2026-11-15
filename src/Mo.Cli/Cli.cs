using System.Reflection;
using Mo.Engine;

namespace Mo.Cli;

/// <summary>The command line, separate from the entry point so the tests can call it.</summary>
public static class Cli
{
    public const int Ok = 0, Failed = 1, Usage = 2;

    public static string UsageText(string language) => """
        Mo Converter command line

          mo list <files...>                       what can be done with these files
          mo run <operation-id> <files...>         run one operation on the files
                 [--out <folder>]                  write results there (default: next to each file)
          mo help                                  this text
          mo version                               program version

        Example:
          mo run image.to-webp photo.jpg --out C:\Converted
        """;

    public static async Task<int> RunAsync(string[] args, TextWriter? output = null, TextWriter? error = null)
    {
        output ??= Console.Out;
        error ??= Console.Error;
        var settings = AppSettings.Load();
        var loc = Localizer.Current = new Localizer(settings.Language ?? Localizer.SystemLanguage());
        var registry = OperationRegistry.CreateDefault();

        if (args.Length == 0)
        {
            error.WriteLine(UsageText(loc.Language));
            return Usage;
        }
        if (args[0] is "help" or "--help" or "-h" or "/?")
        {
            output.WriteLine(UsageText(loc.Language));
            return Ok;
        }
        if (args[0] is "version" or "--version")
        {
            output.WriteLine(Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0");
            return Ok;
        }

        if (args[0] == "list")
        {
            if (args.Length < 2)
            {
                error.WriteLine(UsageText(loc.Language));
                return Usage;
            }
            var input = new InputSet(args.Skip(1));
            foreach (var wheel in Enum.GetValues<WheelKind>())
                foreach (var operation in registry.Match(wheel, input))
                    output.WriteLine($"{wheel,-8} {operation.Id,-28} {loc.T(operation.TitleKey)}");
            return Ok;
        }

        if (args[0] != "run" || args.Length < 3)
        {
            error.WriteLine(UsageText(loc.Language));
            return Usage;
        }

        var rest = args.Skip(2).ToList();
        string? folder = null;
        var outIndex = rest.IndexOf("--out");
        if (outIndex >= 0)
        {
            if (outIndex + 1 >= rest.Count)
            {
                error.WriteLine(loc.T("error.cli-out-missing"));
                return Usage;
            }
            folder = rest[outIndex + 1];
            rest.RemoveRange(outIndex, 2);
        }
        if (folder is not null && !Path.IsPathFullyQualified(folder)) folder = Path.GetFullPath(folder);

        var chosen = registry.Find(args[1]);
        if (chosen is null)
        {
            error.WriteLine(loc.T("error.unknown-operation", args[1]));
            return Failed;
        }

        var files = new InputSet(rest);
        if (files.Count == 0)
        {
            error.WriteLine(loc.T("error.cli-no-files"));
            return Usage;
        }
        var missing = files.Paths.FirstOrDefault(p => !File.Exists(p) && !Directory.Exists(p));
        if (missing is not null)
        {
            error.WriteLine(loc.T("error.file-missing", missing));
            return Failed;
        }
        if (!chosen.Accepts(files))
        {
            error.WriteLine(loc.T("error.cli-not-accepted", chosen.Id));
            return Failed;
        }

        try
        {
            var options = new Dictionary<string, object?> { ["level"] = settings.ImageCompression };
            var progress = new Progress<OperationProgress>(p => error.Write($"\r{p.Fraction:P0} "));
            var context = new OperationContext(files, new OutputPlanner(folder), progress, options);
            var result = await chosen.RunAsync(context, CancellationToken.None);
            error.WriteLine();
            foreach (var path in result.Outputs) output.WriteLine(path);
            if (result.MessageKey is not null) output.WriteLine(loc.T(result.MessageKey, result.MessageArgs ?? []));
            return Ok;
        }
        catch (OperationException e)
        {
            error.WriteLine("\n" + loc.T(e.Key, e.Args));
            return Failed;
        }
    }
}
