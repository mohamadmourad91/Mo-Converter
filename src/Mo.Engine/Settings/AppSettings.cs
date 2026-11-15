using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mo.Engine;

public enum ModifierKey { Shift, Ctrl, Alt }
public enum ThemeChoice { System, Light, Dark }
public enum CompressionLevel { Light, Balanced, Strong }

/// <summary>User preferences, stored as JSON in %AppData%\Mo Converter\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>"en", "de", "ar", or null to follow Windows.</summary>
    public string? Language { get; set; }

    public ModifierKey ConvertKey { get; set; } = ModifierKey.Shift;
    public ModifierKey ToolsKey { get; set; } = ModifierKey.Ctrl;
    public ModifierKey ActionsKey { get; set; } = ModifierKey.Alt;

    /// <summary>Null writes results next to the original file.</summary>
    public string? OutputFolder { get; set; }

    public bool ResultsToShelf { get; set; }
    public bool PlaySounds { get; set; } = true;
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;
    public CompressionLevel ImageCompression { get; set; } = CompressionLevel.Balanced;
    public CompressionLevel VideoCompression { get; set; } = CompressionLevel.Balanced;
    public bool WelcomeShown { get; set; }

    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mo Converter");

    public static string FilePath => Path.Combine(Folder, "settings.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load(string? path = null)
    {
        try
        {
            path ??= FilePath;
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new AppSettings();
        }
        catch (JsonException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (NotSupportedException) { }
        return new AppSettings();
    }

    public void Save(string? path = null)
    {
        path ??= FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }
}
