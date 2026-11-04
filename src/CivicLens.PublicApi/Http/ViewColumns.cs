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

    public static T Enum<T>(DbDataReader reader, int ordinal) where T : struct, Enum =>
        System.Enum.TryParse<T>(reader.GetString(ordinal), out var value) && System.Enum.IsDefined(value)
            ? value
            : throw new InvalidDataException($"The view holds an unknown {typeof(T).Name}.");
}
