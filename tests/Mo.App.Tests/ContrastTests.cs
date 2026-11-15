using System.IO;
using System.Text.RegularExpressions;

namespace Mo.App.Tests;

/// <summary>
/// Every text colour must be readable on the surface it is drawn on, in both themes
/// (WCAG: 4.5:1 for normal text, 3:1 for large or decorative text such as the accent caption).
/// </summary>
public class ContrastTests
{
    private readonly record struct Rgba(double R, double G, double B, double A);

    private static string ThemeFolder
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            {
                var themes = Path.Combine(dir.FullName, "src", "Mo.App", "Themes");
                if (Directory.Exists(themes)) return themes;
            }
            throw new InvalidOperationException("Themes folder not found.");
        }
    }

    private static Dictionary<string, Rgba> Load(string file)
    {
        var text = File.ReadAllText(Path.Combine(ThemeFolder, file));
        var colors = Regex.Matches(text, @"x:Key=""([^""]+)""\s+Color=""#([0-9A-Fa-f]{6,8})""|<Color\s+x:Key=""([^""]+)"">#([0-9A-Fa-f]{6,8})</Color>")
            .ToDictionary(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[3].Value, m => Parse(m.Groups[1].Success ? m.Groups[2].Value : m.Groups[4].Value));
        // A brush may point at a named colour: Color="{StaticResource Mo.AccentColor}".
        foreach (Match m in Regex.Matches(text, @"x:Key=""([^""]+)""\s+Color=""\{StaticResource ([^}]+)\}"""))
            colors[m.Groups[1].Value] = colors[m.Groups[2].Value];
        return colors;
    }

    private static Rgba Parse(string hex)
    {
        if (hex.Length == 6) hex = "FF" + hex;
        int Part(int i) => Convert.ToInt32(hex.Substring(i, 2), 16);
        return new Rgba(Part(2), Part(4), Part(6), Part(0) / 255.0);
    }

    /// <summary>The colour that shows when <paramref name="top"/> is painted over an opaque <paramref name="below"/>.</summary>
    private static Rgba Over(Rgba top, Rgba below) => new(
        top.R * top.A + below.R * (1 - top.A), top.G * top.A + below.G * (1 - top.A), top.B * top.A + below.B * (1 - top.A), 1);

    private static double Luminance(Rgba c)
    {
        static double Channel(double v)
        {
            v /= 255;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static double Ratio(Rgba a, Rgba b)
    {
        var (hi, lo) = Luminance(a) > Luminance(b) ? (Luminance(a), Luminance(b)) : (Luminance(b), Luminance(a));
        return (hi + 0.05) / (lo + 0.05);
    }

    /// <summary>The Windows 11 window colour behind the cards, toasts and the wheel plate.</summary>
    private static Rgba WindowColor(string theme) => theme == "Light" ? new Rgba(0xF3, 0xF3, 0xF6, 1) : new Rgba(0x20, 0x20, 0x24, 1);

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Text_is_readable_on_every_surface(string theme)
    {
        var colors = Load($"{theme}.xaml");
        var window = WindowColor(theme);
        Rgba Solid(string key) => Over(colors[key], window);

        var cases = new (string Name, Rgba Text, Rgba Surface, double Minimum)[]
        {
            ("text on window", Solid("Mo.Text"), window, 4.5),
            ("subtle text on window", Solid("Mo.SubtleText"), window, 4.5),
            ("text on card", Solid("Mo.Text"), Over(colors["Mo.Card.Background"], window), 4.5),
            ("subtle text on card", Solid("Mo.SubtleText"), Over(colors["Mo.Card.Background"], window), 4.5),
            ("wheel text on petal", Solid("Mo.Wheel.Text"), Solid("Mo.Wheel.Petal"), 4.5),
            ("wheel subtle text on petal", Solid("Mo.Wheel.SubtleText"), Solid("Mo.Wheel.Petal"), 4.5),
            ("wheel text on hub", Solid("Mo.Wheel.Text"), Solid("Mo.Wheel.Hub"), 4.5),
            ("wheel subtle text on hub", Solid("Mo.Wheel.SubtleText"), Solid("Mo.Wheel.Hub"), 4.5),
            ("text on highlighted petal", Solid("Mo.AccentText"), Solid("Mo.Accent"), 4.5),
            ("accent caption on hub", Solid("Mo.Accent"), Solid("Mo.Wheel.Hub"), 3.0),
            ("toast text", Solid("Mo.Text"), Over(colors["Mo.Toast.Background"], window), 4.5),
            ("toast icon", Solid("Mo.Accent"), Over(colors["Mo.Toast.Background"], window), 3.0),
            ("accent on window (buttons, links)", Solid("Mo.Accent"), window, 3.0),
        };

        var failures = cases
            .Select(c => (c.Name, Ratio: Ratio(c.Text, c.Surface), c.Minimum))
            .Where(c => c.Ratio < c.Minimum)
            .Select(c => $"{theme}: {c.Name} is {c.Ratio:0.0}:1, needs {c.Minimum}:1")
            .ToList();
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void The_calculation_matches_known_values()
    {
        Assert.Equal(21.0, Ratio(new Rgba(0, 0, 0, 1), new Rgba(255, 255, 255, 1)), 1);
        Assert.Equal(1.0, Ratio(new Rgba(128, 128, 128, 1), new Rgba(128, 128, 128, 1)), 3);
        // #767676 on white is the classic "just passes AA" grey: 4.54:1.
        Assert.Equal(4.54, Ratio(new Rgba(0x76, 0x76, 0x76, 1), new Rgba(255, 255, 255, 1)), 2);
    }
}
