using System.Reflection;
using System.Text.Json;

namespace Mo.Engine;

/// <summary>
/// Key-based texts in English, German and Arabic. Any assembly can contribute by embedding
/// JSON files named "&lt;anything&gt;.&lt;lang&gt;.json" (flat object: key → text with {0} placeholders).
/// </summary>
public sealed class Localizer
{
    public static readonly IReadOnlyList<string> Languages = ["en", "de", "ar"];

    private readonly Dictionary<string, Dictionary<string, string>> _tables = new();

    public static Localizer Current { get; set; } = new("en");

    public Localizer(string language, params Assembly[] assemblies)
    {
        Language = Languages.Contains(language) ? language : "en";
        foreach (var lang in Languages) _tables[lang] = new Dictionary<string, string>();
        Load(typeof(Localizer).Assembly);
        foreach (var assembly in assemblies) Load(assembly);
    }

    public string Language { get; }
    public bool IsRightToLeft => Language == "ar";

    /// <summary>Picks "de" or "ar" from the Windows display language, otherwise "en".</summary>
    public static string SystemLanguage()
    {
        var two = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Languages.Contains(two) ? two : "en";
    }

    public void Load(Assembly assembly)
    {
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = name.Split('.');
            if (parts.Length < 3 || !_tables.TryGetValue(parts[^2], out var table)) continue;
            using var stream = assembly.GetManifestResourceStream(name)!;
            var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
            foreach (var (key, text) in entries) table[key] = text;
        }
    }

    /// <summary>Text for a key in the current language, falling back to English, then to the key itself.</summary>
    public string T(string key, params object?[] args)
    {
        var text = _tables[Language].TryGetValue(key, out var own) ? own
                 : _tables["en"].TryGetValue(key, out var english) ? english
                 : key;
        return args.Length == 0 ? text : string.Format(text, args);
    }

    /// <summary>
    /// Text for a count. Looks for "key.one", "key.two", "key.few", "key.many" or "key.other" first (whichever the language's
    /// rules pick for this number) and falls back to "key". {0} is the count; further arguments follow it.
    /// English and German tell 1 from the rest; Arabic has five forms (1, 2, 3-10, 11-99, 100+).
    /// </summary>
    public string N(string key, long count, params object?[] more)
    {
        var candidate = key + "." + Category(count);
        var args = new object?[] { count }.Concat(more).ToArray();
        return T(Has(candidate, Language) ? candidate : key, args);
    }

    internal string Category(long count)
    {
        if (Language != "ar") return count == 1 ? "one" : "other";
        var n = Math.Abs(count);
        var rest = n % 100;
        return n == 1 ? "one" : n == 2 ? "two" : rest is >= 3 and <= 10 ? "few" : rest is >= 11 and <= 99 ? "many" : "other";
    }

    public bool Has(string key, string language) => _tables.TryGetValue(language, out var table) && table.ContainsKey(key);

    public IEnumerable<string> Keys(string language) => _tables[language].Keys;
}
