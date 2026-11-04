using CivicLens.Application.Publication;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Statements.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Statements;

/// <summary>Owns the mapping between a statement revision and its row and child rows, in both directions.
/// The row also keeps the JSON, but readers use the columns.</summary>
internal static class StatementRevisionRows
{
    private const string SpeakersTable = "statement_revision_speakers";
    private const string OtherSpeakersTable = "statement_revision_other_speakers";
    private const string IssuesTable = "statement_revision_issues";
    private const string PolicyAreasTable = "statement_revision_policy_areas";
    private const string QuotationsTable = "statement_revision_quotations";
    private const string BillNumbersTable = "statement_revision_bill_numbers";
    private const string IdColumn = "candidate_id";

    public static StatementRevisionRow Create(StatementDraftRevision revision, string? runId)
    {
        var row = new StatementRevisionRow
        {
            CandidateId = revision.CandidateId,
            RevisionNumber = revision.RevisionNumber,
            AuthorSubject = revision.AuthorSubject,
            AuthorKind = revision.AuthorKind,
            RunId = runId,
            RevisionJson = ReviewJson.Serialize(revision),
            CreatedAtUtcTicks = revision.CreatedAtUtc.UtcTicks
        };
        FillColumns(row, revision);
        return row;
    }

    /// <summary>Sets every column and child row of the revision. It leaves the key, the author, the run, and the JSON as they are.</summary>
    public static void FillColumns(StatementRevisionRow row, StatementDraftRevision revision)
    {
        row.Headline = revision.Headline;
        row.Summary = revision.Summary;
        row.StatementDate = revision.StatementDate;
        row.StatementDateEvidenceExtractionId = revision.StatementDateEvidence?.ExtractionId;
        row.StatementDateEvidenceStart = revision.StatementDateEvidence?.Start;
        row.StatementDateEvidenceLength = revision.StatementDateEvidence?.Length;
        row.Speakers = [.. revision.SpeakerOfficialIds.Select((officialId, position) => new StatementRevisionSpeakerRow
            { CandidateId = revision.CandidateId, RevisionNumber = revision.RevisionNumber, OfficialId = officialId, Position = (short)position })];
        row.OtherSpeakers = [.. revision.OtherSpeakers.Select((name, position) => new StatementRevisionOtherSpeakerRow
            { CandidateId = revision.CandidateId, RevisionNumber = revision.RevisionNumber, SpeakerName = name, Position = (short)position })];
        row.Issues = [.. revision.IssueIds.Select((issueId, position) => new StatementRevisionIssueRow
            { CandidateId = revision.CandidateId, RevisionNumber = revision.RevisionNumber, IssueId = issueId, Position = (short)position })];
        row.PolicyAreas = [.. revision.PolicyAreaIds.Select((policyAreaId, position) => new StatementRevisionPolicyAreaRow
            { CandidateId = revision.CandidateId, RevisionNumber = revision.RevisionNumber, PolicyAreaId = policyAreaId, Position = (short)position })];
        row.Quotations = [.. revision.Quotations.Select((quotation, position) => QuotationRow(revision, quotation, position))];
        row.BillNumbers = [.. revision.BillNumbers.Select((billNumber, position) => new StatementRevisionBillNumberRow
            { CandidateId = revision.CandidateId, RevisionNumber = revision.RevisionNumber, BillNumber = billNumber, Position = (short)position })];
    }

    /// <summary>Reads the rows of a query, then their child rows, in a fixed number of queries. The child rows come by the exact keys of the
    /// parent rows. The child rows of a revision with columns never change, so a write that commits between the queries cannot drop its children.</summary>
    public static async Task<StatementRevisionPage> ReadPageAsync(CollectionAttemptDbContext db, IQueryable<StatementRevisionRow> query,
        CancellationToken cancellationToken)
    {
        var rows = await query.ToListAsync(cancellationToken);
        if (rows.Count == 0) return StatementRevisionPage.Empty;
        var candidateIds = rows.Select(row => row.CandidateId).ToArray();
        var numbers = rows.Select(row => row.RevisionNumber).ToArray();
        return new(rows,
            await ReadChildrenAsync<StatementRevisionSpeakerRow>(db, SpeakersTable, candidateIds, numbers, cancellationToken),
            await ReadChildrenAsync<StatementRevisionOtherSpeakerRow>(db, OtherSpeakersTable, candidateIds, numbers, cancellationToken),
            await ReadChildrenAsync<StatementRevisionIssueRow>(db, IssuesTable, candidateIds, numbers, cancellationToken),
            await ReadChildrenAsync<StatementRevisionPolicyAreaRow>(db, PolicyAreasTable, candidateIds, numbers, cancellationToken),
            await ReadChildrenAsync<StatementRevisionQuotationRow>(db, QuotationsTable, candidateIds, numbers, cancellationToken),
            await ReadChildrenAsync<StatementRevisionBillNumberRow>(db, BillNumbersTable, candidateIds, numbers, cancellationToken));
    }

    /// <summary>The failure of a reader that meets a statement revision row with null columns. Only the backfill fixes such a row.</summary>
    public static InvalidDataException NotBackfilled(string revision) =>
        new($"{revision} has null columns. Run the statement revision backfill.");

    /// <summary>The reader that the shared review code uses for statements.</summary>
    public static async Task<IReadOnlyList<StoredRevision<StatementRevisionRow, StatementDraftRevision>>> ReadAsync(
        CollectionAttemptDbContext db, IQueryable<StatementRevisionRow> query, CancellationToken cancellationToken) =>
        (await ReadPageAsync(db, query, cancellationToken)).ToStoredRevisions();

    /// <summary>The rows of the named revisions that are stored. A revision that is not stored is absent.</summary>
    public static IQueryable<StatementRevisionRow> NamedRows(CollectionAttemptDbContext db, IReadOnlyCollection<PublishedRevisionBinding> named)
    {
        CitedRevisions.RequireKind(PublishedRecordKind.Statement, [.. named], nameof(named));
        return db.Set<StatementRevisionRow>().FromSqlRaw("""
            SELECT r.* FROM statement_revisions r JOIN unnest({0}, {1}) AS k(id, number) ON r.candidate_id = k.id AND r.revision_number = k.number
            """, named.Select(binding => binding.RecordId).ToArray(), named.Select(binding => binding.RevisionNumber).ToArray()).AsNoTracking();
    }

    /// <summary>Reads the named revisions. It fails when one is not stored.</summary>
    public static async Task<IReadOnlyList<StatementDraftRevision>> ReadNamedAsync(CollectionAttemptDbContext db,
        IReadOnlyCollection<PublishedRevisionBinding> named, CancellationToken cancellationToken)
    {
        var requested = named.Distinct().ToArray();
        if (requested.Length == 0) return [];
        var revisions = (await ReadAsync(db, NamedRows(db, requested), cancellationToken)).Select(stored => stored.Read()).ToArray();
        return revisions.Length == requested.Length ? revisions : throw CitedRevisions.NotStored(PublishedRecordKind.Statement);
    }

    private static async Task<ILookup<(string CandidateId, int RevisionNumber), TChild>> ReadChildrenAsync<TChild>(CollectionAttemptDbContext db,
        string table, string[] candidateIds, int[] numbers, CancellationToken cancellationToken)
        where TChild : class, IStatementRevisionChildRow =>
        (await db.Set<TChild>().FromSqlRaw(ReviewQueries.RevisionChildren(table, IdColumn), candidateIds, numbers).AsNoTracking()
            .ToListAsync(cancellationToken)).ToLookup(child => (child.CandidateId, child.RevisionNumber));

    private static StatementRevisionQuotationRow QuotationRow(StatementDraftRevision revision, StatementQuotation quotation, int position) => new()
    {
        CandidateId = revision.CandidateId,
        RevisionNumber = revision.RevisionNumber,
        Position = (short)position,
        QuotationExtractionId = quotation.Span.ExtractionId,
        QuotationStart = quotation.Span.Start,
        QuotationLength = quotation.Span.Length,
        Kind = quotation.Kind
    };
}
