using System.Collections.Immutable;
using CivicLens.Application.Votes;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.HouseVotes;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Votes.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Votes;

internal static class BillVoteDrafts
{
    public static async Task<ImmutableArray<BillVoteDraft>> ReadAsync(CollectionAttemptDbContext db, int congress, string billType,
        int billNumber, CancellationToken cancellationToken)
    {
        var rows = await (from draft in db.Set<VoteDraftRow>().AsNoTracking()
                          where db.Set<HouseVoteCandidateRow>().Any(candidate => candidate.CandidateId == draft.CandidateId && candidate.Congress == congress &&
                              candidate.BillType == billType && candidate.BillNumber == billNumber)
                          join revision in db.Set<VoteRevisionRow>().AsNoTracking()
                              on new { draft.RecordId, Number = draft.CurrentRevisionNumber } equals new { revision.RecordId, Number = revision.RevisionNumber }
                          orderby draft.EventOn descending, draft.RecordId
                          select new { draft.RecordId, draft.SessionNumber, draft.RollCallNumber, draft.EventOn, draft.ReviewStatus, revision.RevisionJson })
            .Take(BillVoteDraft.MaximumPerBill).ToArrayAsync(cancellationToken);
        return [.. rows.Select(row => new BillVoteDraft(row.RecordId, row.SessionNumber, row.RollCallNumber,
            ReviewJson.Deserialize<VoteDraftRevision>(row.RevisionJson, "vote revision").Kind, row.EventOn, row.ReviewStatus))];
    }
}
