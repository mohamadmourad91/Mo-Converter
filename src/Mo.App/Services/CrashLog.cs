namespace Mo.App.Services;

/// <summary>Appends unexpected exceptions to a small log next to the settings file. Never throws.</summary>
public static class CrashLog
{
    private const long MaxBytes = 512 * 1024;

    public static string FilePath { get; } = Path.Combine(Mo.Engine.AppSettings.Folder, "app-errors.log");

    public static void Write(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes) File.Delete(FilePath);
            File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
