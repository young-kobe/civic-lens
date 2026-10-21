using System.Globalization;
using System.Text.RegularExpressions;

namespace CivicLens.Application.Collection;

internal static partial class RenderedText
{
    private const string DateFormat = "yyyy-MM-dd";

    public static string Line(string label, string value) => $"{label}: {SingleLine(value)}".TrimEnd();

    public static void AddLine(List<string> lines, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) lines.Add(Line(label, value));
    }

    public static string SingleLine(string value) => LineBreaks().Replace(value, " ").Trim();

    public static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Date(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\r\n|[\r\n\v\f\u0085\u2028\u2029]")]
    private static partial Regex LineBreaks();
}
