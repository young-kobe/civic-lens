using System.Collections.Immutable;
using System.Globalization;
using CivicLens.Application.Analysis;
using CivicLens.Core.Actions;
using CivicLens.Core.Analysis;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using static CivicLens.Application.Analysis.AnalysisAnswerRules;

namespace CivicLens.Application.Actions;

public static class ActionDraftResolver
{
    private const string BillTextSource = "the bill text";

    private readonly record struct TextSource(string? PackageId, string? WhyItMatters, ImmutableArray<ActionTextCitation> Citations,
        ImmutableArray<ActionKeyTerm> KeyTerms = default)
    {
        public static TextSource Of(ActionDraftRevision revision) =>
            new(revision.TextPackageId, revision.WhyItMatters, revision.TextCitations, revision.KeyTerms);
    }

    public static AnalysisResolution<ActionDraftRevision> Resolve(string outputJson, ActionDraftContext context, string runId,
        DateTimeOffset createdAtUtc)
    {
        var snapshot = context.Snapshot;
        var output = HeadlineSummaryOutput.Parse(outputJson);
        if (output is null)
            return AnalysisResolution<ActionDraftRevision>.Reject(["The output is not valid JSON for the action schema."], false);
        var errors = new List<string>();
        CheckText(output.Headline.Text, ActionDraftRevision.MaximumHeadlineLength, "headline", required: true, errors);
        CheckText(output.Summary.Text, ActionDraftRevision.MaximumSummaryLength, "summary", required: true, errors);
        CheckConfidence(output.Headline.Confidence, "headline confidence", errors);
        CheckConfidence(output.Summary.Confidence, "summary confidence", errors);
        CheckNames(output.Headline.Text, "headline", snapshot, errors);
        CheckNames(output.Summary.Text, "summary", snapshot, errors);
        CheckNoSummarySentence(output.Headline.Text, "headline", errors);
        CheckNoSummarySentence(output.Summary.Text, "summary", errors);
        if (AiTextRules.PassageGradeError([output.Headline.Text, output.Summary.Text]) is { } passageGrade) errors.Add(passageGrade);
        var issueIds = context.Issues.Check(output.IssueIds, errors);
        if (errors.Count > 0) return AnalysisResolution<ActionDraftRevision>.Reject(Bound(errors), false);
        return AnalysisResolution<ActionDraftRevision>.Accept(Assemble(context, createdAtUtc, ReviewText.AfterModelDraft(context.Evidence.Current,
            output.Headline.Text, output.Summary.Text, runId, snapshot.Fingerprint), issueIds, context.Issues.SubjectsHash,
            snapshot.SummaryVersion, snapshot.SummaryCitation, default));
    }

    /// <summary>Key terms are resolved only with a scope, which exists when the key term setting is on and the bill text has a definitions section.
    /// With no scope the draft has no key terms, whatever the model wrote. With <paramref name="dropFailingKeyTerms"/>, which the last pass sets,
    /// a key term that fails a check is left out and its error is not sent, so the checked summary is kept.</summary>
    public static AnalysisResolution<ActionDraftRevision> ResolveBillText(string outputJson, ActionDraftContext context, string runId,
        DateTimeOffset createdAtUtc, ActionKeyTermScope? keyTerms = null, bool dropFailingKeyTerms = false)
    {
        var billText = context.Evidence.BillText ?? throw new ArgumentException("Bill text drafting needs the bill text.", nameof(context));
        var output = ActionTextOutput.Parse(outputJson);
        if (output is null)
            return AnalysisResolution<ActionDraftRevision>.Reject(["The output is not valid JSON for the bill text schema."], false);
        var errors = new List<string>();
        CheckText(output.Headline.Text, ActionDraftRevision.MaximumHeadlineLength, "headline", required: true, errors);
        CheckConfidence(output.Headline.Confidence, "headline confidence", errors);
        CheckConfidence(output.Summary.Confidence, "summary confidence", errors);
        CheckConfidence(output.WhyItMatters.Confidence, "whyItMatters confidence", errors);
        CheckNames(output.Headline.Text, "headline", context.Snapshot, errors);
        CheckNoSummarySentence(output.Headline.Text, "headline", errors);
        var quotes = new BillTextQuotes(billText);
        var figures = new BillFigureSet(quotes.NormalizedBody);
        var billNumber = context.Snapshot.Bill.Number.ToString(CultureInfo.InvariantCulture);
        errors.AddRange(figures.Errors(output.Headline.Text, "headline", BillTextSource, billNumber));
        var summary = QuotedSentences(output.Summary.Sentences, ActionTextPart.Summary, "summary", quotes, errors);
        var why = QuotedSentences(output.WhyItMatters.Sentences, ActionTextPart.WhyItMatters, "whyItMatters", quotes, errors);
        CheckSentences(summary, "summary", context.Snapshot, figures, errors);
        CheckSentences(why, "whyItMatters", context.Snapshot, figures, errors);
        if (AiTextRules.QuotedPassageGradeError(PassageOf(output)) is { } passageGrade) errors.Add(passageGrade);
        var keyTermErrors = new List<string>();
        var terms = keyTerms?.Resolve(output.KeyTerms, quotes, keyTermErrors) ?? default;
        if (!dropFailingKeyTerms) errors.AddRange(keyTermErrors);
        var issueIds = context.Issues.Check(output.IssueIds, errors);
        if (errors.Count > 0) return AnalysisResolution<ActionDraftRevision>.Reject(Bound(errors), false);
        var citations = summary.Concat(why).ToImmutableArray();
        var source = new TextSource(billText.PackageId, ActionTextCitation.Join(why), citations,
            terms.IsDefaultOrEmpty ? default : terms); // An empty list stays the default array, the one form that a stored row rebuilds to.
        return AnalysisResolution<ActionDraftRevision>.Accept(Assemble(context, createdAtUtc, ReviewText.AfterModelDraft(context.Evidence.Current,
            output.Headline.Text, ActionTextCitation.Join(summary), runId, context.Snapshot.Fingerprint), issueIds, context.Issues.SubjectsHash, null, null, source));
    }

    public static AnalysisResolution<ActionDraftRevision> ResolveIssueChoice(string outputJson, ActionDraftContext context,
        DateTimeOffset createdAtUtc)
    {
        var output = IssueChoiceOutput.Parse(outputJson);
        if (output is null)
            return AnalysisResolution<ActionDraftRevision>.Reject(["The output is not valid JSON for the issue schema."], false);
        var errors = new List<string>();
        CheckConfidence(output.Confidence, "confidence", errors);
        var issueIds = context.Issues.Check(output.IssueIds, errors);
        if (errors.Count > 0) return AnalysisResolution<ActionDraftRevision>.Reject(Bound(errors), false);
        return AnalysisResolution<ActionDraftRevision>.Accept(WriteByCode(context, createdAtUtc, issueIds));
    }

    /// <summary>Keeps the text, the origin, and the suggestion of the current revision. The summary version stays with the text.
    /// An issue stays only while it is still a candidate.</summary>
    public static ActionDraftRevision CopyText(ActionDraftContext context, DateTimeOffset createdAtUtc)
    {
        var current = context.Evidence.Current ?? throw new ArgumentException("Copied text needs a current revision.", nameof(context));
        var subjectsHash = context.Evidence.Candidate.IncludesSubjects ? context.Issues.SubjectsHash : current.SubjectsHash;
        return Assemble(context, createdAtUtc, ReviewText.CopyOf(current), [.. current.TrackedIssueIds.Where(context.Issues.CandidateIds.Contains)],
            subjectsHash, current.SummaryVersion, current.SummaryCitation, TextSource.Of(current));
    }

    public static ActionDraftRevision WriteByCode(ActionDraftContext context, DateTimeOffset createdAtUtc, ImmutableArray<string> trackedIssueIds = default) =>
        Assemble(context, createdAtUtc, ActionCodeText.Of(context.Snapshot), trackedIssueIds.IsDefault ? [] : trackedIssueIds,
            context.Issues.SubjectsHash, context.Snapshot.SummaryVersion, context.Snapshot.SummaryCitation, default);

    private static List<ActionTextCitation> QuotedSentences(AnalysisOutputSentence?[] items, ActionTextPart part, string field,
        BillTextQuotes quotes, List<string> errors)
    {
        var (minimum, maximum) = part == ActionTextPart.Summary
            ? (QuotedSentenceLimits.MinimumSentences, QuotedSentenceLimits.MaximumSentences)
            : (1, ActionDraftRevision.MaximumWhyItMattersSentences);
        var citations = new List<ActionTextCitation>();
        if (items.Length < minimum || items.Length > maximum)
            errors.Add($"The {field} has {items.Length} sentences. Write {minimum} to {maximum}.");
        foreach (var item in items.Take(maximum))
            if (QuotedSentence(item, part, field, quotes, errors) is { } citation) citations.Add(citation);
        return citations;
    }

    private static ActionTextCitation? QuotedSentence(AnalysisOutputSentence? item, ActionTextPart part, string field, BillTextQuotes quotes,
        List<string> errors)
    {
        var sentence = item?.Sentence?.Trim() ?? string.Empty;
        var quote = item?.Quote ?? string.Empty;
        if (sentence.Length == 0 || sentence.Length > QuotedSentenceLimits.MaximumSentenceLength)
            errors.Add($"A {field} sentence has {sentence.Length} characters. Write 1 to {QuotedSentenceLimits.MaximumSentenceLength}.");
        else if (quote.Length > QuotedSentenceLimits.MaximumQuoteLength)
            errors.Add($"A {field} quote has {quote.Length} characters. Quote at most {QuotedSentenceLimits.MaximumQuoteLength}.");
        else if (ActionTextCitation.WordCount(quote) < QuotedSentenceLimits.MinimumQuoteWords)
            errors.Add($"The {field} quote \"{Shorten(quote)}\" has fewer than {QuotedSentenceLimits.MinimumQuoteWords} words. Quote at least {QuotedSentenceLimits.MinimumQuoteWords}.");
        else if (quotes.Find(quote) is not { } span)
            errors.Add($"The {field} quote \"{Shorten(quote)}\" is not an exact quote of the bill text that was sent. Copy it exactly.");
        else if (span.Length > QuotedSentenceLimits.MaximumQuoteLength)
            errors.Add($"A {field} quote spans {span.Length} characters of the stored text. Quote at most {QuotedSentenceLimits.MaximumQuoteLength}.");
        else return new(part, span, sentence);
        return null;
    }

    private static void CheckSentences(List<ActionTextCitation> citations, string field, ActionBillSnapshot snapshot, BillFigureSet figures,
        List<string> errors)
    {
        var text = ActionTextCitation.Join(citations);
        CheckNames(text, field, snapshot, errors);
        CheckNoSummarySentence(text, field, errors);
        // The sentences are free text apart from their quote, so the figures of a sentence are checked against the bill text that was sent.
        errors.AddRange(figures.Errors(text, field, BillTextSource, snapshot.Bill.Number.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>The headline, the summary sentences and the whyItMatters sentences read as one passage.</summary>
    private static IEnumerable<string?> PassageOf(ActionTextOutput output) =>
        output.Summary.Sentences.Concat(output.WhyItMatters.Sentences).Select(item => item?.Sentence).Prepend(output.Headline.Text);

    private static void CheckNoSummarySentence(string text, string field, List<string> errors)
    {
        if (text.Contains(ActionCodeText.NoSummarySentence, StringComparison.Ordinal))
            errors.Add($"The {field} says that Congress.gov has no summary. Never write that sentence. Say only what the bill does.");
    }

    private static ActionDraftRevision Assemble(ActionDraftContext context, DateTimeOffset createdAtUtc, ReviewText text,
        ImmutableArray<string> trackedIssueIds, string? subjectsHash, string? summaryVersion, SourceSpan? summaryCitation, TextSource textSource)
    {
        var (evidence, snapshot, issues, policyAreaId, issueRevisionId) = context;
        var current = evidence.Current;
        var candidate = evidence.Candidate;
        var author = text.Origin == ReviewTextOrigin.Code ? ReviewAuthor.CodeSubject : ReviewAuthor.AnalysisSubject;
        var revision = new ActionDraftRevision(candidate.RecordId, (current?.RevisionNumber ?? 0) + 1, author,
            createdAtUtc, text.Headline, text.Summary, text.Origin, text.Suggestion,
            candidate.SourceId, candidate.Congress, candidate.BillType, candidate.BillNumber, candidate.CandidateId, candidate.UpdateDate,
            candidate.IncludesSummaries, candidate.IncludesSubjects,
            snapshot.Fingerprint, summaryVersion, summaryCitation, snapshot.Bill.PolicyArea, snapshot.PolicyAreaCitation,
            policyAreaId, trackedIssueIds, issues.CandidateIds, issueRevisionId, snapshot.Sponsorships, snapshot.Bill.Title, subjectsHash,
            text.Origin == ReviewTextOrigin.Code ? snapshot.BillCitation : null, textSource.PackageId, textSource.WhyItMatters, textSource.Citations,
            textSource.KeyTerms);
        revision.ValidateAsWorkerRevision(current);
        return revision;
    }

    private static void CheckNames(string text, string field, ActionBillSnapshot snapshot, List<string> errors)
    {
        CheckNamesOutsideFacts(text, field, snapshot.ExcludedNames, snapshot.OfficialNames.Values, errors);
        foreach (var name in CosponsorNamesOf(snapshot).Where(name => !IsPartOfListedName(name, SponsorNamesOf(snapshot))))
            if (text.Contains(name, StringComparison.OrdinalIgnoreCase))
                errors.Add($"The {field} names \"{Shorten(name)}\", a cosponsor. Code lists the cosponsors, so name only the sponsors.");
    }

    private static IEnumerable<string> CosponsorNamesOf(ActionBillSnapshot snapshot)
    {
        var sponsors = SponsorIdsOf(snapshot);
        return snapshot.Sponsorships.Where(item => item.Role == ActionSponsorshipRole.Cosponsor && !sponsors.Contains(item.PersonId))
            .Select(item => snapshot.OfficialNames[item.PersonId]).Distinct(StringComparer.Ordinal);
    }

    private static IEnumerable<string> SponsorNamesOf(ActionBillSnapshot snapshot) =>
        SponsorIdsOf(snapshot).Select(id => snapshot.OfficialNames[id]);

    private static HashSet<string> SponsorIdsOf(ActionBillSnapshot snapshot) =>
        snapshot.Sponsorships.Where(item => item.Role == ActionSponsorshipRole.Sponsor).Select(item => item.PersonId).ToHashSet(StringComparer.Ordinal);
}
