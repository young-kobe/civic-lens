using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed record PublishedRecordReference(PublishedRecordKind Kind, string RecordId)
{
    private const char Separator = ':';

    public static string KindNames { get; } = string.Join(", ", Enum.GetValues<PublishedRecordKind>().Select(NameOf));

    public static PublishedRecordReference Parse(string? text)
    {
        var separator = text?.IndexOf(Separator) ?? -1;
        if (separator < 1 || separator == text!.Length - 1)
            throw new ArgumentException("Name each record as <kind>:<id>.", nameof(text));
        var kindName = text[..separator];
        if (!TryParseKind(kindName, out var kind))
            throw new ArgumentException($"Unknown record kind '{kindName}'. Use one of: {KindNames}.", nameof(text));
        return new PublishedRecordReference(kind, text[(separator + 1)..]);
    }

    public override string ToString() => NameOf(Kind) + Separator + RecordId;

    private static bool TryParseKind(string name, out PublishedRecordKind kind)
    {
        foreach (var candidate in Enum.GetValues<PublishedRecordKind>())
        {
            if (NameOf(candidate) != name) continue;
            kind = candidate;
            return true;
        }
        kind = default;
        return false;
    }

    public static string NameOf(PublishedRecordKind kind) => JsonNamingPolicy.CamelCase.ConvertName(kind.ToString());
}
