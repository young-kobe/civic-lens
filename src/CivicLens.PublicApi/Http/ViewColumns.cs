using System.Data.Common;
using System.Globalization;

namespace CivicLens.PublicApi.Http;

/// <summary>Reads the column types that the <c>public_api</c> views use.</summary>
internal static class ViewColumns
{
    public static string? Text(DbDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    // The counts are bigint in some views and integer in others.
    public static int? Number(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    public static DateOnly? Date(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateOnly>(ordinal);

    public static T? NullableEnum<T>(DbDataReader reader, int ordinal) where T : struct, Enum =>
        reader.IsDBNull(ordinal) ? null : Enum<T>(reader, ordinal);

    /// <summary>A time that a view stores as UTC ticks.</summary>
    public static DateTimeOffset? Utc(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : new DateTimeOffset(reader.GetInt64(ordinal), TimeSpan.Zero);

    public static T Enum<T>(DbDataReader reader, int ordinal) where T : struct, Enum => Enum<T>(reader.GetString(ordinal));

    /// <summary>An enum name that a view holds, such as one element of an array column.</summary>
    public static T Enum<T>(string name) where T : struct, Enum =>
        System.Enum.TryParse<T>(name, out var value) && System.Enum.IsDefined(value)
            ? value
            : throw new InvalidDataException($"The view holds an unknown {typeof(T).Name}.");

    /// <summary>Reads the last column of a list read, which counts every row of its view in the same query, so the total and the page share one snapshot.
    /// The officials list is the precedent. A filter never changes the total.</summary>
    public static int Total(DbDataReader reader) => checked((int)reader.GetInt64(reader.FieldCount - 1));
}
