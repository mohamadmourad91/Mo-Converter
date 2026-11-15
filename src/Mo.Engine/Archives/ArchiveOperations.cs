using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Readers;
using Mo.Engine.Docs;

namespace Mo.Engine.Archives;

/// <summary>
/// Safe extraction of ZIP, 7z, RAR, TAR and TAR.GZ. Entries that would land outside the target folder are refused
/// ("zip slip"), files are never overwritten, and extraction stops before the disk runs full.
/// </summary>
public static class ArchiveExtractor
{
    /// <summary>Space that must stay free on the disk while extracting.</summary>
    private const long Reserve = 512L * 1024 * 1024;

    public static IReadOnlyList<string> Extract(string archive, string destination, CancellationToken cancellation)
    {
        var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);
        var written = new List<string>();
        var drive = new DriveInfo(Path.GetPathRoot(root)!);
        try
        {
            var extension = FileTypes.ExtensionOf(archive);
            if (extension is "gz" or "tgz" or "tar")
            {
                // Streaming reader: understands a TAR inside GZIP as one archive.
                using var stream = File.OpenRead(archive);
                using var reader = ReaderFactory.OpenReader(stream, new ReaderOptions());
                while (reader.MoveToNextEntry())
                {
                    cancellation.ThrowIfCancellationRequested();
                    var entry = reader.Entry;
                    if (entry.IsDirectory) continue;
                    if (entry.IsEncrypted) throw new OperationException("error.archive-protected", Path.GetFileName(archive));
                    var name = entry.Key ?? Path.GetFileNameWithoutExtension(archive);
                    using var input = reader.OpenEntryStream();
                    Write(input, name, root, drive, written, archive);
                }
            }
            else
            {
                using var opened = ArchiveFactory.OpenArchive(archive, new ReaderOptions());
                foreach (var entry in opened.Entries)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (entry.IsDirectory) continue;
                    if (entry.IsEncrypted) throw new OperationException("error.archive-protected", Path.GetFileName(archive));
                    using var input = entry.OpenEntryStream();
                    Write(input, entry.Key ?? "file", root, drive, written, archive);
                }
            }
            if (written.Count == 0) throw new OperationException("error.archive-empty", Path.GetFileName(archive));
            return written;
        }
        catch (Exception e) when (e is not OperationException and not OperationCanceledException)
        {
            Cleanup(written, root);
            var protectedArchive = e.Message.Contains("password", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase);
            throw new OperationException(protectedArchive ? "error.archive-protected" : "error.archive-unreadable", Path.GetFileName(archive));
        }
        catch
        {
            Cleanup(written, root);
            throw;
        }
    }

    private static void Write(Stream input, string entryName, string root, DriveInfo drive, List<string> written, string archive)
    {
        var relative = entryName.Replace('\\', '/').TrimStart('/');
        var target = Path.GetFullPath(Path.Combine(root, relative));
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || relative.Length == 0)
            throw new OperationException("error.archive-unsafe", Path.GetFileName(archive), entryName);

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target))
        {
            // Two entries with the same name (case differences): keep both.
            var stem = Path.Combine(Path.GetDirectoryName(target)!, Path.GetFileNameWithoutExtension(target));
            var n = 2;
            while (File.Exists($"{stem} ({n}){Path.GetExtension(target)}")) n++;
            target = $"{stem} ({n}){Path.GetExtension(target)}";
        }

        using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
        written.Add(target);
        var buffer = new byte[81920];
        int read;
        long sinceCheck = 0;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, read);
            sinceCheck += read;
            if (sinceCheck > 64L * 1024 * 1024)
            {
                sinceCheck = 0;
                if (drive.AvailableFreeSpace < Reserve) throw new OperationException("error.archive-too-large", Path.GetFileName(archive));
            }
        }
    }

    private static void Cleanup(List<string> written, string root)
    {
        foreach (var file in written) DocumentConvertOperation.TryDelete(file);
        try { if (Directory.Exists(root) && !Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Any(File.Exists)) Directory.Delete(root, true); }
        catch (IOException) { }
    }

    /// <summary>"backup.tar.gz" → "backup"; "photos.zip" → "photos".</summary>
    public static string BaseName(string archive)
    {
        var name = Path.GetFileName(archive);
        foreach (var suffix in new[] { ".tar.gz", ".tar.bz2", ".tar.xz", ".tgz", ".zip", ".7z", ".rar", ".tar", ".gz" })
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return name[..^suffix.Length];
        return Path.GetFileNameWithoutExtension(name);
    }
}

/// <summary>Tools wheel: unpacks archives into a folder with the archive's name.</summary>
internal sealed class ExtractArchiveOperation : IOperation
{
    public string Id => "archive.extract";
    public WheelKind Wheel => WheelKind.Tools;
    public string TitleKey => "op." + Id;
    public string? Badge => null;
    public string Glyph => "\uE8B7";
    public int Order => 10;

    public bool Accepts(InputSet input) => input.AllAre(FileCategory.Archive);

    public Task<OperationResult> RunAsync(OperationContext context, CancellationToken cancellation) =>
        Task.Run(() =>
        {
            var folders = new List<string>();
            var paths = context.Input.Paths;
            for (var i = 0; i < paths.Count; i++)
            {
                if (!File.Exists(paths[i])) throw new OperationException("error.file-missing", Path.GetFileName(paths[i]));
                context.Report((double)i / paths.Count, Path.GetFileName(paths[i]));
                var named = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(paths[i]))!, ArchiveExtractor.BaseName(paths[i]));
                var folder = context.Output.FolderFor(named);
                ArchiveExtractor.Extract(paths[i], folder, cancellation);
                folders.Add(folder);
            }
            context.Report(1);
            return OperationResult.Of([.. folders]);
        }, cancellation);
}

/// <summary>Convert wheel: 7z, RAR, TAR and TAR.GZ repacked as ZIP, which every computer can open.</summary>
internal sealed class ArchiveToZipOperation : IOperation
{
    public string Id => "archive.to-zip";
    public WheelKind Wheel => WheelKind.Convert;
    public string TitleKey => "op." + Id;
    public string? Badge => "ZIP";
    public string Glyph => "\uF012";
    public int Order => 10;

    public bool Accepts(InputSet input) => input.AllAre(FileCategory.Archive) && !input.AllHaveExtension("zip");

    public Task<OperationResult> RunAsync(OperationContext context, CancellationToken cancellation) =>
        Task.Run(() =>
        {
            var outputs = new List<string>();
            foreach (var source in context.Input.Paths)
            {
                if (!File.Exists(source)) throw new OperationException("error.file-missing", Path.GetFileName(source));
                var named = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source))!, ArchiveExtractor.BaseName(source));
                var target = context.Output.PathFor(named, "zip");
                var temp = Directory.CreateTempSubdirectory("mo-archive-").FullName;
                try
                {
                    ArchiveExtractor.Extract(source, temp, cancellation);
                    ZipFile.CreateFromDirectory(temp, target, System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: false);
                    outputs.Add(target);
                }
                catch
                {
                    DocumentConvertOperation.TryDelete(target);
                    throw;
                }
                finally
                {
                    try { Directory.Delete(temp, true); } catch (IOException) { }
                }
            }
            return OperationResult.Of([.. outputs]);
        }, cancellation);
}
