using System.Globalization;
using CivicLens.PublicApi.Http;

namespace CivicLens.PublicApi.Statements;

/// <summary>The date and record ID that order one statement's linked votes, carried with the release of the page.</summary>
public sealed record StatementLinkedVoteCursor(int Release, DateOnly VotedOn, string RecordId)
{
    private const string Tag = "sv";

    internal static StatementLinkedVoteCursor Of(int release, StatementLinkedVoteRow row) => new(release, row.VotedOn, row.VoteRecordId);

    public static StatementLinkedVoteCursor? Parse(string? value)
    {
        if (value is null) return null;
        var parts = CursorFields.Split(value, Tag, 3);
        return new(CursorFields.Release(parts[0]), CursorFields.Date(parts[1]), CursorFields.RecordId(parts[2]));
    }

    public string Encode() => string.Create(CultureInfo.InvariantCulture, $"{Tag}.{Release}.{CursorFields.Format(VotedOn)}.{RecordId}");
}
