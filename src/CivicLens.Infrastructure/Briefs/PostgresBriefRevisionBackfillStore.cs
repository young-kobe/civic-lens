using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Core.Briefs;
using CivicLens.Infrastructure.Briefs.Persistence;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Briefs;

public sealed class PostgresBriefRevisionBackfillStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IRevisionBackfillStore
{
    private const string ColumnsKey = "columns";
    private static readonly HashSet<string> NoAllowedAbsences = [];

    private readonly RevisionBackfill<BriefRevisionRow, BriefRevision, BriefRevisionPage> backfill = new(contextFactory, new(
        "brief_revision_backfill", PostgresAdvisoryLock.BriefReview, row => row.Headline == null, BriefRevisionRows.ReadPageAsync, FillColumnsAsync,
        ReadMovable, DifferingFields));

    public Task<RevisionBackfillBatch> BackfillBatchAsync(RevisionKey? after, int size, CancellationToken cancellationToken) =>
        backfill.BackfillBatchAsync(after, size, cancellationToken);

    public Task<RevisionVerification> VerifyAsync(int mismatchLimit, CancellationToken cancellationToken) =>
        backfill.VerifyAsync(mismatchLimit, cancellationToken);

    private static Task FillColumnsAsync(CollectionAttemptDbContext db, BriefRevisionRow row, BriefRevision revision, CancellationToken cancellationToken)
    {
        BriefRevisionRows.FillColumns(row, revision);
        return Task.CompletedTask;
    }

    private static (BriefRevision? Revision, string? Refusal) ReadMovable(BriefRevisionRow row)
    {
        BriefRevision revision;
        try
        {
            revision = ReviewJson.Deserialize<BriefRevision>(row.RevisionJson, "brief revision");
        }
        catch (Exception exception) when (ReviewJson.IsUnreadableStoredData(exception))
        {
            return (null, RevisionBackfillReasons.UnreadableJson);
        }
        if (HasNullElements(revision)) return (null, RevisionBackfillReasons.InvalidShape);
        try
        {
            revision.ValidateShape();
        }
        catch (ArgumentException)
        {
            return (null, RevisionBackfillReasons.InvalidShape);
        }
        return (revision, null);
    }

    private static bool HasNullElements(BriefRevision revision) =>
        HasNullContent(revision.Paragraphs, revision.Citations) || revision.Suggestion is { } suggestion && HasNullContent(suggestion.Paragraphs, suggestion.Citations);

    private static bool HasNullContent(ImmutableArray<BriefParagraph> paragraphs,
        ImmutableArray<BriefCitation> citations) =>
        !paragraphs.IsDefault && paragraphs.Any(paragraph => paragraph is null || paragraph.CitationIds.IsDefault) ||
        !citations.IsDefault && citations.Any(citation => citation is null);

    private static IReadOnlyList<string> DifferingFields(BriefRevisionRow row, BriefRevisionPage page)
    {
        BriefRevision rebuilt;
        try
        {
            rebuilt = page.Rebuild(row);
        }
        catch (InvalidDataException)
        {
            return [ColumnsKey];
        }
        if (RevisionJsonComparison.TryParseObject(row.RevisionJson) is not { } stored) return [RevisionJsonComparison.UnreadableKey];
        return RevisionJsonComparison.DifferingKeys(stored, ReviewJson.ToObject(rebuilt), NoAllowedAbsences);
    }
}
