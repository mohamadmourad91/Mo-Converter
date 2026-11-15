using System.Diagnostics;
using System.IO.Compression;
using System.Windows;
using Mo.Engine;

namespace Mo.App.Services.Demo;

/// <summary>
/// Small, real operations that live in the app (they need the clipboard or Explorer) so every wheel
/// has something to offer for any file. Registered at startup next to the engine's operations.
/// </summary>
public static class DemoOperations
{
    public static void Register(OperationRegistry registry)
    {
        foreach (var operation in new IOperation[] { new CopyPathOperation(), new ShowInExplorerOperation(), new ZipOperation(), new ReadQrOperation() })
        {
            if (registry.Find(operation.Id) is null) registry.Add(operation);
        }
    }
}

/// <summary>Actions wheel: copies the full paths (one per line) to the clipboard.</summary>
public sealed class CopyPathOperation : IOperation
{
    public string Id => "app.copy-path";
    public WheelKind Wheel => WheelKind.Actions;
    public string TitleKey => "op.app.copy-path";
    public string? Badge => null;
    public string Glyph => "\uE8C8"; // Copy
    public int Order => 900;

    public bool Accepts(InputSet input) => input.Count > 0;

    public Task<OperationResult> RunAsync(OperationContext context, CancellationToken cancellation)
    {
        var text = string.Join(Environment.NewLine, context.Input.Paths);
        // The clipboard is an STA/UI-thread resource; operations run on the thread pool.
        Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(text));
        var count = context.Input.Count;
        return Task.FromResult(count == 1
            ? OperationResult.Message("result.copied-path")
            : OperationResult.Message("result.copied-paths", count));
    }
}

/// <summary>Actions wheel: opens Explorer with the files selected (one window per folder, at most five).</summary>
public sealed class ShowInExplorerOperation : IOperation
{
    private const int MaxWindows = 5;

    public string Id => "app.show-in-explorer";
    public WheelKind Wheel => WheelKind.Actions;
    public string TitleKey => "op.app.show-in-explorer";
    public string? Badge => null;
    public string Glyph => "\uEC50"; // FolderOpen-like "file explorer"
    public int Order => 910;

    public bool Accepts(InputSet input) => input.Count > 0;

    public Task<OperationResult> RunAsync(OperationContext context, CancellationToken cancellation)
    {
        var firstPerFolder = context.Input.Paths
            .Where(p => File.Exists(p) || Directory.Exists(p))
            .GroupBy(p => Path.GetDirectoryName(Path.GetFullPath(p).TrimEnd('\\')) ?? p, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(MaxWindows)
            .ToList();
        if (firstPerFolder.Count == 0) throw new OperationException("error.file-missing", Path.GetFileName(context.Input.Paths[0]));

        foreach (var path in firstPerFolder)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });
        }
        return Task.FromResult(OperationResult.Message("result.shown-in-explorer"));
    }
}

/// <summary>Tools wheel: packs the dropped files and folders into one ZIP next to the first item.</summary>
public sealed class ZipOperation : IOperation
{
    public string Id => "app.zip";
    public WheelKind Wheel => WheelKind.Tools;
    public string TitleKey => "op.app.zip";
    public string? Badge => "ZIP";
    public string Glyph => "\uF012"; // ZipFolder
    public int Order => 900;

    public bool Accepts(InputSet input) =>
        input.Count > 0 && !(input.IsSingle && input.AllAre(FileCategory.Archive));

    public async Task<OperationResult> RunAsync(OperationContext context, CancellationToken cancellation)
    {
        var sources = context.Input.Paths;
        var missing = sources.FirstOrDefault(p => !File.Exists(p) && !Directory.Exists(p));
        if (missing is not null) throw new OperationException("error.file-missing", Path.GetFileName(missing));

        var target = sources.Count == 1
            ? context.Output.PathFor(sources[0], "zip")
            : context.Output.PathFor(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(sources[0]))!, Localizer.Current.T("op.app.zip.archive-name")), "zip");

        // List every file first so progress can be reported per file.
        var entries = new List<(string File, string Entry)>();
        foreach (var source in sources)
        {
            var full = Path.GetFullPath(source).TrimEnd('\\');
            if (File.Exists(full))
            {
                entries.Add((full, Path.GetFileName(full)));
                continue;
            }
            var root = Path.GetDirectoryName(full)!;
            foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
                entries.Add((file, Path.GetRelativePath(root, file).Replace('\\', '/')));
        }

        try
        {
            await using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
            for (var i = 0; i < entries.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                zip.CreateEntryFromFile(entries[i].File, entries[i].Entry, System.IO.Compression.CompressionLevel.Optimal);
                context.Report((i + 1) / (double)entries.Count);
            }
        }
        catch
        {
            TryDelete(target);
            throw;
        }
        return OperationResult.Of(target);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Actions wheel for pictures: reads QR codes (and barcodes) and copies what they say to the clipboard.</summary>
public sealed class ReadQrOperation : IOperation
{
    public string Id => "app.read-qr";
    public WheelKind Wheel => WheelKind.Actions;
    public string TitleKey => "op.app.read-qr";
    public string? Badge => null;
    public string Glyph => "\uED14";
    public int Order => 800;

    public bool Accepts(InputSet input) => input.IsSingle && input.AllAre(FileCategory.Image);

    public Task<OperationResult> RunAsync(OperationContext context, CancellationToken cancellation) =>
        Task.Run(() =>
        {
            var path = context.Input.Paths[0];
            if (!File.Exists(path)) throw new OperationException("error.file-missing", Path.GetFileName(path));
            IReadOnlyList<string> found;
            try { found = Mo.Engine.Images.QrCodes.Read(path); }
            catch (ImageMagick.MagickException) { throw new OperationException("error.image-unreadable", Path.GetFileName(path)); }
            if (found.Count == 0) throw new OperationException("error.qr-none", Path.GetFileName(path));

            var text = string.Join(Environment.NewLine, found);
            Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(text));
            var shown = found[0].Length > 80 ? found[0][..77] + "\u2026" : found[0];
            return OperationResult.Message("result.qr-copied", shown);
        }, cancellation);
}