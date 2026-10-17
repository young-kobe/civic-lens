using System.Collections.Immutable;
using CivicLens.Core.Statements;

namespace CivicLens.Application.Relationships;

public sealed record ApprovedStatement(StatementDraftRevision Revision, string ArticleText)
{
    public string RecordId => Revision.CandidateId;

    public ImmutableArray<StatementPassage> Passages() =>
        [.. Revision.Quotations.Select(quotation => new StatementPassage(quotation.Span, ArticleText.Substring(quotation.Span.Start, quotation.Span.Length)))];
}
