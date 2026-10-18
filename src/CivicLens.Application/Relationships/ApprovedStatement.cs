using System.Collections.Immutable;
using CivicLens.Core.Documents;
using CivicLens.Core.Statements;

namespace CivicLens.Application.Relationships;

public sealed record ApprovedStatement(StatementDraftRevision Revision, string ArticleText)
{
    public string RecordId => Revision.CandidateId;

    public bool HasPassageCovering(SourceSpan span) => Passages().Any(passage => passage.Span.Covers(span));

    public ImmutableArray<StatementPassage> Passages() =>
        [.. Revision.Quotations.Select(quotation => new StatementPassage(quotation.Span, ArticleText.Substring(quotation.Span.Start, quotation.Span.Length)))];
}
