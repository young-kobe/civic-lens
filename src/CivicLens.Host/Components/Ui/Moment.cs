using System.Globalization;

namespace CivicLens.Host.Components.Ui;

/// <summary>The one owner of displayed times. Every displayed time is UTC.</summary>
public static class Moment
{
    public static string Short(DateTimeOffset value) => Format(value, "d MMM, HH:mm");

    public static string Day(DateTimeOffset value) => Format(value, "d MMM yyyy");

    public static string Day(DateOnly value) => value.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    public static string Full(DateTimeOffset value) => Format(value, "yyyy-MM-dd HH:mm 'UTC'");

    public static string Iso(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Machine(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static string Format(DateTimeOffset value, string pattern) =>
        value.UtcDateTime.ToString(pattern, CultureInfo.InvariantCulture);
}
