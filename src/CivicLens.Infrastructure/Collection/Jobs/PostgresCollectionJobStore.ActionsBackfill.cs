using CivicLens.Application.Collection.Bills;
using CivicLens.Infrastructure.Collection.BillStream;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CivicLens.Infrastructure.Collection.Jobs;

public sealed partial class PostgresCollectionJobStore : IBillActionsBackfillStore
{
    private const string ActionsBackfillSavepoint = "bill_actions_backfill";

    public async Task<IReadOnlyList<string>> ListBillsWithUnverifiedActionsAsync(string? after, int limit,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Set<BillPageRow>().AsNoTracking()
            .Where(page => page.Role == BillPageRole.Actions && page.ImportedCount == null &&
                (after == null || page.DetailJobId.CompareTo(after) > 0))
            .Select(page => page.DetailJobId).Distinct().OrderBy(id => id).Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BillActionsBackfillPage>> ListActionPagesAsync(string detailJobId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detailJobId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await (from page in db.Set<BillPageRow>().AsNoTracking()
                      join gate in db.Set<BillGateRow>().AsNoTracking() on page.DetailJobId equals gate.DetailJobId
                      where page.DetailJobId == detailJobId && page.Role == BillPageRole.Actions
                      orderby page.PageOrder
                      select new BillActionsBackfillPage(page.JobId, page.PageOrder, page.ExtractionId, page.ClosesChain,
                          gate.Congress, gate.BillType, gate.BillNumber, gate.SourceId)).Take(101)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> RecordVerifiedActionChainAsync(string detailJobId, IReadOnlyList<VerifiedBillActionsPage> pages,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detailJobId);
        ArgumentNullException.ThrowIfNull(pages);
        return TransactionAsync(async (db, _) =>
        {
            var transaction = db.Database.CurrentTransaction
                ?? throw new InvalidOperationException("Bill history recovery requires its admission transaction.");
            await transaction.CreateSavepointAsync(ActionsBackfillSavepoint, cancellationToken);
            try
            {
                var pageRows = await db.Set<BillPageRow>().Where(page => page.DetailJobId == detailJobId &&
                    page.Role == BillPageRole.Actions).OrderBy(page => page.PageOrder).Take(101).ToListAsync(cancellationToken);
                var gate = await db.Set<BillGateRow>().SingleAsync(row => row.DetailJobId == detailJobId, cancellationToken);
                var pageJobIds = pageRows.Select(row => row.JobId).ToArray();
                var followOns = await db.Set<BillFollowOnJobRow>().AsNoTracking()
                    .Where(row => pageJobIds.Contains(row.JobId)).Select(row => row.DetailJobId)
                    .ToListAsync(cancellationToken);
                if (followOns.Count != pageRows.Count || followOns.Any(root => root != detailJobId))
                    throw new InvalidDataException("The actions pages do not belong to the selected bill's follow-on chain.");
                if (pageRows.Count != pages.Count || pageRows.Count is 0 or > 100)
                    throw new InvalidDataException("The saved actions chain changed or exceeds its recovery bound.");

                var existingActions = await db.Set<BillActionRow>().AsNoTracking()
                    .Where(row => row.DetailJobId == detailJobId).OrderBy(row => row.Position).ToListAsync(cancellationToken);
                var existingVotes = await db.Set<BillActionRecordedVoteRow>().AsNoTracking()
                    .Where(row => row.DetailJobId == detailJobId).OrderBy(row => row.ActionPosition).ThenBy(row => row.Position)
                    .ToListAsync(cancellationToken);

                var expected = new List<(BillActionRow Action, List<BillActionRecordedVoteRow> Votes)>();
                var counts = new int[pages.Count];
                var position = 0;
                for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
                {
                    var pageRow = pageRows[pageIndex];
                    var verifiedPage = pages[pageIndex];
                    var page = verifiedPage.Page;
                    var identity = verifiedPage.Identity;
                    if (identity.JobId != pageRow.JobId || identity.PageOrder != pageRow.PageOrder ||
                        identity.ExtractionId != pageRow.ExtractionId || identity.ClosesChain != pageRow.ClosesChain ||
                        identity.Congress != gate.Congress || identity.BillType != gate.BillType || identity.BillNumber != gate.BillNumber ||
                        identity.SourceId != gate.SourceId ||
                        pageRow.PageOrder != pageIndex || pageRow.ClosesChain != (page.Next is null))
                        throw new InvalidDataException("The verified actions pages do not match the saved page chain.");
                    counts[pageIndex] = page.Actions.Count;
                    if (pageRow.ImportedCount is { } count && count != page.Actions.Count)
                        throw new InvalidDataException("A saved actions import count conflicts with its verified capture.");
                    foreach (var action in page.Actions)
                    {
                        var actionRow = NewActionRow(detailJobId, pageRow.JobId, position, action);
                        var voteRows = action.RecordedVotes.Select((vote, voteOrder) =>
                            NewRecordedVoteRow(detailJobId, position, voteOrder, vote)).ToList();
                        expected.Add((actionRow, voteRows));
                        position++;
                    }
                }

                VerifySemantics(existingActions, existingVotes, expected,
                    pageRows.ToDictionary(row => row.JobId, row => row.ImportedCount, StringComparer.Ordinal));
                var positionsMatch = ExistingPositionsMatch(existingActions, existingVotes, expected);
                if (!positionsMatch)
                {
                    db.RemoveRange(await db.Set<BillActionRecordedVoteRow>().Where(row => row.DetailJobId == detailJobId)
                        .ToListAsync(cancellationToken));
                    db.RemoveRange(await db.Set<BillActionRow>().Where(row => row.DetailJobId == detailJobId)
                        .ToListAsync(cancellationToken));
                    await db.SaveChangesAsync(cancellationToken);
                    foreach (var item in expected)
                    {
                        db.Add(item.Action);
                        db.AddRange(item.Votes);
                    }
                }
                for (var index = 0; index < pageRows.Count; index++) pageRows[index].ImportedCount = counts[index];
                await db.SaveChangesAsync(cancellationToken);

                var savedActions = await db.Set<BillActionRow>().AsNoTracking().Where(row => row.DetailJobId == detailJobId)
                    .OrderBy(row => row.Position).ToListAsync(cancellationToken);
                var savedVotes = await db.Set<BillActionRecordedVoteRow>().AsNoTracking()
                    .Where(row => row.DetailJobId == detailJobId).OrderBy(row => row.ActionPosition).ThenBy(row => row.Position)
                    .ToListAsync(cancellationToken);
                var savedMarkers = await db.Set<BillPageRow>().AsNoTracking().Where(row => row.DetailJobId == detailJobId &&
                        row.Role == BillPageRole.Actions).OrderBy(row => row.PageOrder)
                    .Select(row => row.ImportedCount).ToListAsync(cancellationToken);
                if (!savedMarkers.SequenceEqual(counts.Select(count => (int?)count)))
                    throw new InvalidDataException("The recovered actions chain failed its import marker proof.");
                VerifySemantics(savedActions, savedVotes, expected,
                    pageRows.Select((row, index) => (row.JobId, ImportedCount: savedMarkers[index]))
                        .ToDictionary(row => row.JobId, row => row.ImportedCount, StringComparer.Ordinal));
                if (!ExistingPositionsMatch(savedActions, savedVotes, expected))
                    throw new InvalidDataException("The recovered actions chain failed its readback proof.");
                await transaction.ReleaseSavepointAsync(ActionsBackfillSavepoint, cancellationToken);
                return true;
            }
            catch (Exception exception) when (exception is InvalidDataException or DbUpdateException)
            {
                await transaction.RollbackToSavepointAsync(ActionsBackfillSavepoint, cancellationToken);
                db.ChangeTracker.Clear();
                if (exception is InvalidDataException) throw;
                throw new InvalidDataException("The verified actions chain conflicts with stored action history.", exception);
            }
        }, cancellationToken);
    }

    private static void VerifySemantics(IReadOnlyList<BillActionRow> actualActions,
        IReadOnlyList<BillActionRecordedVoteRow> actualVotes,
        IReadOnlyList<(BillActionRow Action, List<BillActionRecordedVoteRow> Votes)> expected,
        IReadOnlyDictionary<string, int?> importedCounts)
    {
        var checkedActions = 0;
        var checkedVotes = 0;
        var actionsByJob = actualActions.ToLookup(row => row.JobId, StringComparer.Ordinal);
        var votesByAction = actualVotes.ToLookup(row => row.ActionPosition);
        foreach (var page in expected.GroupBy(item => item.Action.JobId))
        {
            var pageActions = actionsByJob[page.Key].OrderBy(row => row.Position).ToArray();
            var expectedPage = page.ToArray();
            if (pageActions.Length == 0 && importedCounts[page.Key] is null) continue;
            if (pageActions.Length != expectedPage.Length)
                throw new InvalidDataException("Stored action rows conflict with the verified page count.");
            for (var index = 0; index < expectedPage.Length; index++)
            {
                var actual = pageActions[index];
                var wanted = expectedPage[index];
                if (!SameAction(actual, wanted.Action))
                    throw new InvalidDataException("Stored action values conflict with the verified captures.");
                var actualActionVotes = votesByAction[actual.Position]
                    .OrderBy(row => row.Position).ToArray();
                if (actualActionVotes.Length != wanted.Votes.Count)
                    throw new InvalidDataException("Stored recorded vote counts conflict with the verified captures.");
                for (var voteIndex = 0; voteIndex < actualActionVotes.Length; voteIndex++)
                    if (!SameVote(actualActionVotes[voteIndex], wanted.Votes[voteIndex]))
                        throw new InvalidDataException("Stored recorded vote values conflict with the verified captures.");
                checkedActions++;
                checkedVotes += actualActionVotes.Length;
            }
        }
        if (checkedActions != actualActions.Count || checkedVotes != actualVotes.Count)
            throw new InvalidDataException("Stored action history includes rows outside the verified pages.");
    }

    private static bool ExistingPositionsMatch(IReadOnlyList<BillActionRow> actions,
        IReadOnlyList<BillActionRecordedVoteRow> votes,
        IReadOnlyList<(BillActionRow Action, List<BillActionRecordedVoteRow> Votes)> expected)
    {
        var expectedActions = expected.Select(item => (item.Action.JobId, item.Action.Position)).OrderBy(item => item.Position);
        var actualActions = actions.Select(row => (row.JobId, row.Position)).OrderBy(item => item.Position);
        if (!actualActions.SequenceEqual(expectedActions)) return false;

        var actualOwners = actions.ToDictionary(row => row.Position, row => row.JobId);
        var expectedOwners = expected.ToDictionary(item => item.Action.Position, item => item.Action.JobId);
        var expectedVotes = expected.SelectMany(item => item.Votes)
            .Select(row => (JobId: row.DetailJobId, ActionJobId: expectedOwners[row.ActionPosition],
                row.ActionPosition, row.Position)).OrderBy(item => item.ActionJobId).ThenBy(item => item.ActionPosition).ThenBy(item => item.Position);
        var actualVotePositions = votes.Select(row => (JobId: row.DetailJobId, ActionJobId: actualOwners[row.ActionPosition],
                row.ActionPosition, row.Position))
            .OrderBy(item => item.ActionJobId).ThenBy(item => item.ActionPosition).ThenBy(item => item.Position);
        return actualVotePositions.SequenceEqual(expectedVotes);
    }

    private static bool SameAction(BillActionRow left, BillActionRow right) =>
        left.ActionDate == right.ActionDate && left.ActionTime == right.ActionTime && left.Text == right.Text &&
        left.Type == right.Type && left.ActionCode == right.ActionCode && left.SourceSystemCode == right.SourceSystemCode &&
        left.SourceSystemName == right.SourceSystemName;

    private static bool SameVote(BillActionRecordedVoteRow left, BillActionRecordedVoteRow right) =>
        left.Chamber == right.Chamber && left.Congress == right.Congress && left.SessionNumber == right.SessionNumber &&
        left.RollNumber == right.RollNumber && left.VotedAtUtcTicks == right.VotedAtUtcTicks && left.Url == right.Url;
}
