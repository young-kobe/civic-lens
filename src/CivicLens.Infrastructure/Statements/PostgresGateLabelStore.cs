using CivicLens.Application.Statements;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Statements.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Statements;

public sealed class PostgresGateLabelStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IGateLabelStore
{
    public async Task AddAsync(GateLabelEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.AuthorKind == ReviewAuthorKind.Analysis && GateLabelSplit.IsHeldOut(entry.CandidateId))
            throw new ArgumentException("A held-out release takes no model label.", nameof(entry));
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var extractionId = await db.Set<StatementCandidateRow>().Where(row => row.CandidateId == entry.CandidateId)
            .Select(row => row.ExtractionId).SingleOrDefaultAsync(cancellationToken)
            ?? throw new ArgumentException("A label needs an existing candidate.", nameof(entry));
        if (extractionId != entry.ExtractionId)
            throw new ArgumentException("A label must cite the extraction of its candidate.", nameof(entry));
        db.Add(new StatementGateLabelRow
        {
            LabelId = Guid.NewGuid().ToString("N"),
            CandidateId = entry.CandidateId,
            ExtractionId = entry.ExtractionId,
            AuthorSubject = entry.AuthorSubject,
            AuthorKind = entry.AuthorKind,
            RunId = entry.RunId,
            LabelJson = ReviewJson.Serialize(entry.Label),
            CreatedAtUtcTicks = entry.CreatedAtUtc.UtcTicks
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<GateLabelQueue> ListUnlabeledAsync(int limit, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var unlabeled = await (from candidate in db.Set<StatementCandidateRow>().AsNoTracking()
                               where !db.Set<StatementGateLabelRow>()
                                   .Any(label => label.CandidateId == candidate.CandidateId && label.AuthorKind == ReviewAuthorKind.Person)
                               orderby candidate.CreatedAtUtcTicks, candidate.CandidateId
                               select new { candidate.CandidateId, candidate.SourceId, candidate.Url }).ToArrayAsync(cancellationToken);
        var items = unlabeled.Select(row => new GateLabelQueueItem(row.CandidateId, row.SourceId, row.Url, GateLabelSplit.IsHeldOut(row.CandidateId)));
        return new([.. items.OrderByDescending(item => item.HeldOut).Take(limit)], unlabeled.Length);
    }

    public async Task<GateLabelTask?> GetAsync(string candidateId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var candidate = await db.Set<StatementCandidateRow>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.CandidateId == candidateId, cancellationToken);
        if (candidate is null) return null;
        var article = await StatementAnalysisListing.ReadArticleAsync(db, candidateId, cancellationToken)
            ?? throw new InvalidOperationException("The extraction of a statement candidate is missing.");
        var labels = await db.Set<StatementGateLabelRow>().AsNoTracking().Where(row => row.CandidateId == candidateId)
            .OrderByDescending(row => row.CreatedAtUtcTicks).ThenByDescending(row => row.LabelId).ToArrayAsync(cancellationToken);
        return new(candidateId, candidate.ExtractionId, candidate.Url, article.Extraction.Text,
            GateLabelSplit.IsHeldOut(candidateId), NewestOf(labels, ReviewAuthorKind.Person), NewestOf(labels, ReviewAuthorKind.Analysis));
    }

    public async Task<IReadOnlyList<GateLabelExportRow>> ListLabelsOfRecordAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var human = await db.Set<StatementGateLabelRow>().AsNoTracking().Where(row => row.AuthorKind == ReviewAuthorKind.Person)
            .OrderByDescending(row => row.CreatedAtUtcTicks).ThenByDescending(row => row.LabelId).ToArrayAsync(cancellationToken);
        var recorded = human.GroupBy(row => row.CandidateId).Select(group => group.First()).OrderBy(row => row.CandidateId, StringComparer.Ordinal).ToArray();
        var extractions = await PostgresDocumentExtractionStore.ReadManyAsync(db, [.. recorded.Select(row => row.ExtractionId)], cancellationToken);
        return [.. recorded.Select(row => ToExportRow(row, extractions[row.ExtractionId].Text))];
    }

    private static GateLabelExportRow ToExportRow(StatementGateLabelRow row, string text)
    {
        var label = ReviewJson.Deserialize<GateLabel>(row.LabelJson, "gate label");
        return new(row.CandidateId, row.ExtractionId, GateLabelSplit.IsHeldOut(row.CandidateId), label.IssueIds, label.NamesBillOrVote, text);
    }

    private static GateLabel? NewestOf(StatementGateLabelRow[] newestFirst, ReviewAuthorKind kind) =>
        newestFirst.FirstOrDefault(row => row.AuthorKind == kind) is { } row ? ReviewJson.Deserialize<GateLabel>(row.LabelJson, "gate label") : null;
}
