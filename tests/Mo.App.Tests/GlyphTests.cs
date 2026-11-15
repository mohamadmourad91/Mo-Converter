using System.Windows;
using System.Windows.Media;
using Mo.App.Services.Demo;
using Mo.Engine;

namespace Mo.App.Tests;

public class GlyphTests
{
    /// <summary>Every operation icon must exist in the Windows icon font, otherwise the wheel shows an empty box.</summary>
    [Fact]
    public void Every_operation_icon_exists_in_the_icon_font()
    {
        var fonts = new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" }
            .Select(name => new Typeface(new FontFamily(name), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal))
            .Select(face => face.TryGetGlyphTypeface(out var glyphs) ? glyphs : null)
            .Where(g => g is not null)
            .ToList();
        Assert.NotEmpty(fonts);

        var registry = OperationRegistry.CreateDefault();
        DemoOperations.Register(registry);
        var samples = new[]
        {
            new InputSet(["a.png"]), new InputSet(["a.jpg", "b.png"]), new InputSet(["x.txt"]),
            new InputSet(["a.pdf"]), new InputSet(["a.pdf", "b.pdf"]),
            new InputSet(["a.mp4"]), new InputSet(["a.mp4", "b.mov"]), new InputSet(["a.mp3"]), new InputSet(["a.wav", "b.mp3"]),
            new InputSet(["a.docx"]), new InputSet(["a.md"]), new InputSet(["a.csv"]), new InputSet(["a.xlsx"]), new InputSet(["a.pptx"]),
            new InputSet(["a.7z"]), new InputSet(["a.srt"]),
        };
        var operations = Enum.GetValues<WheelKind>()
            .SelectMany(w => samples.SelectMany(s => registry.Match(w, s)))
            .DistinctBy(o => o.Id)
            .ToList();

        var missing = operations
            .Where(o => !fonts.Any(f => o.Glyph.Length == 1 && f!.CharacterToGlyphMap.ContainsKey(o.Glyph[0])))
            .Select(o => $"{o.Id} ({string.Join(" ", o.Glyph.Select(c => ((int)c).ToString("X4")))})")
            .ToList();
        Assert.Empty(missing);
    }
}
