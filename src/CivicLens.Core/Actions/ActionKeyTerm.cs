using CivicLens.Core.Documents;

namespace CivicLens.Core.Actions;

/// <summary>
/// One key term of a bill. The span names the exact quote of the definitions section that defines the term. Definition is the stored characters of
/// that span, kept in its own column so the public view can serve it. Approval checks that the two agree. The term itself appears in the quote.
/// </summary>
public sealed record ActionKeyTerm(string Term, SourceSpan Span, string Definition)
{
    public const int MaximumPerRevision = 5;
    public const int MaximumTermLength = 80;

    public bool IsDefinedBy(string definition) => definition.Contains(Term, StringComparison.OrdinalIgnoreCase);
}
