using System.Collections.Immutable;
using CivicLens.Core.Actions;
using static CivicLens.Application.Analysis.AnalysisAnswerRules;

namespace CivicLens.Application.Actions;

/// <summary>
/// The key terms of one bill text draft: where the definitions section lies, and the checks of the terms that the model returns. Code locates the
/// section, so a bill with no definitions section has no scope, and its draft asks for no key terms. The one owner of the key term checks is
/// <see cref="Resolve"/>. A failed check is an error that the repair pass sends back. On the last pass the resolver drops the failing terms instead, so a
/// key term never fails the draft. A glossary term or a repeated term is always dropped, because it is no fault of the model.
/// </summary>
public sealed class ActionKeyTermScope
{
    private readonly ActionDraftBillText billText;
    private readonly IReadOnlySet<string> glossary;

    private ActionKeyTermScope(ActionDraftBillText billText, BillDefinitionsSection section, IReadOnlySet<string> glossary)
    {
        this.billText = billText;
        Section = section;
        this.glossary = glossary;
    }

    public BillDefinitionsSection Section { get; }

    public static ActionKeyTermScope? For(ActionDraftBillText billText, IReadOnlySet<string> glossary)
    {
        ArgumentNullException.ThrowIfNull(billText);
        ArgumentNullException.ThrowIfNull(glossary);
        return BillDefinitionsSection.Find(billText.Body) is { } section ? new(billText, section, glossary) : null;
    }

    internal ImmutableArray<ActionKeyTerm> Resolve(AnalysisOutputKeyTerm?[]? items, BillTextQuotes quotes, List<string> errors)
    {
        if (items is not { Length: > 0 }) return [];
        if (items.Length > ActionKeyTerm.MaximumPerRevision)
            errors.Add($"The keyTerms list has {items.Length} terms. Write at most {ActionKeyTerm.MaximumPerRevision}.");
        var terms = new List<ActionKeyTerm>();
        foreach (var item in items.Take(ActionKeyTerm.MaximumPerRevision))
            if (Quoted(item, quotes, errors) is { } term && !glossary.Contains(term.Term) && !IsRepeat(term, terms)) terms.Add(term);
        return [.. terms];
    }

    private ActionKeyTerm? Quoted(AnalysisOutputKeyTerm? item, BillTextQuotes quotes, List<string> errors)
    {
        var term = item?.Term?.Trim() ?? string.Empty;
        var quote = item?.Quote ?? string.Empty;
        if (term.Length == 0 || term.Length > ActionKeyTerm.MaximumTermLength)
            errors.Add($"A key term has {term.Length} characters. Write 1 to {ActionKeyTerm.MaximumTermLength}.");
        else if (quote.Length > ActionDraftRevision.MaximumTextQuoteLength)
            errors.Add($"The quote of the key term \"{Shorten(term)}\" has {quote.Length} characters. Quote at most {ActionDraftRevision.MaximumTextQuoteLength}.");
        else if (ActionTextCitation.WordCount(quote) < ActionDraftRevision.MinimumTextQuoteWords)
            errors.Add($"The quote of the key term \"{Shorten(term)}\" has fewer than {ActionDraftRevision.MinimumTextQuoteWords} words. Quote at least {ActionDraftRevision.MinimumTextQuoteWords}.");
        else if (quotes.FindInDefinitions(quote, Section) is not { } span)
            errors.Add($"The quote of the key term \"{Shorten(term)}\" is not an exact quote of the definitions section. Copy it exactly from that section.");
        else if (span.Length > ActionDraftRevision.MaximumTextQuoteLength)
            errors.Add($"The quote of the key term \"{Shorten(term)}\" spans {span.Length} characters of the stored text. Quote at most {ActionDraftRevision.MaximumTextQuoteLength}.");
        else return Defined(new ActionKeyTerm(term, span, quote, Section.Cuts(billText.Body, span.Start + span.Length - billText.BodyStart)), errors);
        return null;
    }

    private static ActionKeyTerm? Defined(ActionKeyTerm term, List<string> errors)
    {
        if (term.IsDefinedBy(term.Definition)) return term;
        errors.Add($"The key term \"{Shorten(term.Term)}\" is not the term that its quote defines in quotation marks. Write the term exactly as the quote marks it.");
        return null;
    }

    private static bool IsRepeat(ActionKeyTerm term, List<ActionKeyTerm> kept) =>
        kept.Any(item => string.Equals(item.Term, term.Term, StringComparison.OrdinalIgnoreCase) || item.Span == term.Span);
}
