namespace Mo.Engine;

/// <summary>Decides where results are written and never overwrites an existing file.</summary>
public sealed class OutputPlanner
{
    private readonly string? _folder;
    private readonly HashSet<string> _reserved = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    /// <param name="folder">Fixed output folder, or null to write next to each source file.</param>
    public OutputPlanner(string? folder = null) => _folder = string.IsNullOrWhiteSpace(folder) ? null : folder;

    /// <summary>
    /// A free path for a result derived from <paramref name="source"/>: same name, new extension,
    /// optional suffix ("photo-compressed.jpg"), and " (2)", " (3)"… when the name is taken.
    /// </summary>
    public string PathFor(string source, string extension, string? suffix = null)
    {
        var folder = _folder ?? Path.GetDirectoryName(Path.GetFullPath(source))!;
        Directory.CreateDirectory(folder);
        var stem = Path.GetFileNameWithoutExtension(source.TrimEnd(Path.DirectorySeparatorChar)) + (suffix is null ? "" : "-" + suffix);
        var ext = extension.Length == 0 ? "" : "." + extension.TrimStart('.');

        lock (_gate)
        {
            for (var n = 1; ; n++)
            {
                var candidate = Path.Combine(folder, n == 1 ? stem + ext : $"{stem} ({n}){ext}");
                if (File.Exists(candidate) || Directory.Exists(candidate) || !_reserved.Add(candidate)) continue;
                return candidate;
            }
        }
    }

    /// <summary>A free folder path, for operations that produce many files (PDF pages, video frames).</summary>
    public string FolderFor(string source, string? suffix = null)
    {
        var path = PathFor(source, "", suffix);
        Directory.CreateDirectory(path);
        return path;
    }
}
