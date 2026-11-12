using System.Text.RegularExpressions;
using CivicLens.Core.Documents;

namespace CivicLens.Core.Actions;

/// <summary>
/// One key term of a bill. The span names the exact quote of the definitions section that defines the term, in the exact characters of the stored text.
/// Definition is that quote with each GPO quote mark pair made plain by <see cref="GpoQuoteMarks.Normalize"/>, kept in its own column so the public view can serve it.
/// Approval checks that the two agree. Cut is true when the quote ends before the end of its definition paragraph, as
/// <see cref="BillDefinitionsSection.Cuts"/> decides at draft time. Approval checks it again.
/// </summary>
public sealed partial record ActionKeyTerm(string Term, SourceSpan Span, string Definition, bool Cut)
{
    public const int MaximumPerRevision = 5;
    public const int MaximumTermLength = 80;

    /// <summary>True when the plain definition marks the term itself in quotation marks, such as "Secretary" in The term "Secretary" means.
    /// The term equals one marked term exactly, case included. A word that only appears in the quote is not the defined term.</summary>
    public bool IsDefinedBy(string definition) =>
        MarkedTerm().Matches(definition).Any(match => string.Equals(match.Groups["term"].Value, Term, StringComparison.Ordinal));

    [GeneratedRegex("\"(?<term>[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex MarkedTerm();
}
