namespace Mo.Engine;

/// <summary>Broad family a file belongs to. Operations declare which families they accept.</summary>
public enum FileCategory
{
    Image, Pdf, Video, Audio, Document, Spreadsheet, Presentation, Subtitle, Archive, Folder, Other,
}

public static class FileTypes
{
    private static readonly Dictionary<string, FileCategory> ByExtension = Build();

    private static Dictionary<string, FileCategory> Build()
    {
        var map = new Dictionary<string, FileCategory>(StringComparer.OrdinalIgnoreCase);
        void Add(FileCategory category, params string[] extensions)
        {
            foreach (var extension in extensions) map[extension] = category;
        }

        Add(FileCategory.Image, "png", "jpg", "jpeg", "webp", "avif", "tif", "tiff", "gif", "bmp", "ico", "heic", "heif");
        Add(FileCategory.Pdf, "pdf");
        Add(FileCategory.Video, "mp4", "mov", "mkv", "webm", "avi", "m4v", "wmv");
        Add(FileCategory.Audio, "mp3", "wav", "m4a", "aac", "flac", "ogg", "opus", "wma");
        Add(FileCategory.Document, "docx", "doc", "rtf", "odt", "txt", "md", "markdown", "html", "htm", "epub");
        Add(FileCategory.Spreadsheet, "xlsx", "xls", "csv", "tsv", "ods");
        Add(FileCategory.Presentation, "pptx", "ppt", "odp");
        Add(FileCategory.Subtitle, "srt", "vtt", "ass", "ssa", "sbv");
        Add(FileCategory.Archive, "zip", "tar", "gz", "tgz", "7z", "rar");
        return map;
    }

    /// <summary>Lower-case extension without the dot; empty when there is none.</summary>
    public static string ExtensionOf(string path) => Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

    public static FileCategory Classify(string path)
    {
        if (Directory.Exists(path)) return FileCategory.Folder;
        return ByExtension.TryGetValue(ExtensionOf(path), out var category) ? category : FileCategory.Other;
    }
}
