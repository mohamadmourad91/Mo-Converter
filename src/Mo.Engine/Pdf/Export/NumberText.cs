using System.Globalization;
using System.Text.RegularExpressions;

namespace Mo.Engine.Pdf.Export;

/// <summary>
/// Recognises numbers written the German way (1.234,56) or the English way (1,234.56) so spreadsheet cells
/// become real numbers. Anything doubtful stays text: codes with leading zeros, dates, currencies, percentages.
/// </summary>
public static partial class NumberText
{
    [GeneratedRegex(@"^[+-]?(\d{1,3}([.,'\u00A0\u202F ]\d{3})+|\d+)([.,]\d+)?$")]
    private static partial Regex Shape();

    public static bool TryParse(string text, out double value) => TryParse(text, out value, out _);

    /// <summary>Also reports how the number was written: digits after the decimal mark and whether thousands were grouped.</summary>
    public static bool TryParse(string text, out double value, out (int Decimals, bool Grouped) shape)
    {
        value = 0;
        shape = default;
        var s = text.Trim().Replace('\u2212', '-'); // typographic minus
        if (s.Length == 0 || !Shape().IsMatch(s)) return false;

        var digits = s.TrimStart('+', '-');
        // "007", "01234": identifiers and postal codes, not quantities.
        if (digits.Length > 1 && digits[0] == '0' && char.IsDigit(digits[1])) return false;

        var lastDot = s.LastIndexOf('.');
        var lastComma = s.LastIndexOf(',');
        char? decimalMark = null;
        if (lastDot >= 0 && lastComma >= 0)
        {
            decimalMark = lastDot > lastComma ? '.' : ',';
        }
        else if (lastDot >= 0 || lastComma >= 0)
        {
            var mark = lastDot >= 0 ? '.' : ',';
            var occurrences = s.Count(c => c == mark);
            var after = s.Length - s.LastIndexOf(mark) - 1;
            var before = digits.IndexOf(mark);
            // One mark followed by exactly three digits after a short integer part ("1.234") is a thousands
            // separator; "0.125" and "3,5" are decimals.
            var isThousands = occurrences > 1 || (after == 3 && before is >= 1 and <= 3 && digits[0] != '0');
            if (!isThousands) decimalMark = mark;
        }

        var normal = new System.Text.StringBuilder();
        foreach (var c in s)
        {
            if (char.IsDigit(c) || c is '+' or '-') normal.Append(c);
            else if (c == decimalMark) normal.Append('.');
        }
        var decimals = decimalMark is { } m ? s.Length - s.LastIndexOf(m) - 1 : 0;
        var grouped = digits.Any(c => !char.IsDigit(c) && c != decimalMark);
        shape = (decimals, grouped);
        return double.TryParse(normal.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
