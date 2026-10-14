using System.Collections.Immutable;
using CivicLens.Core.Actions;
using CivicLens.Core.Statements;

namespace CivicLens.Tests.Fixtures;

internal static class ActionRevisionFixtures
{
    public static readonly string RecordId = new('a', 64);
    public static readonly string CandidateId = new('b', 64);
    public static readonly string ExtractionId = new('c', 64);
    public static readonly string Fingerprint = new('d', 64);
    public static readonly string IssueRevisionId = new('e', 64);
    public static readonly DateTimeOffset Updated = new(2025, 3, 3, 12, 0, 0, TimeSpan.Zero);

    public static ActionSponsorship Sponsorship(string bioguide = "W000817") => new("warren", bioguide, ActionSponsorshipRole.Sponsor, false,
        new DateOnly(2025, 1, 3), null, [new(ExtractionId, 0, 10), new(ExtractionId, 11, 10)]);

    public static ActionDraftRevision Model(int number = 1, string author = "civic-lens:analysis", DateTimeOffset? candidateUpdate = null) => new(
        RecordId, number, author, Updated.AddMinutes(number), "Warren sponsored a bill", "Senator Warren sponsored the bill.",
        ActionTextOrigin.Model, null, "congress-bills", 119, "hr", 1, CandidateId, candidateUpdate ?? Updated, false,
        Fingerprint, null, null,
        null, null, [], IssueRevisionId, [Sponsorship()], "A housing bill");

    public static ActionDraftRevision PersonEditOf(ActionDraftRevision previous, string headline, string summary) => previous with
    {
        RevisionNumber = previous.RevisionNumber + 1,
        AuthorSubject = "auth0|editor",
        Headline = headline,
        Summary = summary,
        TextOrigin = ActionTextOrigin.Person
    };

    public static ImmutableArray<StatementSpan> Spans(params (int Start, int Length)[] spans) =>
        [.. spans.Select(span => new StatementSpan(ExtractionId, span.Start, span.Length))];
}
