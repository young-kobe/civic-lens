using System.Collections.Immutable;
using CivicLens.Application;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Publication;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Actions.Persistence;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Actions;

internal static class ActionRevisionRows
{
    public static ActionRevisionRow Create(ActionDraftRevision revision, string? runId, CodeVersion codeVersion)
    {
        var authorKind = ReviewAuthor.KindOf(revision.AuthorSubject);
        var row = new ActionRevisionRow
        {
            RecordId = revision.RecordId,
            RevisionNumber = revision.RevisionNumber,
            AuthorSubject = revision.AuthorSubject,
            AuthorKind = authorKind,
            RunId = runId,
            CodeVersion = authorKind == ReviewAuthorKind.Code ? codeVersion.Sha : null,
            RevisionJson = ReviewJson.Serialize(revision),
            CreatedAtUtcTicks = revision.CreatedAtUtc.UtcTicks
        };
        FillColumns(row, revision);
        return row;
    }

    public static void FillColumns(ActionRevisionRow row, ActionDraftRevision revision)
    {
        ReviewTextRows.Fill(row, revision);
        row.SourceId = revision.SourceId;
        row.Congress = revision.Congress;
        row.BillType = revision.BillType;
        row.BillNumber = revision.BillNumber;
        row.CandidateId = revision.CandidateId;
        row.CandidateUpdateUtcTicks = revision.CandidateUpdateDate.UtcTicks;
        row.CandidateIncludesSummaries = revision.CandidateIncludesSummaries;
        row.CandidateIncludesSubjects = revision.CandidateIncludesSubjects;
        row.Fingerprint = revision.Fingerprint;
        row.SummaryVersion = revision.SummaryVersion;
        row.SummaryCitationExtractionId = revision.SummaryCitation?.ExtractionId;
        row.SummaryCitationStart = revision.SummaryCitation?.Start;
        row.SummaryCitationLength = revision.SummaryCitation?.Length;
        row.PolicyArea = revision.PolicyArea;
        row.PolicyAreaCitationExtractionId = revision.PolicyAreaCitation?.ExtractionId;
        row.PolicyAreaCitationStart = revision.PolicyAreaCitation?.Start;
        row.PolicyAreaCitationLength = revision.PolicyAreaCitation?.Length;
        row.PolicyAreaId = revision.PolicyAreaId;
        row.BillTitle = revision.BillTitle;
        row.BillCitationExtractionId = revision.BillCitation?.ExtractionId;
        row.BillCitationStart = revision.BillCitation?.Start;
        row.BillCitationLength = revision.BillCitation?.Length;
        row.TextPackageId = revision.TextPackageId;
        row.WhyItMatters = revision.WhyItMatters;
        row.IssueRevisionId = revision.IssueRevisionId;
        row.SubjectsHash = revision.SubjectsHash;
        row.IssueCandidatesRecorded = !revision.IssueCandidateIds.IsDefault;
        row.EventOn = revision.EventOn();
        row.CosponsorCount = revision.CosponsorCount();
        row.Issues = [.. IssueIds(revision).Select((issueId, position) => IssueRow(revision, issueId, position))];
        row.TextCitations = [.. revision.TextCitations.IsDefault ? [] : revision.TextCitations.Select((citation, position) => TextCitationRow(revision, citation, position))];
        row.Sponsorships = [.. revision.Sponsorships.Select((sponsorship, position) => SponsorshipRow(revision, sponsorship, position))];
    }

    public static void FillCrsSummary(ActionRevisionRow row, ActionDraftRevision revision,
        IReadOnlyDictionary<string, DocumentExtraction> extractions)
    {
        row.CrsSummaryText = CrsSummaryOf(revision, extractions);
        row.CrsSummaryRead = true;
    }

    private static string? CrsSummaryOf(ActionDraftRevision revision, IReadOnlyDictionary<string, DocumentExtraction> extractions)
    {
        if (revision.SummaryCitation is not { } citation || revision.SummaryVersion is not { } version ||
            !extractions.TryGetValue(citation.ExtractionId, out var extraction)) return null;
        DocumentTextSpan span;
        try { span = new(extraction, citation.Start, citation.Length); }
        catch (ArgumentException) { return null; }
        var end = span.Start + span.Length;
        var text = extraction.Text;
        if (span.Start > 0 && (span.Start < 2 || text.AsSpan(span.Start - 2, 2) is not "\n\n")) return null;
        if (end < text.Length && (text.Length - end < 2 || text.AsSpan(end, 2) is not "\n\n")) return null;
        return CongressBillRenderer.ReadSummaryBody(span.Quote, version);
    }

    /// <summary>Reads the rows of a query, then their issues, sponsorships, and citations, in a fixed number of queries. The child rows come
    /// by the exact keys of the parent rows. The child rows of a revision with columns never change, so a write that commits between the queries cannot drop its children.</summary>
    public static async Task<ActionRevisionPage> ReadPageAsync(CollectionAttemptDbContext db, IQueryable<ActionRevisionRow> query,
        CancellationToken cancellationToken)
    {
        var rows = await query.ToListAsync(cancellationToken);
        var recordIds = rows.Select(row => row.RecordId).ToArray();
        var numbers = rows.Select(row => row.RevisionNumber).ToArray();
        var issues = (await db.Set<ActionRevisionIssueRow>().FromSqlRaw(ReviewQueries.RevisionChildren("action_revision_issues"), recordIds, numbers)
            .AsNoTracking().ToListAsync(cancellationToken)).ToLookup(issue => (issue.RecordId, issue.RevisionNumber));
        var sponsorships = (await db.Set<ActionRevisionSponsorshipRow>().FromSqlRaw(ReviewQueries.RevisionChildren("action_revision_sponsorships"), recordIds, numbers)
            .AsNoTracking().ToListAsync(cancellationToken)).ToLookup(item => (item.RecordId, item.RevisionNumber));
        var citations = (await db.Set<ActionRevisionSponsorshipCitationRow>().FromSqlRaw(ReviewQueries.RevisionChildren("action_revision_sponsorship_citations"), recordIds, numbers)
            .AsNoTracking().ToListAsync(cancellationToken)).ToLookup(citation => (citation.RecordId, citation.RevisionNumber, citation.SponsorshipPosition));
        var textCitations = (await db.Set<ActionRevisionTextCitationRow>().FromSqlRaw(ReviewQueries.RevisionChildren("action_revision_text_citations"), recordIds, numbers)
            .AsNoTracking().ToListAsync(cancellationToken)).ToLookup(citation => (citation.RecordId, citation.RevisionNumber));
        return new(rows, issues, sponsorships, citations, textCitations);
    }

    /// <summary>The failure of a reader that meets an action revision row with null columns. Only the backfill fixes such a row.</summary>
    public static InvalidDataException NotBackfilled(string revision) =>
        new($"{revision} has null columns. Run the action revision backfill.");

    /// <summary>The reader that the shared review code uses for actions.</summary>
    public static async Task<IReadOnlyList<StoredRevision<ActionRevisionRow, ActionDraftRevision>>> ReadAsync(CollectionAttemptDbContext db,
        IQueryable<ActionRevisionRow> query, CancellationToken cancellationToken) =>
        (await ReadPageAsync(db, query, cancellationToken)).ToStoredRevisions();

    /// <summary>Reads the revisions of a query and builds each one. A row with null columns fails the read.</summary>
    public static async Task<ImmutableArray<ActionDraftRevision>> ReadDraftRevisionsAsync(CollectionAttemptDbContext db,
        IQueryable<ActionRevisionRow> query, CancellationToken cancellationToken) =>
        [.. (await ReadAsync(db, query, cancellationToken)).Select(stored => stored.Read())];

    /// <summary>Reads the named revisions. It fails when one is not stored.</summary>
    public static async Task<IReadOnlyList<ActionDraftRevision>> ReadNamedAsync(CollectionAttemptDbContext db,
        IReadOnlyCollection<PublishedRevisionBinding> named, CancellationToken cancellationToken)
    {
        var requested = named.Distinct().ToArray();
        if (requested.Length == 0) return [];
        CitedRevisions.RequireKind(PublishedRecordKind.Action, requested, nameof(named));
        var rows = db.Set<ActionRevisionRow>().FromSqlRaw("""
            SELECT r.* FROM action_revisions r JOIN unnest({0}, {1}) AS k(id, number) ON r.record_id = k.id AND r.revision_number = k.number
            """, requested.Select(binding => binding.RecordId).ToArray(), requested.Select(binding => binding.RevisionNumber).ToArray()).AsNoTracking();
        var revisions = await ReadDraftRevisionsAsync(db, rows, cancellationToken);
        return revisions.Length == requested.Length ? revisions : throw CitedRevisions.NotStored(PublishedRecordKind.Action);
    }

    /// <summary>Reads an exact bounded batch and validates the stored count projection against its rebuilt revision.</summary>
    public static async Task ValidateBillCountBindingsAsync(CollectionAttemptDbContext db,
        IReadOnlyCollection<PublishedRevisionBinding> named, CancellationToken cancellationToken)
    {
        var requested = named.Distinct().ToArray();
        if (requested.Length == 0) return;
        CitedRevisions.RequireKind(PublishedRecordKind.Action, requested, nameof(named));
        var rows = db.Set<ActionRevisionRow>().FromSqlRaw("""
            SELECT r.* FROM action_revisions r JOIN unnest({0}, {1}) AS k(id, number) ON r.record_id = k.id AND r.revision_number = k.number
            """, requested.Select(binding => binding.RecordId).ToArray(), requested.Select(binding => binding.RevisionNumber).ToArray()).AsNoTracking();
        var page = await ReadPageAsync(db, rows, cancellationToken);
        if (page.Rows.Count != requested.Length)
            throw CitedRevisions.NotStored(PublishedRecordKind.Action);
        foreach (var row in page.Rows)
        {
            ActionDraftRevision rebuilt;
            try
            {
                rebuilt = page.Rebuild(row);
                rebuilt.ValidateShape();
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidDataException or InvalidOperationException)
            {
                throw new InvalidOperationException($"Action revision {row.RecordId} {row.RevisionNumber} cannot produce a valid bill count projection.", exception);
            }
            if (row.EventOn != rebuilt.EventOn() || row.CosponsorCount != rebuilt.CosponsorCount())
                throw new InvalidOperationException($"Action revision {row.RecordId} {row.RevisionNumber} has a mismatched bill count projection.");
        }
    }

    private static IEnumerable<string> IssueIds(ActionDraftRevision revision) =>
        revision.IssueCandidateIds.IsDefault ? revision.TrackedIssueIds : revision.IssueCandidateIds;

    private static ActionRevisionIssueRow IssueRow(ActionDraftRevision revision, string issueId, int position) => new()
    {
        RecordId = revision.RecordId,
        RevisionNumber = revision.RevisionNumber,
        IssueId = issueId,
        Position = (short)position,
        IsTracked = revision.TrackedIssueIds.Contains(issueId)
    };

    private static ActionRevisionSponsorshipRow SponsorshipRow(ActionDraftRevision revision, ActionSponsorship sponsorship, int position) => new()
    {
        RecordId = revision.RecordId,
        RevisionNumber = revision.RevisionNumber,
        Position = (short)position,
        PersonId = sponsorship.PersonId,
        BioguideId = sponsorship.BioguideId,
        Role = sponsorship.Role,
        IsOriginal = sponsorship.IsOriginal,
        SponsoredOn = sponsorship.SponsoredOn,
        WithdrawnOn = sponsorship.WithdrawnOn,
        Citations = [.. sponsorship.Citations.Select((citation, citationPosition) => CitationRow(revision, (short)position, citation, citationPosition))]
    };

    private static ActionRevisionTextCitationRow TextCitationRow(ActionDraftRevision revision, ActionTextCitation citation, int position) => new()
    {
        RecordId = revision.RecordId,
        RevisionNumber = revision.RevisionNumber,
        Position = (short)position,
        Part = citation.Part,
        CitationExtractionId = citation.Span.ExtractionId,
        CitationStart = citation.Span.Start,
        CitationLength = citation.Span.Length,
        Sentence = citation.Sentence
    };

    private static ActionRevisionSponsorshipCitationRow CitationRow(ActionDraftRevision revision, short sponsorshipPosition, SourceSpan citation, int position) => new()
    {
        RecordId = revision.RecordId,
        RevisionNumber = revision.RevisionNumber,
        SponsorshipPosition = sponsorshipPosition,
        Position = (short)position,
        CitationExtractionId = citation.ExtractionId,
        CitationStart = citation.Start,
        CitationLength = citation.Length
    };
}
