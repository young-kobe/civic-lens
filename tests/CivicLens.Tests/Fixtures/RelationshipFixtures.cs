using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Application.Relationships;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;
using CivicLens.Core.Relationships;
using CivicLens.Core.Statements;
using CivicLens.Core.Votes;

namespace CivicLens.Tests.Fixtures;

internal static class RelationshipFixtures
{
    public const string BillSentence = "I introduced H.R. 1234 to cap rent.";
    public const string TitleSentence = "The Lower Rent Act of 2025 helps renters.";
    public const string FillerSentence = "Nothing else here.";
    public const string Article = "Senator Warren said: " + BillSentence + " " + TitleSentence + " " + FillerSentence;
    public const string BillWords = "H.R. 1234";

    public static readonly string StatementId = new('1', 64);
    public static readonly string ActionId = new('a', 64);
    public static readonly string VoteId = new('b', 64);
    public static readonly string ExtractionId = new('c', 64);
    public static readonly DateOnly StatementDate = new(2025, 3, 1);
    public static readonly DateTimeOffset Created = new(2025, 3, 5, 0, 0, 0, TimeSpan.Zero);

    public static readonly IReadOnlyDictionary<string, string> Officials = new Dictionary<string, string>
    {
        ["warren"] = "Elizabeth Warren",
        ["markey"] = "Ed Markey",
        ["sanders"] = "Bernie Sanders"
    };

    public static SourceSpan Span(string words, string article = Article) =>
        new(ExtractionId, article.IndexOf(words, StringComparison.Ordinal), words.Length);

    public static ApprovedStatement Statement(DateOnly? date = null, bool dated = true, string[]? officials = null, string[]? passages = null,
        int revision = 1, string article = Article) => new(new StatementDraftRevision(StatementId, revision, "auth0|editor", Created, "Warren on rent", "Summary",
        dated ? date ?? StatementDate : null, null, [.. officials ?? ["warren"]], [], [], [],
        [.. (passages ?? [BillSentence, TitleSentence]).Select(text => new StatementQuotation(Span(text, article), StatementQuotationKind.DirectQuotation))], []),
        article);

    public static ActionDraftRevision Action(int billNumber = 1234, string title = "Lower Rent Act of 2025", int congress = 119,
        string sponsor = "warren", ActionSponsorshipRole role = ActionSponsorshipRole.Sponsor, bool original = true, DateOnly? withdrawnOn = null,
        int revision = 1) => ActionRevisionFixtures.Model(revision) with
        {
            RecordId = ActionId,
            Congress = congress,
            BillNumber = billNumber,
            BillTitle = title,
            Sponsorships = [ActionRevisionFixtures.Sponsorship() with { PersonId = sponsor, Role = role, IsOriginal = original, WithdrawnOn = withdrawnOn }]
        };

    public static VoteDraftRevision Vote(int billNumber = 1234, string person = "warren", string cast = "Yea", int congress = 119,
        int revision = 1)
    {
        SourceSpan Line(int start) => new(ExtractionId, start, 5);
        return new(VoteId, revision, "auth0|editor", Created, "House vote", "Summary", CivicLens.Core.Review.ReviewTextOrigin.Person, null,
            "congress-house-votes", VoteSource.CongressGov, Chamber.House, congress, 1, 100, "hr", billNumber, new string('d', 64), Created,
            [new("Started: 2025-04-10T11:08:00-04:00", Line(0)), new("Question: On Passage", Line(10)), new("Result: Passed", Line(20))],
            new DateOnly(2025, 4, 10), Line(0), VoteKind.Passage, Line(10), [new(person, "W000817", cast, Line(30))], ["housing"], new string('e', 64));
    }

    public static ApprovedStatement StatementAboutBill(string official, int congress, int billNumber, string citationType = "H.R.",
        string? id = null, bool dated = true)
    {
        var sentence = $"I back {citationType} {billNumber} today.";
        var statement = Statement(CongressCalendar.StartOf(congress).AddDays(57), dated, [official], [sentence], article: "Said: " + sentence);
        return id is null ? statement : statement with { Revision = statement.Revision with { CandidateId = id } };
    }

    public static RelationshipPair ActionPair(ApprovedStatement? statement = null, ActionDraftRevision? action = null) =>
        RelationshipPairFinder.Match(statement ?? Statement(), action ?? Action())!;

    public static RelationshipPair VotePair(ApprovedStatement? statement = null, VoteDraftRevision? vote = null) =>
        RelationshipPairFinder.Match(statement ?? Statement(), vote ?? Vote())!;

    public static RelationshipRevision Revision(RelationshipPair? pair = null)
    {
        var chosen = pair ?? ActionPair();
        return new(chosen.PairId, 1, CivicLens.Core.Review.ReviewAuthor.AnalysisSubject, Created,
            new(RelationshipRecordKind.Statement, chosen.Statement.RecordId, chosen.Statement.Revision.RevisionNumber), chosen.Target.Citation,
            chosen.Match, RelationshipType.DescribesOwnBill, "Warren says she introduced the bill.", Span(BillWords), "F2", chosen.OfficialId);
    }

    public static RelationshipDraftContext Context(RelationshipPair pair, RelationshipRevision? current = null, string? settledFingerprint = null) =>
        RelationshipDraftContext.Create(Evidence(pair, current, settledFingerprint), pair, Officials);

    public static RelationshipDraftEvidence Evidence(RelationshipPair pair, RelationshipRevision? current = null, string? settledFingerprint = null) =>
        new(pair.PairId, pair.Target.Kind, pair.Statement, pair.Target.Kind == RelationshipRecordKind.Action ? Action() : null,
            pair.Target.Kind == RelationshipRecordKind.Vote ? Vote() : null, settledFingerprint, current, false);

    public static string Output(string type = "describesOwnBill", string reason = "Warren says she introduced the bill.",
        string? quote = BillSentence, string? factId = "F2") => JsonSerializer.Serialize(
        new Dictionary<string, object?> { ["type"] = type, ["reason"] = reason, ["statementQuote"] = quote, ["targetFactId"] = factId });

    public static string None() => Output("none", "", null, null);
}
