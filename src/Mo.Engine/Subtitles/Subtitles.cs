using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Mo.Engine.Office;

namespace Mo.Engine.Subtitles;

/// <summary>One subtitle: shown from <see cref="Start"/> to <see cref="End"/>. Text uses \n for line breaks and &lt;i&gt;/&lt;b&gt; for style.</summary>
public sealed record Cue(TimeSpan Start, TimeSpan End, string Text);

/// <summary>Reads and writes SRT, WebVTT, ASS/SSA and YouTube SBV subtitles.</summary>
public static partial class SubtitleFormat
{
    [GeneratedRegex(@"(?:(\d+):)?(\d{1,2}):(\d{2})[.,](\d{1,3})")]
    private static partial Regex Time();

    [GeneratedRegex(@"\{[^}]*\}")]
    private static partial Regex AssTag();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTag();

    public static List<Cue> Parse(string text, string extension)
    {
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        return extension switch
        {
            "ass" or "ssa" => ParseAss(text),
            "sbv" => ParseBlocks(text, ','),
            _ => ParseBlocks(text, null), // SRT and WebVTT
        };
    }

    // ---------- reading ----------

    private static TimeSpan ParseTime(Match m)
    {
        var hours = m.Groups[1].Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        var fraction = m.Groups[4].Value;
        var millis = int.Parse(fraction, CultureInfo.InvariantCulture) * (int)Math.Pow(10, 3 - fraction.Length);
        return new TimeSpan(0, hours, int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture), millis);
    }

    /// <summary>Blank-line separated blocks with a time line ("a --> b", or "a,b" for SBV) followed by the text.</summary>
    private static List<Cue> ParseBlocks(string text, char? sbvSeparator)
    {
        var cues = new List<Cue>();
        foreach (var block in Regex.Split(text.Trim('\uFEFF', '\n', ' '), @"\n\s*\n"))
        {
            var lines = block.Split('\n').ToList();
            var timeLine = lines.FindIndex(l => sbvSeparator is null ? l.Contains("-->") : Time().Matches(l).Count == 2 && l.Contains(sbvSeparator.Value));
            if (timeLine < 0) continue; // header ("WEBVTT"), NOTE, STYLE, REGION blocks
            var times = Time().Matches(lines[timeLine]);
            if (times.Count < 2) continue;
            var body = string.Join("\n", lines.Skip(timeLine + 1)).Trim();
            if (body.Length > 0) cues.Add(new Cue(ParseTime(times[0]), ParseTime(times[1]), body));
        }
        return cues;
    }

    private static List<Cue> ParseAss(string text)
    {
        var cues = new List<Cue>();
        string[]? format = null;
        var inEvents = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('[')) { inEvents = line.Equals("[Events]", StringComparison.OrdinalIgnoreCase); continue; }
            if (!inEvents) continue;
            if (line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
            {
                format = line[7..].Split(',').Select(f => f.Trim()).ToArray();
                continue;
            }
            if (format is null || !line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase)) continue;

            // The text is the last field and may itself contain commas.
            var fields = line[9..].Split(',', format.Length);
            if (fields.Length < format.Length) continue;
            var start = Time().Match(fields[Array.FindIndex(format, f => f.Equals("Start", StringComparison.OrdinalIgnoreCase))]);
            var end = Time().Match(fields[Array.FindIndex(format, f => f.Equals("End", StringComparison.OrdinalIgnoreCase))]);
            if (!start.Success || !end.Success) continue;
            var body = fields[^1]
                .Replace("{\\i1}", "<i>").Replace("{\\i0}", "</i>").Replace("{\\b1}", "<b>").Replace("{\\b0}", "</b>");
            body = AssTag().Replace(body, "").Replace("\\N", "\n").Replace("\\n", "\n").Replace("\\h", " ").Trim();
            if (body.Length > 0) cues.Add(new Cue(ParseTime(start), ParseTime(end), body));
        }
        return cues.OrderBy(c => c.Start).ToList();
    }

    // ---------- writing ----------

    public static string Write(IReadOnlyList<Cue> cues, string extension) => extension switch
    {
        "srt" => Srt(cues),
        "vtt" => Vtt(cues),
        "ass" => Ass(cues),
        "txt" => PlainText(cues),
        var other => throw new NotSupportedException(other),
    };

    private static string Stamp(TimeSpan t, char decimalMark) =>
        $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}{decimalMark}{t.Milliseconds:000}";

    private static string Srt(IReadOnlyList<Cue> cues)
    {
        var text = new StringBuilder();
        for (var i = 0; i < cues.Count; i++)
            text.Append(i + 1).Append('\n')
                .Append(Stamp(cues[i].Start, ',')).Append(" --> ").Append(Stamp(cues[i].End, ',')).Append('\n')
                .Append(cues[i].Text).Append("\n\n");
        return text.ToString();
    }

    private static string Vtt(IReadOnlyList<Cue> cues)
    {
        var text = new StringBuilder("WEBVTT\n\n");
        foreach (var cue in cues)
        {
            // "&" must be escaped in WebVTT; <i> and <b> are allowed as they are.
            var body = cue.Text.Replace("&", "&amp;");
            text.Append(Stamp(cue.Start, '.')).Append(" --> ").Append(Stamp(cue.End, '.')).Append('\n').Append(body).Append("\n\n");
        }
        return text.ToString();
    }

    private static string Ass(IReadOnlyList<Cue> cues)
    {
        static string AssTime(TimeSpan t) => $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds / 10:00}";
        var text = new StringBuilder();
        text.Append("[Script Info]\nScriptType: v4.00+\nPlayResX: 1920\nPlayResY: 1080\nWrapStyle: 0\nScaledBorderAndShadow: yes\n\n");
        text.Append("[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n");
        text.Append("Style: Default,Arial,64,&H00FFFFFF,&H000000FF,&H00000000,&H64000000,0,0,0,0,100,100,0,0,1,3,1,2,60,60,50,1\n\n");
        text.Append("[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n");
        foreach (var cue in cues)
        {
            var body = cue.Text.Replace("<i>", "{\\i1}").Replace("</i>", "{\\i0}").Replace("<b>", "{\\b1}").Replace("</b>", "{\\b0}");
            body = HtmlTag().Replace(body, "").Replace("\n", "\\N");
            text.Append($"Dialogue: 0,{AssTime(cue.Start)},{AssTime(cue.End)},Default,,0,0,0,,{body}\n");
        }
        return text.ToString();
    }

    private static string PlainText(IReadOnlyList<Cue> cues) =>
        string.Join("\n\n", cues.Select(c => HtmlTag().Replace(c.Text, ""))) + "\n";
}

/// <summary>Convert wheel: subtitles into SRT, WebVTT, ASS or plain text (the spoken words only).</summary>
internal sealed class SubtitleConvertOperation(string extension, string badge, int order) : IOperation
{
    public string Id => "subtitle.to-" + extension;
    public WheelKind Wheel => WheelKind.Convert;
    public string TitleKey => "op." + Id;
    public string? Badge => badge;
    public string Glyph => "\uE7F0";
    public int Order => order;

    public bool Accepts(InputSet input) =>
        input.AllAre(FileCategory.Subtitle) && !input.AllHaveExtension(extension == "ass" ? ["ass", "ssa"] : [extension]);

    public Task<OperationResult> RunAsync(OperationContext context, CancellationToken cancellation) =>
        Task.Run(() =>
        {
            var outputs = new List<string>();
            foreach (var source in context.Input.Paths)
            {
                cancellation.ThrowIfCancellationRequested();
                if (!File.Exists(source)) throw new OperationException("error.file-missing", Path.GetFileName(source));
                var cues = SubtitleFormat.Parse(TextFiles.Read(source), FileTypes.ExtensionOf(source));
                if (cues.Count == 0) throw new OperationException("error.subtitle-empty", Path.GetFileName(source));
                var target = context.Output.PathFor(source, extension);
                TextFiles.Write(target, SubtitleFormat.Write(cues, extension));
                outputs.Add(target);
            }
            return OperationResult.Of([.. outputs]);
        }, cancellation);
}
