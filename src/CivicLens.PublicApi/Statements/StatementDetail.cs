using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Statements;

/// <summary><c>SpeakerTerms</c> pairs with the speakers by index. <c>ApprovedAtUtc</c> is the stored approval time of the published revision, null until the entries backfill fills it.</summary>
public sealed record StatementDetail(int Release, StatementRow Statement, PublishedApprover ApprovedBy, PublishedDocumentVersion Source, string? ArticleUrl,
    PublishedCitation? StatementDateEvidence, PublishedQuotation[] Quotations)
{
    public required StatementSpeakerTerm[] SpeakerTerms { get; init; }
    public DateTimeOffset? ApprovedAtUtc { get; init; }
    public DateTimeOffset? FirstPublishedAtUtc { get; init; }
}
