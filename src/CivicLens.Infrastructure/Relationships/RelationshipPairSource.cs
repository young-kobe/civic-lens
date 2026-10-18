using System.Collections.Immutable;
using CivicLens.Application.Relationships;
using CivicLens.Core.Relationships;
using CivicLens.Infrastructure.Collection;

namespace CivicLens.Infrastructure.Relationships;

internal static class RelationshipPairSource
{
    public static async Task<ImmutableArray<RelationshipPair>> FindAsync(CollectionAttemptDbContext db, RelationshipRecordKind kind,
        string recordId, RelationshipRevisionScope scope, CancellationToken cancellationToken) => kind switch
        {
            RelationshipRecordKind.Statement => await FindOfStatementsAsync(
                await RelationshipEvidenceReader.ReadStatementsAsync(db, [recordId], scope, cancellationToken), db, scope, cancellationToken),
            RelationshipRecordKind.Action => await FindOfActionAsync(db, recordId, scope, cancellationToken),
            _ => await FindOfVoteAsync(db, recordId, scope, cancellationToken)
        };

    public static async Task<ImmutableArray<RelationshipPair>> FindAllAsync(CollectionAttemptDbContext db, CancellationToken cancellationToken) =>
        await FindOfStatementsAsync(await RelationshipEvidenceReader.ReadStatementsAsync(db, null, RelationshipRevisionScope.Approved, cancellationToken),
            db, RelationshipRevisionScope.Approved, cancellationToken);

    private static async Task<ImmutableArray<RelationshipPair>> FindOfStatementsAsync(ImmutableArray<ApprovedStatement> statements,
        CollectionAttemptDbContext db, RelationshipRevisionScope scope, CancellationToken cancellationToken)
    {
        if (statements.IsEmpty) return [];
        var officialIds = statements.SelectMany(statement => statement.Revision.SpeakerOfficialIds).Distinct(StringComparer.Ordinal).ToArray();
        var actions = await RelationshipEvidenceReader.ReadActionsSponsoredByAsync(db, officialIds, scope, cancellationToken);
        var votes = await RelationshipEvidenceReader.ReadVotesOnAsync(db, RelationshipPairFinder.BillsMentionedIn(statements), scope, cancellationToken);
        return RelationshipPairFinder.Find(statements, actions, votes);
    }

    private static async Task<ImmutableArray<RelationshipPair>> FindOfActionAsync(CollectionAttemptDbContext db, string recordId,
        RelationshipRevisionScope scope, CancellationToken cancellationToken)
    {
        if (await RelationshipEvidenceReader.ReadActionAsync(db, recordId, scope, cancellationToken) is not { } action) return [];
        var officialIds = action.Sponsorships.Select(item => item.PersonId).Distinct(StringComparer.Ordinal).ToArray();
        return RelationshipPairFinder.Find(await RelationshipEvidenceReader.ReadStatementsOfOfficialsAsync(db, officialIds, scope, cancellationToken),
            [action], []);
    }

    private static async Task<ImmutableArray<RelationshipPair>> FindOfVoteAsync(CollectionAttemptDbContext db, string recordId,
        RelationshipRevisionScope scope, CancellationToken cancellationToken)
    {
        if (await RelationshipEvidenceReader.ReadVoteAsync(db, recordId, scope, cancellationToken) is not { } vote) return [];
        var officialIds = vote.Members.Select(item => item.PersonId).Distinct(StringComparer.Ordinal).ToArray();
        return RelationshipPairFinder.Find(
            await RelationshipEvidenceReader.ReadDatedStatementsOfOfficialsAsync(db, officialIds, vote.Congress, scope, cancellationToken), [], [vote]);
    }
}
