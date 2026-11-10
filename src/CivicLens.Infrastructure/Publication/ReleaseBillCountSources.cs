using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Actions;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Publication;

/// <summary>Reads what the tables hold for the bill counts of one release, so a caller can compare it with the files of the release.</summary>
internal static class ReleaseBillCountSources
{
    private const string ReadVotes = """
        SELECT s.record_id AS "RecordId", s.revision_number AS "RevisionNumber", v.congress AS "Congress"
          FROM publication_record_states s
          JOIN vote_revisions v ON v.record_id = s.record_id AND v.revision_number = s.revision_number
         WHERE s.kind = 'vote' AND s.first_release <= {0} AND (s.last_release IS NULL OR s.last_release >= {0})
        """;

    private const string ReadOfficials = """
        SELECT official_id AS "Value" FROM release_officials WHERE release_number = {0}
        """;

    public static async Task<ReleaseBillCountSource> ReadAsync(CollectionAttemptDbContext db, int releaseNumber, CancellationToken cancellationToken)
    {
        var members = await ReadMembersAsync(db, releaseNumber, cancellationToken);
        var actions = new List<BillCountActionProjection>();
        foreach (var batch in members.Where(member => member.Kind == PublishedRecordKind.Action).Chunk(ReleaseBillCounts.ValidationBatchSize))
            actions.AddRange(await ActionRevisionRows.ValidateBillCountBindingsAsync(db, batch, cancellationToken));
        var votes = await db.Database.SqlQueryRaw<VoteRow>(ReadVotes, releaseNumber).ToArrayAsync(cancellationToken);
        var officials = await db.Database.SqlQueryRaw<string>(ReadOfficials, releaseNumber).ToArrayAsync(cancellationToken);
        return new(members, actions, [.. votes.Select(vote => new BillCountVoteProjection(vote.RecordId, vote.RevisionNumber, vote.Congress))], officials);
    }

    public static async Task RequireSameMembersAsync(CollectionAttemptDbContext db, int releaseNumber,
        IReadOnlyCollection<PublishedRevisionBinding> validated, CancellationToken cancellationToken)
    {
        var current = await ReadMembersAsync(db, releaseNumber, cancellationToken);
        var expected = validated.ToHashSet();
        if (current.Count != expected.Count || !expected.SetEquals(current))
            throw new InvalidDataException("The action and vote revisions of the release changed after its files were checked.");
    }

    private static async Task<IReadOnlyList<PublishedRevisionBinding>> ReadMembersAsync(CollectionAttemptDbContext db, int releaseNumber,
        CancellationToken cancellationToken)
    {
        var action = PublishedRecordReference.NameOf(PublishedRecordKind.Action);
        var vote = PublishedRecordReference.NameOf(PublishedRecordKind.Vote);
        var rows = await db.Set<PublicationRecordStateRow>().AsNoTracking()
            .Where(row => (row.Kind == action || row.Kind == vote) && row.FirstRelease <= releaseNumber &&
                (row.LastRelease == null || row.LastRelease >= releaseNumber))
            .OrderBy(row => row.Kind).ThenBy(row => row.RecordId).ToArrayAsync(cancellationToken);
        return [.. rows.Select(row => new PublishedRevisionBinding(PublishedEntries.KindOf(row.Kind), row.RecordId, row.RevisionNumber))];
    }

    private sealed class VoteRow
    {
        public string RecordId { get; set; } = "";
        public int RevisionNumber { get; set; }
        public int Congress { get; set; }
    }
}
