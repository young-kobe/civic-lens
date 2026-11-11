using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Statements;

public sealed record StatementDetail(int Release, StatementRow Statement, PublishedApprover ApprovedBy, PublishedDocumentVersion Source, string? ArticleUrl,
    PublishedCitation? StatementDateEvidence, PublishedQuotation[] Quotations);
