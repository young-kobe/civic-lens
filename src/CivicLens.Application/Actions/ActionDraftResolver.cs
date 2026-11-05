using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using static CivicLens.Application.Analysis.AnalysisAnswerRules;

namespace CivicLens.Application.Actions;

public static class ActionDraftResolver
{
    private readonly record struct TextSource(string? PackageId, string? WhyItMatters, ImmutableArray<ActionTextCitation> Citations)
    {
        public static TextSource Of(ActionDraftRevision revision) => new(revision.TextPackageId, revision.WhyItMatters, revision.TextCitations);
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
        var issueIds = context.Issues.Check(output.IssueIds, errors);
        if (errors.Count > 0) return AnalysisResolution<ActionDraftRevision>.Reject(Bound(errors), false);
        return AnalysisResolution<ActionDraftRevision>.Accept(Assemble(context, createdAtUtc, ReviewText.AfterModelDraft(context.Evidence.Current,
            output.Headline.Text, output.Summary.Text, runId, snapshot.Fingerprint), issueIds, context.Issues.SubjectsHash,
            snapshot.SummaryVersion, snapshot.SummaryCitation, default));
    }

    public static AnalysisResolution<ActionDraftRevision> ResolveBillText(string outputJson, ActionDraftContext context, string runId,
        DateTimeOffset createdAtUtc)
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
        var summary = QuotedSentences(output.Summary.Sentences, ActionTextPart.Summary, "summary", billText, errors);
        var why = QuotedSentences(output.WhyItMatters.Sentences, ActionTextPart.WhyItMatters, "whyItMatters", billText, errors);
        var issueIds = context.Issues.Check(output.IssueIds, errors);
        if (errors.Count > 0) return AnalysisResolution<ActionDraftRevision>.Reject(Bound(errors), false);
        var citations = summary.Concat(why).ToImmutableArray();
        var source = new TextSource(billText.PackageId, Join(output.WhyItMatters.Sentences), citations);
        return AnalysisResolution<ActionDraftRevision>.Accept(Assemble(context, createdAtUtc, ReviewText.AfterModelDraft(context.Evidence.Current,
            output.Headline.Text, Join(output.Summary.Sentences), runId, context.Snapshot.Fingerprint), issueIds, context.Issues.SubjectsHash, null, null, source));
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

    private static string Join(string[] sentences) => string.Join(ActionDraftRevision.SentenceSeparator, sentences);

    private static List<ActionTextCitation> QuotedSentences(string[] sentences, ActionTextPart part, string field, ActionDraftBillText billText,
        List<string> errors)
    {
        var (minimum, maximum) = part == ActionTextPart.Summary
            ? (ActionDraftRevision.MinimumSummarySentences, ActionDraftRevision.MaximumSummarySentences)
            : (1, ActionDraftRevision.MaximumWhyItMattersSentences);
        var citations = new List<ActionTextCitation>();
        if (sentences.Length < minimum || sentences.Length > maximum)
            errors.Add($"The {field} has {sentences.Length} sentences. Write {minimum} to {maximum}.");
        foreach (var sentence in sentences.Take(maximum))
        {
            if (sentence.Length > ActionDraftRevision.MaximumTextQuoteLength)
                errors.Add($"A {field} sentence has {sentence.Length} characters. Quote at most {ActionDraftRevision.MaximumTextQuoteLength}.");
            else if (ActionCitations.BillTextQuote(billText, sentence) is { } span) citations.Add(new(part, span));
            else errors.Add($"The {field} sentence \"{Shorten(sentence)}\" is not an exact quote of the bill text. Copy a sentence exactly as the bill text has it.");
        }
        return citations;
    }

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
            text.Origin == ReviewTextOrigin.Code ? snapshot.BillCitation : null, textSource.PackageId, textSource.WhyItMatters, textSource.Citations);
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
