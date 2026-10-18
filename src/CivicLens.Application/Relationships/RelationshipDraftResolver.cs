using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Application.Statements;
using CivicLens.Core.Documents;
using CivicLens.Core.Relationships;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using static CivicLens.Application.Analysis.AnalysisAnswerRules;

namespace CivicLens.Application.Relationships;

public static class RelationshipDraftResolver
{
    public static AnalysisResolution<RelationshipRevision?> Resolve(string outputJson, RelationshipDraftContext context,
        DateTimeOffset createdAtUtc)
    {
        var output = Parse(outputJson);
        if (output is null)
            return AnalysisResolution<RelationshipRevision?>.Reject(["The output is not valid JSON for the relationship schema."], false);
        if (output.Type == RelationshipDraftPrompt.NoneAnswer) return AnalysisResolution<RelationshipRevision?>.Accept(null);
        var errors = new List<string>();
        var citationErrors = new List<string>();
        var pair = context.Pair;
        var type = RelationshipTypes.Parse(output.Type, pair.Target.Kind);
        if (type is null) errors.Add($"The type must be {RelationshipDraftPrompt.NoneAnswer} or one of the types that the schema lists.");
        CheckReason(output.Reason, context, errors);
        CheckFact(output.TargetFactId, pair.Target, errors, citationErrors);
        var quote = LocateQuote(output.StatementQuote, pair, errors, citationErrors);
        if (errors.Count > 0) return AnalysisResolution<RelationshipRevision?>.Reject(Bound(errors), citationErrors.Count > 0);
        var current = context.Evidence.Current;
        var revision = new RelationshipRevision(pair.PairId, (current?.RevisionNumber ?? 0) + 1, ReviewAuthor.AnalysisSubject, createdAtUtc,
            new(RelationshipRecordKind.Statement, pair.Statement.RecordId, pair.Statement.Revision.RevisionNumber), pair.Target.Citation,
            pair.Match, type!.Value, output.Reason, quote!, output.TargetFactId!);
        revision.ValidateAsAnalysisRevision(current);
        return AnalysisResolution<RelationshipRevision?>.Accept(revision);
    }

    private static void CheckReason(string reason, RelationshipDraftContext context, List<string> errors)
    {
        CheckText(reason, RelationshipRevision.MaximumReasonLength, "reason", required: true, errors);
        if (reason.Contains('\n') || reason.Contains('\r')) errors.Add("The reason is one sentence on one line.");
        CheckNamesOutsideFacts(reason, "reason", context.ExcludedNames, context.ListedNames, errors);
    }

    private static void CheckFact(string? factId, RelationshipTarget target, List<string> errors, List<string> citationErrors)
    {
        if (target.Facts.Any(fact => fact.Id == factId)) return;
        AddCitationError($"The targetFactId \"{Shorten(factId)}\" is not a listed fact ID. Use one of the listed IDs.", errors, citationErrors);
    }

    private static SourceSpan? LocateQuote(string? quote, RelationshipPair pair, List<string> errors, List<string> citationErrors)
    {
        var start = ArticleQuote.LocateStart(pair.Statement.ArticleText, quote, "statementQuote", StatementDraftRevision.MaximumQuotationLength,
            errors, citationErrors);
        if (start is not { } found) return null;
        var span = new SourceSpan(pair.Match.Span.ExtractionId, found, quote!.Length);
        if (!pair.Statement.HasPassageCovering(span))
            AddCitationError("The statementQuote must lie inside one passage of the statement.", errors, citationErrors);
        else if (!span.Covers(pair.Match.Span))
            AddCitationError("The statementQuote must include the matched words.", errors, citationErrors);
        else return span;
        return null;
    }

    private static RelationshipDraftOutput? Parse(string outputJson)
    {
        try
        {
            return JsonSerializer.Deserialize<RelationshipDraftOutput>(outputJson, OutputJson);
        }
        catch (JsonException) { return null; }
    }
}
