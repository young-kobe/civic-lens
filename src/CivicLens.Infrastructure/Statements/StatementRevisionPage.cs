using CivicLens.Core.Documents;
using CivicLens.Core.Statements;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Statements.Persistence;

namespace CivicLens.Infrastructure.Statements;

/// <summary>Statement revision rows with their child rows. A page costs a fixed number of queries.</summary>
internal sealed record StatementRevisionPage(IReadOnlyList<StatementRevisionRow> Rows,
    ILookup<(string CandidateId, int RevisionNumber), StatementRevisionSpeakerRow> Speakers,
    ILookup<(string CandidateId, int RevisionNumber), StatementRevisionOtherSpeakerRow> OtherSpeakers,
    ILookup<(string CandidateId, int RevisionNumber), StatementRevisionIssueRow> Issues,
    ILookup<(string CandidateId, int RevisionNumber), StatementRevisionPolicyAreaRow> PolicyAreas,
    ILookup<(string CandidateId, int RevisionNumber), StatementRevisionQuotationRow> Quotations,
    ILookup<(string CandidateId, int RevisionNumber), StatementRevisionBillNumberRow> BillNumbers) : IRevisionRowPage<StatementRevisionRow>
{
    public static readonly StatementRevisionPage Empty = new([], Lookup<StatementRevisionSpeakerRow>(), Lookup<StatementRevisionOtherSpeakerRow>(),
        Lookup<StatementRevisionIssueRow>(), Lookup<StatementRevisionPolicyAreaRow>(), Lookup<StatementRevisionQuotationRow>(),
        Lookup<StatementRevisionBillNumberRow>());

    /// <summary>Rebuilds the revision of a row. A row with null columns fails here, and the JSON is no fallback.</summary>
    public StatementDraftRevision Rebuild(StatementRevisionRow row)
    {
        if (row.Headline is null)
            throw StatementRevisionRows.NotBackfilled($"Statement revision {row.CandidateId} {row.RevisionNumber}");
        var key = (row.CandidateId, row.RevisionNumber);
        return new(row.CandidateId, row.RevisionNumber, row.AuthorSubject, new DateTimeOffset(row.CreatedAtUtcTicks, TimeSpan.Zero),
            row.Headline, row.Summary!, row.StatementDate, EvidenceOf(row),
            [.. Speakers[key].OrderBy(speaker => speaker.Position).Select(speaker => speaker.OfficialId)],
            [.. OtherSpeakers[key].OrderBy(speaker => speaker.Position).Select(speaker => speaker.SpeakerName)],
            [.. Issues[key].OrderBy(issue => issue.Position).Select(issue => issue.IssueId)],
            [.. PolicyAreas[key].OrderBy(area => area.Position).Select(area => area.PolicyAreaId)],
            [.. Quotations[key].OrderBy(quotation => quotation.Position).Select(quotation => new StatementQuotation(
                new(quotation.QuotationExtractionId, quotation.QuotationStart, quotation.QuotationLength), quotation.Kind))],
            [.. BillNumbers[key].OrderBy(bill => bill.Position).Select(bill => bill.BillNumber)]);
    }

    public IReadOnlyList<StoredRevision<StatementRevisionRow, StatementDraftRevision>> ToStoredRevisions() =>
        [.. Rows.Select(row => new StoredRevision<StatementRevisionRow, StatementDraftRevision>(row, () => Rebuild(row)))];

    private static SourceSpan? EvidenceOf(StatementRevisionRow row) => row.StatementDateEvidenceExtractionId is null ? null
        : new(row.StatementDateEvidenceExtractionId, row.StatementDateEvidenceStart!.Value, row.StatementDateEvidenceLength!.Value);

    private static ILookup<(string CandidateId, int RevisionNumber), TChild> Lookup<TChild>() where TChild : IStatementRevisionChildRow =>
        Array.Empty<TChild>().ToLookup(child => (child.CandidateId, child.RevisionNumber));
}
