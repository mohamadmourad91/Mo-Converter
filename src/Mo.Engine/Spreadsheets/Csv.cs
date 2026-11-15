using System.Text;

namespace Mo.Engine.Spreadsheets;

/// <summary>CSV as Excel writes and reads it: quotes around fields with separators, quotes or line breaks.</summary>
public static class Csv
{
    private static readonly char[] Candidates = [';', ',', '\t', '|'];

    /// <summary>
    /// The separator that splits the first lines into the same number of fields most consistently
    /// (German Excel writes ';', English Excel ',').
    /// </summary>
    public static char DetectSeparator(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0).Take(20).ToList();
        if (lines.Count == 0) return ',';
        var best = ',';
        var bestScore = 0.0;
        foreach (var candidate in Candidates)
        {
            var counts = lines.Select(l => Parse(l, candidate).FirstOrDefault()?.Count ?? 1).ToList();
            var fields = counts.Max();
            if (fields < 2) continue;
            // How many lines have the most common field count, weighted by the number of fields.
            var mode = counts.GroupBy(c => c).OrderByDescending(g => g.Count()).First();
            var score = mode.Count() / (double)counts.Count * Math.Log(mode.Key + 1);
            if (score > bestScore) { bestScore = score; best = candidate; }
        }
        return best;
    }

    public static List<List<string>> Parse(string text, char separator)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"' && field.Length == 0) quoted = true;
            else if (c == separator) { row.Add(field.ToString()); field.Clear(); }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = [];
            }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        // Drop trailing empty lines.
        while (rows.Count > 0 && rows[^1].All(string.IsNullOrEmpty)) rows.RemoveAt(rows.Count - 1);
        return rows;
    }

    public static string Write(IEnumerable<IReadOnlyList<string>> rows, char separator)
    {
        var text = new StringBuilder();
        foreach (var row in rows)
        {
            text.AppendJoin(separator, row.Select(f => Quote(f, separator)));
            text.Append("\r\n");
        }
        return text.ToString();
    }

    private static string Quote(string field, char separator) =>
        field.IndexOfAny([separator, '"', '\n', '\r']) >= 0 || field.StartsWith(' ') || field.EndsWith(' ')
            ? "\"" + field.Replace("\"", "\"\"") + "\""
            : field;

    /// <summary>The list separator of the user's region (';' in Germany, ',' in the US), like Excel uses.</summary>
    public static char RegionalSeparator()
    {
        var separator = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ListSeparator;
        return separator.Length == 1 ? separator[0] : ',';
    }
}
