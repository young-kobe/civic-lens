using CivicLens.Publication.Contracts;
using Npgsql;

namespace CivicLens.PublicApi.Records;

/// <summary>Reads the text rewrite column of a correction view. The column holds the enum name, so an unknown name is invalid data.</summary>
internal static class TextRewriteColumn
{
    private static readonly Dictionary<string, PublishedTextRewrite> ByName =
        Enum.GetValues<PublishedTextRewrite>().ToDictionary(value => value.ToString(), StringComparer.Ordinal);

    public static PublishedTextRewrite Read(NpgsqlDataReader reader, int ordinal) =>
        ByName.TryGetValue(reader.GetString(ordinal), out var rewrite)
            ? rewrite
            : throw new InvalidDataException("A correction view holds an unknown text rewrite.");
}
