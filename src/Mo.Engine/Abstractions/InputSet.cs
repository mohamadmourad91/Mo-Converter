namespace Mo.Engine;

/// <summary>The files the user is dragging, classified once so operations can test them cheaply.</summary>
public sealed class InputSet
{
    public InputSet(IEnumerable<string> paths)
    {
        Paths = paths.ToArray();
        Categories = Paths.Select(FileTypes.Classify).ToArray();
        Extensions = Paths.Select(FileTypes.ExtensionOf).ToHashSet();
    }

    public IReadOnlyList<string> Paths { get; }
    public IReadOnlyList<FileCategory> Categories { get; }

    /// <summary>Distinct lower-case extensions without the dot.</summary>
    public IReadOnlySet<string> Extensions { get; }

    public int Count => Paths.Count;
    public bool IsSingle => Paths.Count == 1;

    public bool AllAre(FileCategory category) => Count > 0 && Categories.All(c => c == category);

    /// <summary>The one category every file shares, or null for an empty or mixed selection.</summary>
    public FileCategory? CommonCategory =>
        Count > 0 && Categories.All(c => c == Categories[0]) ? Categories[0] : null;

    /// <summary>True when every file already has this extension, so converting to it would do nothing.</summary>
    public bool AllHaveExtension(params string[] extensions) =>
        Count > 0 && Extensions.All(e => extensions.Contains(e, StringComparer.OrdinalIgnoreCase));
}
