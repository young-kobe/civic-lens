using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Votes;

namespace CivicLens.Infrastructure.Publication;

public sealed partial class PostgresPublicationStore
{
    public async Task<VotePartyTallyPage> ReadVotePartyTallyEvidenceAsync(VotePartyTallyVerificationCursor? after, int limit, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await BeginVerificationReadAsync(db, cancellationToken);
        var page = await VotePartyTallyRows.ReadEvidenceAsync(db, after, limit, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return page;
    }
}
