using System.Collections.Immutable;
using CivicLens.Application.Briefs;
using CivicLens.Core.Briefs;
using CivicLens.Core.Review;

namespace CivicLens.Tests.Fixtures;

internal static class BriefFixtures
{
    public static readonly DateOnly Week = new(2026, 10, 5);
    public static readonly DateTimeOffset WeekStartUtc = new(2026, 10, 5, 4, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset WeekEndUtc = new(2026, 10, 12, 4, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset InWeek = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    public static readonly string Fingerprint = new('f', 64);
    public static readonly string RecordId = BriefWeek.RecordIdOf(Week);
    public static readonly string ExampleRecordId = new('a', 64);
    public static readonly DateTimeOffset Created = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    public static readonly IReadOnlyDictionary<string, string> Officials = new Dictionary<string, string>
    {
        ["warren"] = "Elizabeth Warren",
        ["markey"] = "Ed Markey",
        ["sanders"] = "Bernie Sanders"
    };

    public static BriefWindow Window(int first = 2, int cutoff = 3) =>
        new(Week, WeekStartUtc.UtcTicks, WeekEndUtc.UtcTicks, first, cutoff);

    public static BriefRecord Statement(string id, string headline = "A statement", int revision = 1, params string[] officials) =>
        new(BriefCitationKind.Statement, id, revision, headline, "Summary of " + headline, new DateOnly(2026, 10, 6),
            [.. officials], null, 0);

    public static BriefRecord DocumentChange(string id, string headline = "A change", int revision = 1, params string[] officials) =>
        new(BriefCitationKind.DocumentChange, id, revision, headline, "Summary of " + headline, null, [.. officials], null, 0);

    public static BriefRecord Action(string id, string bill = "119 hr 1", int sponsorships = 1, params string[] officials) =>
        new(BriefCitationKind.Action, id, 1, "Bill " + bill, "Summary of " + bill, null, [.. officials], bill, sponsorships);

    public static BriefDraftEvidence Evidence(IEnumerable<BriefRecord> records, BriefRevision? current = null, BriefWindow? window = null,
        int leftOut = 0) =>
        new(window ?? Window(), [.. records], leftOut, current);

    public static BriefDraftContext Context(IEnumerable<BriefRecord> records, BriefRevision? current = null) =>
        BriefDraftContext.Create(Evidence(records, current), SelectAll(records), Officials);

    public static BriefSelection SelectAll(IEnumerable<BriefRecord> records) => BriefSelection.Select([.. records], _ => true);

    public static BriefRevision Revision(int number = 1, string author = ReviewAuthor.AnalysisSubject) => new(RecordId, number, author, Created,
        Window(), Fingerprint, "A week of housing news", ReviewTextOrigin.Model, null,
        [new("Warren backed a rent cap.", ["R1"]), new("A rule changed.", ["R1", "R2"])],
        [new("R1", BriefCitationKind.Statement, new string('1', 64), 1), new("R2", BriefCitationKind.DocumentChange, new string('2', 32), 3)],
        new(5, 4, 2));

    public static string Output(string headline = "A week of housing news", double confidence = 0.8, params (string Text, string[] Ids)[] paragraphs)
    {
        var body = paragraphs.Length > 0 ? paragraphs : [("A paragraph.", ["R1"])];
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            headline,
            paragraphs = body.Select(paragraph => new { text = paragraph.Text, citationIds = paragraph.Ids }),
            confidence
        });
    }
}
