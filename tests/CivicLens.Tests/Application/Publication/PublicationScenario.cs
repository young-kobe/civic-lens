using CivicLens.Application.Collection.FederalRegister;
using CivicLens.Application.ExecutiveActions;
using CivicLens.Core.ExecutiveActions;
using CivicLens.Tests.Application.ExecutiveActions;
using CivicLens.Application.Collection;
using CivicLens.Application.Roster;
using CivicLens.Application.Paging;
using System.Collections.Immutable;
using CivicLens.Application.Actions;
using CivicLens.Application.Briefs;
using CivicLens.Application.Issues;
using CivicLens.Core.Briefs;
using CivicLens.Tests.Application.Briefs;
using CivicLens.Tests.Application.Relationships;
using CivicLens.Application.Relationships;
using CivicLens.Core.Relationships;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Core.Actions;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using CivicLens.Tests.Application.Actions;
using CivicLens.Core.Votes;
using CivicLens.Tests.Application.Statements;
using CivicLens.Tests.Application.Votes;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Publication;

internal sealed class PublicationScenario
{
    public static readonly ReviewActor Owner = new("owner", ReviewRole.Owner);
    public static readonly ReviewActor Reviewer = new("reviewer", ReviewRole.Reviewer);
    public static readonly DateTimeOffset ApprovedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public List<string> Events { get; } = [];
    public List<ReleaseOfficialPage> OfficialPages { get; } = [];
    public FakeClock Clock { get; } = new(new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
    public FakePublicationStore Publications { get; } = new();
    public FakeReleaseDirectory Releases { get; }
    public FakeReviewStore Reviews { get; } = new();
    public FakeStatementReviewStore Statements { get; } = new();
    public FakeActionReviewStore Actions { get; } = new();
    public FakeVoteReviewStore Votes { get; } = new();
    public FakeExecutiveActionReviewStore ExecutiveActions { get; } = new();
    public FakeRelationshipReviewStore Relationships { get; } = new();
    public FakeBriefReviewStore Briefs { get; } = new();
    public Dictionary<string, string> OfficialNames { get; } = new() { ["mayor"] = "Mayor Example", ["chair"] = "Council Chair Example" };

    public Dictionary<string, (string? BioguideId, string? WikidataId)> ExternalIds { get; } = [];
    public Dictionary<string, OfficeConfiguration[]> Offices { get; } = [];
    public Dictionary<string, string> Websites { get; } = [];
    public HashSet<string> WithheldPhotos { get; } = [];
    public RosterMember[] RosterMembers { get; set; } = [];
    public ExecutiveOfficial[] ExecutiveOfficials { get; set; } = [];
    public OfficialPhoto[] Photos { get; set; } = [];
    public PhotoCaptures Captures { get; } = new();
    public int PhotoReads { get; private set; }

    private CollectionConfiguration Collection() => RosterFixtures.Configuration() with
    {
        People = [.. OfficialNames.Select(name => new PersonConfiguration
        {
            Id = name.Key,
            Name = name.Value,
            BioguideId = ExternalIds.GetValueOrDefault(name.Key).BioguideId,
            WikidataId = ExternalIds.GetValueOrDefault(name.Key).WikidataId,
            Offices = Offices.GetValueOrDefault(name.Key),
            Website = Websites.GetValueOrDefault(name.Key),
            WithholdPhoto = WithheldPhotos.Contains(name.Key)
        })],
        Sources = [RosterFixtures.Source(), ExecutiveFixtures.Source()]
    };

    public PublicationScenario() => Releases = new() { Events = Events };

    public PublishedRecordLoaders Loaders() => new(Reviews, Statements, Actions, Votes, Relationships, Briefs, ExecutiveActions);

    public IssueConfiguration Issues { get; set; } = IssueConfiguration.None;

    public PublicBaseUrl? BaseUrl { get; set; }

    public PublishRelease Publisher() => new(Publications, Releases, new FakeRenderer { Events = Events, OfficialPages = OfficialPages, PublicBaseUrl = BaseUrl }, Loaders(),
        new PublicationCatalog(OfficialNames, Issues), new PublicationOfficials(Collection(), new FixedMemberRoster(RosterMembers, Photos, () => PhotoReads++), new FixedExecutiveRoster(ExecutiveOfficials)),
        Captures.Photos("artifacts"), Clock, new PruneReleases(Publications, Releases, NullLogger<PruneReleases>.Instance));

    public BlockedPublishedRecords Blocked() => new(Publications);

    public ListBlockedPublished BlockedLister() => new(Publications, Releases, Blocked());

    public ActivatePublicationRelease Activator(ILogger<ActivatePublicationRelease>? logger = null) =>
        new(Publications, Releases, Blocked(), logger ?? NullLogger<ActivatePublicationRelease>.Instance);

    private const string PunctuatedHeadline = "Mayor\u2019s \u201Chousing\u201D plan: caf\u00E9 owners, r\u00E9sum\u00E9 rules; it\u2019s 100% ready? ";

    public static PublishedRecordEntry FilledStatementEntry(int index, int headlineLength, int extraIssueIds = 0) => new()
    {
        Kind = PublishedRecordKind.Statement,
        RecordId = index.ToString("x64"),
        RevisionNumber = 1,
        Headline = string.Concat(Enumerable.Repeat(PunctuatedHeadline, headlineLength / PunctuatedHeadline.Length + 1))[..headlineLength],
        FirstPublishedAtUtc = ApprovedAt.AddMinutes(-index),
        Officials = [new() { Id = "mayor", Name = "Mayor Example" }, new() { Id = "chair", Name = "Council Chair Example" }],
        IssueIds = ["housing", "public-transit", .. Enumerable.Range(0, extraIssueIds).Select(issue => $"{issue:x2}".PadRight(256, 'i'))],
        PolicyAreaIds = ["land-use", "transportation"],
        SourceHost = "council.example.gov",
        EventDate = new DateOnly(2026, 1, 15)
    };

    // An action entry names its sponsor only, and its policy area is the longest Congress.gov policy area name.
    public static PublishedRecordEntry FilledActionEntry(int index, int headlineLength) => FilledStatementEntry(index, headlineLength) with
    {
        Kind = PublishedRecordKind.Action,
        Officials = [new() { Id = "mayor", Name = "Mayor Example" }],
        PolicyAreaIds = ["Civil Rights and Liberties, Minority Issues"],
        SourceHost = "api.congress.gov",
        BillTitle = new string('t', 250)
    };

    public void SeedRelease(int recordCount, bool withFiles) =>
        SeedRelease(withFiles, [.. Enumerable.Range(0, recordCount).Select(index => new PublishedRecordEntry
        {
            IssueIds = [],
            Kind = PublishedRecordKind.DocumentChange,
            Officials = [],
            PolicyAreaIds = [],
            RecordId = index.ToString("x32"),
            RevisionNumber = 1,
            Headline = $"Seeded {index}",
            FirstPublishedAtUtc = ApprovedAt.AddMinutes(-index)
        })]);

    public void SeedRelease(bool withFiles, PublishedRecordEntry[] entries)
    {
        var recordCount = entries.Length;
        var manifest = new PublicationRelease
        {
            SchemaVersion = PublicationProtocol.SchemaVersion,
            ReleaseNumber = 1,
            PublishedAtUtc = ApprovedAt,
            Withdrawals = [],
            Records = entries
        };
        var files = new Dictionary<string, byte[]>
        {
            [PublicationProtocol.ManifestPath] = JsonSerializer.SerializeToUtf8Bytes(manifest, PublicationProtocol.JsonOptions),
            [PublicationProtocol.OfficialsPath] = JsonSerializer.SerializeToUtf8Bytes(new PublishedOfficials { Officials = [] }, PublicationProtocol.JsonOptions)
        };
        if (withFiles)
            foreach (var entry in entries)
            {
                files[PublicationProtocol.RecordDataPath(entry.RecordId)] = [];
                files[PublicationProtocol.RecordPagePath(entry.RecordId)] = [];
            }
        Releases.Directories["seeded"] = files;
        Releases.Active = "seeded";
        Publications.Committed.Add(new(1, "seeded", ApprovedAt, recordCount));
        Publications.ActiveReleaseNumber = 1;
    }

    public void SeedReleaseOfAnotherSchemaVersion()
    {
        var manifest = new JsonObject
        {
            ["schemaVersion"] = PublicationProtocol.SchemaVersion + 1,
            ["releaseNumber"] = 1,
            ["publishedAtUtc"] = ApprovedAt,
            ["records"] = new JsonArray(),
            ["withdrawals"] = new JsonArray()
        };
        Releases.Directories["seeded"] = new() { [PublicationProtocol.ManifestPath] = JsonSerializer.SerializeToUtf8Bytes(manifest) };
        Releases.Active = "seeded";
        Publications.Committed.Add(new(1, "seeded", ApprovedAt, 1));
        Publications.ActiveReleaseNumber = 1;
    }

    public static PublishReleaseRequest Request(string key, params string[] draftIds) =>
        new([.. draftIds.Select(id => new PublishedRecordReference(PublishedRecordKind.DocumentChange, id))], key);

    public static PublishReleaseRequest Request(string key, params PublishedRecordReference[] records) => new([.. records], key);

    public static PublishReleaseRequest WithNotesFor(PublishReleaseRequest request, params PublishedRecordReference[] replaced) =>
        request with { Notes = [.. replaced.Select(record => new RecordNote(record.Kind, record.RecordId, "Fixed."))] };

    public string AddDraft(char id, ImmutableArray<ReviewDecision>? decisions = null, string[]? officialIds = null,
        int citationStart = 6)
    {
        var draftId = new string(id, 32);
        var before = Extraction($"alpha\nold {id}\nomega\n", $"before-{id}");
        var after = Extraction($"alpha\nnew {id}\nomega\n", $"after-{id}");
        var comparison = DocumentComparison.Create(before, after);
        var eligible = new EligibleDocumentComparison("source", "https://example.test/",
            "https://example.test/before", "https://example.test/after", ApprovedAt.AddDays(-9), ApprovedAt.AddDays(-1), comparison);

        var revision = new DocumentChangeDraftRevision(draftId, 1, comparison.ComparisonId, "author", ApprovedAt,
            $"Headline {id}", "Summary", null, null, "City Council", null, null, [.. officialIds ?? []], ["housing"],
            [new(before.ExtractionId, citationStart, 5)]);
        var all = decisions ?? [Decision(draftId, 1, ReviewDecisionKind.Approve)];
        var review = new DocumentChangeReview(draftId, revision, all.Length, [revision], all,
            ReviewPolicy.GetUnresolvedConcerns(all));
        Reviews.Items[draftId] = new(review, eligible, before, after);
        return draftId;
    }

    public string AddStatement(char id, ImmutableArray<ReviewDecision>? decisions = null, string[]? speakerIds = null,
        int revisionNumber = 1, DateOnly? statementDate = null, string? feedUrl = null)
    {
        var candidateId = new string(id, 64);
        var extraction = Extraction($"Mayor said: housing matters.\nOther text {id}.\n", $"statement-{id}", feedUrl ?? StatementArticleUrl);
        var revision = new StatementDraftRevision(candidateId, revisionNumber, "author", ApprovedAt, $"Statement {id}", "Summary", statementDate,
            statementDate is null ? null : new SourceSpan(extraction.ExtractionId, 0, 5),
            [.. speakerIds ?? ["mayor"]], ["Council Member"], ["housing"], ["land-use"],
            [new(new(extraction.ExtractionId, 12, 15), StatementQuotationKind.DirectQuotation)], ["HB 12"]);
        var all = decisions ?? [Decision(candidateId, 1, ReviewDecisionKind.Approve, revisionNumber)];
        var concerns = ReviewPolicy.GetUnresolvedConcerns(all);
        var review = new StatementReview(candidateId, revision, all.Length, ReviewPolicy.GetCurrentStatus(revisionNumber, all, concerns),
            [revision], all, concerns);
        Statements.Publishable[candidateId] = new(review, StatementArticleUrl, extraction);
        return candidateId;
    }

    public const string StatementArticleUrl = "https://example.test/article";

    public const string ActionDetail = "Bill: 119 hr 7\nIntroduced: 2026-01-03\nPolicy area: Housing and Community Development\nSponsor: M000001\n";
    public const string ActionCosponsors = "Bill: 119 hr 7\nCosponsor: C000001, original, 2026-01-05, withdrawn 2026-01-20\n";

    // The sponsor cites the detail page and the cosponsor cites a second API page, so the record has two sources.
    public string AddAction(char id, ImmutableArray<ReviewDecision>? decisions = null, string sponsorId = "mayor",
        string? policyAreaId = "housing-development", bool personText = false)
    {
        var recordId = new string(id, 64);
        var detail = Extraction(ActionDetail, $"action-detail-{id}", "https://api.congress.gov/v3/bill/119/hr/7");
        var cosponsors = Extraction(ActionCosponsors, $"action-cosponsors-{id}", "https://api.congress.gov/v3/bill/119/hr/7/cosponsors");
        SourceSpan DetailLine(string line) => new(detail.ExtractionId, ActionDetail.IndexOf(line, StringComparison.Ordinal), line.Length);
        var revision = new ActionDraftRevision(recordId, 1, personText ? "auth0|editor" : "civic-lens:analysis", ApprovedAt, $"Action {id}",
            "The mayor sponsored a housing bill.", personText ? ReviewTextOrigin.Person : ReviewTextOrigin.Model, null, "congress-bills", 119, "hr", 7, new string('b', 64), ApprovedAt, false, new string('d', 64), null, null,
            "Housing and Community Development", DetailLine("Policy area: Housing and Community Development"), policyAreaId, ["housing"], new string('e', 64),
            [
                new(sponsorId, "M000001", ActionSponsorshipRole.Sponsor, false, new DateOnly(2026, 1, 3), null,
                    [DetailLine("Sponsor: M000001"), DetailLine("Introduced: 2026-01-03")]),
                new("chair", "C000001", ActionSponsorshipRole.Cosponsor, true, new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 20),
                    [new(cosponsors.ExtractionId, ActionCosponsors.IndexOf("Cosponsor", StringComparison.Ordinal),
                        ActionCosponsors.Length - ActionCosponsors.IndexOf("Cosponsor", StringComparison.Ordinal) - 1)])
            ], "A housing bill");
        var all = decisions ?? [Decision(recordId, 1, ReviewDecisionKind.Approve)];
        var concerns = ReviewPolicy.GetUnresolvedConcerns(all);
        var review = new ActionReview(recordId, revision, null, [revision], all.Length, ReviewPolicy.GetCurrentStatus(1, all, concerns), all, concerns);
        Actions.Publishable[recordId] = new(review, ImmutableDictionary.CreateRange(StringComparer.Ordinal,
            [KeyValuePair.Create(detail.ExtractionId, detail), KeyValuePair.Create(cosponsors.ExtractionId, cosponsors)]));
        return recordId;
    }

    public void ReviseAction(string recordId, int revisionNumber, string? approverSubject = null)
    {
        var publishable = Actions.Publishable[recordId];
        var review = publishable.Review;
        var revised = review.CurrentRevision with { RevisionNumber = revisionNumber, TrackedIssueIds = [$"issue-{revisionNumber}"] };
        Actions.Publishable[recordId] = publishable with
        {
            Review = review with
            {
                CurrentRevision = revised,
                Revisions = review.Revisions.Add(revised),
                Decisions = review.Decisions.Add(Decision(recordId, revisionNumber, ReviewDecisionKind.Approve) with
                {
                    RevisionNumber = revisionNumber,
                    ActorSubject = approverSubject ?? review.Decisions[^1].ActorSubject
                }),
                ReviewStateVersion = revisionNumber
            }
        };
    }

    public void ReviseVote(string recordId, int revisionNumber)
    {
        var publishable = Votes.Publishable[recordId];
        var review = publishable.Review;
        var revised = review.CurrentRevision with { RevisionNumber = revisionNumber, TrackedIssueIds = [$"issue-{revisionNumber}"] };
        Votes.Publishable[recordId] = publishable with
        {
            Review = review with
            {
                CurrentRevision = revised,
                Revisions = review.Revisions.Add(revised),
                Decisions = review.Decisions.Add(Decision(recordId, revisionNumber, ReviewDecisionKind.Approve) with { RevisionNumber = revisionNumber }),
                ReviewStateVersion = revisionNumber
            }
        };
    }

    public const string DefaultPartyTotal = "Party total D: Yea 1, Nay 0, Present 0, Not voting 0";
    public const string VoteDetail = "Started: 2025-04-10T11:08:00-04:00\nQuestion: On Passage\nResult: Passed\nLegislation: HR 7\n" + DefaultPartyTotal + "\n";
    public const string VoteMembers = "Member: M000001, Mayor Example, D, MA, Yea\n";

    public string AddVote(char id, ImmutableArray<ReviewDecision>? decisions = null, ReviewTextOrigin origin = ReviewTextOrigin.Model,
        string? billType = "hr", ImmutableArray<string>? billActionRecordIds = null, string[]? memberIds = null, int congress = 119,
        string[]? totalLines = null, string[]? otherMemberLines = null, Chamber chamber = Chamber.House,
        string trackedSourceId = "M000001", string cast = "Yea", string result = "Passed", VoteSource? source = null)
    {
        var recordId = new string(id, 64);
        string[] memberPersonIds = memberIds ?? ["mayor"];
        var memberLines = memberPersonIds.Select(person => $"Member: {trackedSourceId}, {OfficialNames[person]}, D, MA, {cast}").ToArray();
        var memberText = string.Concat(memberLines.Concat(otherMemberLines ?? []).Select(line => line + "\n"));
        string[] totals = totalLines ?? [DefaultPartyTotal];
        var detailText = VoteDetail[..VoteDetail.IndexOf(DefaultPartyTotal, StringComparison.Ordinal)].Replace("Result: Passed", $"Result: {result}", StringComparison.Ordinal) + string.Concat(totals.Select(line => line + "\n"));
        var detail = Extraction(detailText, $"vote-detail-{id}", "https://api.congress.gov/v3/house-vote/119/1/100");
        var members = Extraction(memberText, $"vote-members-{id}", "https://api.congress.gov/v3/house-vote/119/1/100/members");
        SourceSpan Line(DocumentExtraction extraction, string text, string line) =>
            new(extraction.ExtractionId, text.IndexOf(line, StringComparison.Ordinal), line.Length);
        VoteFact Fact(string line) => new(line, Line(detail, detailText, line));
        var revision = new VoteDraftRevision(recordId, 1, origin == ReviewTextOrigin.Model ? "civic-lens:analysis" : "auth0|editor", ApprovedAt,
            $"Vote {id}", "The House voted on passage.", origin, null, "congress-house-votes", source ?? (chamber == Chamber.Senate ? VoteSource.Voteview : VoteSource.CongressGov), chamber, congress, 1, 100, billType, billType is null ? null : 7,
            new string('b', 64), ApprovedAt,
            [Fact("Started: 2025-04-10T11:08:00-04:00"), Fact("Question: On Passage"), Fact($"Result: {result}"), Fact("Legislation: HR 7"), .. totals.Select(Fact)],
            new DateOnly(2025, 4, 10), Line(detail, detailText, "Started: 2025-04-10T11:08:00-04:00"), VoteKind.Passage,
            Line(detail, detailText, "Question: On Passage"),
            [.. memberPersonIds.Select((person, index) => new MemberVote(person, "M000001", cast, Line(members, memberText, memberLines[index])))],
            ["housing"], new string('e', 64));
        var all = decisions ?? [Decision(recordId, 1, ReviewDecisionKind.Approve)];
        var concerns = ReviewPolicy.GetUnresolvedConcerns(all);
        var review = new VoteReview(recordId, revision, null, [revision], all.Length, ReviewPolicy.GetCurrentStatus(1, all, concerns), all, concerns);
        if (billType is not null) Votes.BillActions[(congress, billType, 7)] = billActionRecordIds ?? [];
        Votes.Publishable[recordId] = new(review, ImmutableDictionary.CreateRange(StringComparer.Ordinal,
            [KeyValuePair.Create(detail.ExtractionId, detail), KeyValuePair.Create(members.ExtractionId, members)]),
            billActionRecordIds ?? []);
        return recordId;
    }

    public string AddExecutiveAction(char id, ImmutableArray<ReviewDecision>? decisions = null, bool unpublished = false, bool withNotice = false)
    {
        var recordId = new string(id, 64);
        var (extraction, evidence) = ExecutiveEvidence(withNotice ? FederalRegisterFixtures.BidenProclamation : FederalRegisterFixtures.TrumpOrder, $"executive-action-{id}");
        var extractions = new List<DocumentExtraction> { extraction };
        var notices = new List<ExecutiveActionNotice>();
        if (withNotice)
        {
            var (noticeExtraction, correction) = ExecutiveEvidence(FederalRegisterFixtures.BidenProclamationCorrection, $"executive-notice-{id}");
            extractions.Add(noticeExtraction);
            notices.Add(ExecutiveActionRevisions.NoticeOf(correction));
        }
        var revision = ExecutiveActionRevisions.Create(FederalRegisterFixtures.SourceId, ApprovedAt, evidence, notices) with { RecordId = recordId };
        var all = decisions ?? [Decision(recordId, 1, ReviewDecisionKind.Approve)];
        var concerns = ReviewPolicy.GetUnresolvedConcerns(all);
        var review = new ExecutiveActionReview(recordId, revision, null, [revision], all.Length,
            ReviewPolicy.GetCurrentStatus(1, all, concerns), all, concerns);
        ExecutiveActions.Publishable[recordId] = new(review, ImmutableDictionary.CreateRange(StringComparer.Ordinal,
            extractions.Select(item => KeyValuePair.Create(item.ExtractionId, item))));
        if (unpublished) ExecutiveActions.Unpublished.Add(new(review, null));
        return recordId;
    }

    private static (DocumentExtraction Extraction, FederalRegisterEvidence Evidence) ExecutiveEvidence(string json, string attemptId)
    {
        var document = FederalRegisterFixtures.Document(json);
        var text = FederalRegisterRenderer.Render(document);
        var extraction = Extraction(text, attemptId, $"https://www.federalregister.gov/api/v1/documents/{document.DocumentNumber}.json");
        return (extraction, new FederalRegisterEvidence(document, extraction.ExtractionId, text, "mayor"));
    }

    public string AddRelationship(char id, string statementId, string targetId, PublishedRecordKind targetKind = PublishedRecordKind.Action,
        ReviewDecisionKind decision = ReviewDecisionKind.Approve, int statementRevision = 1, int targetRevision = 1, int revisionNumber = 1, SourceSpan? quoteSpan = null)
    {
        var recordId = new string(id, 64);
        var statement = Statements.Publishable[statementId];
        var statementRevisionRecord = statement.Review.CurrentRevision!;
        var quote = quoteSpan ?? new SourceSpan(statement.Extraction.ExtractionId, 12, 15);
        var target = targetKind == PublishedRecordKind.Action
            ? RelationshipTarget.Of(Actions.Publishable[targetId].Review.CurrentRevision)
            : RelationshipTarget.Of(Votes.Publishable[targetId].Review.CurrentRevision);
        var revision = new RelationshipRevision(recordId, revisionNumber, ReviewAuthor.AnalysisSubject, ApprovedAt,
            new(RelationshipRecordKind.Statement, statementId, statementRevision), new(Enum.Parse<RelationshipRecordKind>(targetKind.ToString()), targetId, targetRevision),
            new(RelationshipMatchBasis.BillNumber, quote),
            targetKind == PublishedRecordKind.Action ? RelationshipType.DescribesOwnBill : RelationshipType.StatementAboutVote,
            $"Statement {id} describes the record.", quote, "F2", statementRevisionRecord.SpeakerOfficialIds[0]);
        var all = ImmutableArray.Create(Decision(recordId, 1, decision, revisionNumber));
        var concerns = ReviewPolicy.GetUnresolvedConcerns(all);
        var review = new RelationshipReview(recordId, revision, [revision], 1, ReviewPolicy.GetCurrentStatus(revisionNumber, all, concerns), all,
            concerns, false);
        Relationships.Publishable[recordId] = new(review, new ApprovedStatement(statementRevisionRecord, statement.Extraction.Text), target, false);
        return recordId;
    }

    public string AddBrief(DateOnly week, PublishedRevisionBinding[] cited, ReviewDecisionKind decision = ReviewDecisionKind.Approve)
    {
        var recordId = BriefWeek.RecordIdOf(week);
        var citations = cited.Select((binding, index) => new BriefCitation($"R{index + 1}",
            Enum.Parse<BriefCitationKind>(binding.Kind.ToString()), binding.RecordId, binding.RevisionNumber)).ToImmutableArray();
        var revision = new BriefRevision(recordId, 1, ReviewAuthor.AnalysisSubject, ApprovedAt, BriefFixtures.Window() with { WeekStart = week },
            BriefFixtures.Fingerprint, $"Brief of {week:yyyy-MM-dd}", ReviewTextOrigin.Model, null,
            [.. citations.Select(citation => new BriefParagraph($"Paragraph for {citation.Id}.", [citation.Id]))], citations,
            new(citations.Length + 2, citations.Length + 1, citations.Length));
        var all = ImmutableArray.Create(Decision(recordId, 1, decision));
        var concerns = ReviewPolicy.GetUnresolvedConcerns(all);
        var review = new BriefReview(recordId, revision, [revision], 1, ReviewPolicy.GetCurrentStatus(1, all, concerns), all, concerns);
        Briefs.Publishable[recordId] = new(review, citations.ToImmutableDictionary(citation => citation.Id,
            citation => new BriefCitedRecord($"Cited {citation.Id}", "Summary", false), StringComparer.Ordinal));
        return recordId;
    }

    public static ReviewDecision Decision(string draftId, int stateVersion, ReviewDecisionKind kind, int revisionNumber = 1) =>
        new(new string((char)('a' + stateVersion), 32), draftId, revisionNumber, stateVersion, kind, "reviewer",
            kind == ReviewDecisionKind.Approve ? null : "Needs work.", [], ApprovedAt);

    internal static DocumentExtraction Extraction(string text, string attemptId, string url = "https://example.test/") => new(
        new(attemptId, "source", url, url, DateTimeOffset.UnixEpoch,
            new CollectionResponse(200, null, null, "text/html", []), new CaptureIdentity(new string('a', 64), 0)),
        "extraction", text);
}

internal sealed class FakeRenderer : IReleaseRenderer
{
    public List<string>? Events { get; init; }
    public List<ReleaseOfficialPage>? OfficialPages { get; init; }
    public IReadOnlyList<ReleaseAsset> Assets { get; } = [new("assets/site.css", [1, 2, 3])];
    public PublicBaseUrl? PublicBaseUrl { get; init; }
    public Task<string> RenderNotFoundAsync(CancellationToken cancellationToken) => Task.FromResult("<html>not found</html>");
    public Task<string> RenderRecordAsync(PublishedDocumentChange record, PublicationCatalog catalog, CancellationToken cancellationToken) =>
        Task.FromResult($"<html>{record.RecordId}</html>");
    public Task<string> RenderStatementAsync(PublishedStatement record, ImmutableArray<PublishedRelationship> related, PublicationCatalog catalog,
        CancellationToken cancellationToken) =>
        Logged($"render {record.RecordId[..1]}", $"<html>statement {record.RecordId}{Related(related)}</html>");
    public Task<string> RenderActionAsync(PublishedAction record, ImmutableArray<PublishedRelationship> related, PublicationCatalog catalog,
        CancellationToken cancellationToken) =>
        Logged($"render {record.RecordId[..1]}", $"<html>action {record.RecordId}{Related(related)}{Corrections(record.Corrections)}</html>");
    public Task<string> RenderVoteAsync(PublishedVote record, string? billActionRecordId, ImmutableArray<PublishedRelationship> related,
        PublicationCatalog catalog, CancellationToken cancellationToken) =>
        Task.FromResult($"<html>vote {record.RecordId} bill {billActionRecordId}{Related(related)}</html>");
    public Task<string> RenderExecutiveActionAsync(PublishedExecutiveAction record, CancellationToken cancellationToken) =>
        Task.FromResult($"<html>executive action {record.RecordId}</html>");
    public Task<string> RenderRelationshipAsync(PublishedRelationship record, PublicationCatalog catalog, CancellationToken cancellationToken) =>
        Task.FromResult($"<html>relationship {record.RecordId}</html>");
    public Task<string> RenderBriefAsync(PublishedBrief record, CancellationToken cancellationToken) =>
        Task.FromResult($"<html>brief {record.RecordId}</html>");
    public Task<string> RenderWithdrawalAsync(PublishedWithdrawal withdrawal, CancellationToken cancellationToken) =>
        Task.FromResult($"<html>withdrawn {withdrawal.RecordId} {withdrawal.Note}{PublicBaseUrl?.Join(string.Empty)}</html>");
    public Task<string> RenderHomeAsync(PublicationRelease release, PublishedBrief? brief, ReleaseFacets facets,
        CancellationToken cancellationToken) =>
        Task.FromResult($"<html>home {release.Records.Length} brief {brief?.RecordId}</html>");
    public Task<string> RenderAllRecordsAsync(PublicationRelease release, int page, CancellationToken cancellationToken) =>
        Task.FromResult($"<html>index {page} {release.Records.Length}</html>");
    public Task<string> RenderBriefsAsync(PublicationRelease release, PublishedRecordEntry[] briefs, int page,
        CancellationToken cancellationToken) =>
        Task.FromResult($"<html>briefs {page} {string.Join(',', briefs.Select(entry => entry.RecordId))}</html>");
    public Task<string> RenderFacetPageAsync(ReleaseFacet facet, int page, CancellationToken cancellationToken) =>
        Task.FromResult($"<html>facet {facet.Name} {page} {string.Join(',', facet.Records.Select(entry => entry.RecordId))}</html>");
    public Task<string> RenderFacetsAsync(ReleaseFacets facets, CancellationToken cancellationToken) =>
        Task.FromResult($"<html>facets {facets.Issues.Length} {facets.PolicyAreas.Length}</html>");
    public Task<string> RenderOfficialAsync(ReleaseOfficialPage page, PublicationCatalog catalog, CancellationToken cancellationToken)
    {
        OfficialPages?.Add(page);
        return Task.FromResult($"<html>official {page.Profile.Id} {(page.IsTracked ? "tracked" : "untracked")} {(page.IsCurrent ? "current" : "past")} {page.Congress} past [{string.Join(',', page.PastCongresses)}] records [{string.Join(',', page.Records.Select(entry => entry.RecordId))}] votes [{string.Join(',', page.Votes.Select(vote => vote.RecordId))}]</html>");
    }
    public Task<string> RenderOfficialsAsync(IReadOnlyList<OfficialListing> officials, DateTimeOffset publishedAtUtc, CancellationToken cancellationToken) =>
        Task.FromResult($"<html>officials {string.Join(',', officials.Select(official => $"{official.Profile.Id}:{official.RecordCount}"))}</html>");
    public Task<string> RenderReportAsync(CancellationToken cancellationToken) =>
        Task.FromResult("<html>report</html>");
    public Task<string> RenderMethodAsync(CancellationToken cancellationToken) =>
        Task.FromResult("<html>method</html>");
    public Task<string> RenderAboutAsync(CancellationToken cancellationToken) =>
        Task.FromResult("<html>about</html>");
    public Task<string> RenderSearchAsync(PublicationRelease release, ReleaseFacets facets, CancellationToken cancellationToken) =>
        Task.FromResult($"<html>search {release.Records.Length} {facets.Issues.Length}</html>");

    private Task<string> Logged(string entry, string page)
    {
        Events?.Add(entry);
        return Task.FromResult(page);
    }

    private static string Corrections(PublishedCorrection[] corrections) => corrections.Length == 0 ? "" : $" corrections {corrections.Length}";

    private static string Related(ImmutableArray<PublishedRelationship> related) =>
        related.IsEmpty ? "" : " related " + string.Join(' ', related.Select(link => link.RecordId[..1]));
}

internal sealed class FakePublicationStore : IPublicationStore
{
    private readonly Dictionary<(string Subject, string Key), (string Hash, PublicationReleaseSummary Summary)> receipts = [];

    public List<PublicationReleaseSummary> Committed { get; } = [];
    public PublicationCommit? LastCommit { get; private set; }
    public bool ConflictOnCommit { get; set; }
    public Exception? FailOnCommit { get; set; }
    public PublicationReleaseSummary? ConcurrentWinner { get; set; }

    public Dictionary<PublishedRecordReference, ReviewStatus> ReviewStatuses { get; } = [];
    public int? ActiveReleaseNumber { get; set; }
    public Action? BeforeServe { get; set; }
    public Exception? FailOnPrune { get; set; }
    public bool InsidePrune { get; private set; }

    public Task<PublicationState> GetStateAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new PublicationState(Committed.Select(item => item.ReleaseNumber).DefaultIfEmpty().Max(),
            Committed.SingleOrDefault(item => item.ReleaseNumber == ActiveReleaseNumber)));

    public async Task<PublicationReleaseSummary> ActivateAsync(int releaseNumber, Func<string, CancellationToken, Task> requireFiles,
        CancellationToken cancellationToken)
    {
        var summary = Committed.SingleOrDefault(item => item.ReleaseNumber == releaseNumber)
            ?? throw new ArgumentException($"Release {releaseNumber} has not been committed.");
        await requireFiles(summary.DirectoryName, cancellationToken);
        ActiveReleaseNumber = releaseNumber;
        return summary;
    }

    public async Task<PublicationReleaseSummary?> ServeActiveAsync(Func<string, CancellationToken, Task> serve,
        CancellationToken cancellationToken)
    {
        BeforeServe?.Invoke();
        var active = Committed.SingleOrDefault(item => item.ReleaseNumber == ActiveReleaseNumber);
        if (active is not null) await serve(active.DirectoryName, cancellationToken);
        return active;
    }

    public async Task PruneAsync(int keep, Func<int, string?, CancellationToken, Task> retireOlder, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailOnPrune is not null) throw FailOnPrune;
        if (Committed.Count == 0) return;
        var lowestKept = Committed.OrderByDescending(item => item.ReleaseNumber).Take(keep).Min(item => item.ReleaseNumber);
        InsidePrune = true;
        try
        {
            await retireOlder(lowestKept, Committed.SingleOrDefault(item => item.ReleaseNumber == ActiveReleaseNumber)?.DirectoryName,
                cancellationToken);
        }
        finally
        {
            InsidePrune = false;
        }
    }

    public Task<IReadOnlyDictionary<PublishedRecordReference, ReviewStatus>> ReadReviewStatusesAsync(
        IReadOnlyCollection<PublishedRecordReference> records, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<PublishedRecordReference, ReviewStatus>>(
            records.Where(ReviewStatuses.ContainsKey).ToDictionary(record => record, record => ReviewStatuses[record]));

    public Task<IReadOnlyList<PublicationReleaseSummary>> ListAsync(int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PublicationReleaseSummary>>([.. Committed.AsEnumerable().Reverse().Take(limit)]);

    public Task<PublicationReleaseSummary?> FindReplayAsync(string actorSubject, string idempotencyKey,
        string payloadHash, CancellationToken cancellationToken)
    {
        if (!receipts.TryGetValue((actorSubject, idempotencyKey), out var receipt)) return Task.FromResult<PublicationReleaseSummary?>(null);
        if (receipt.Hash != payloadHash) throw new ArgumentException("Key reused with a different request.");
        return Task.FromResult<PublicationReleaseSummary?>(receipt.Summary);
    }

    public Task<PublicationReleaseSummary> CommitAsync(string actorSubject, PublicationCommit commit,
        string idempotencyKey, string payloadHash, CancellationToken cancellationToken)
    {
        if (ConflictOnCommit) throw new PublicationConflictException("Review state changed.");
        if (FailOnCommit is not null) throw FailOnCommit;
        if (ConcurrentWinner is not null) return Task.FromResult(ConcurrentWinner);
        LastCommit = commit;
        var summary = new PublicationReleaseSummary(commit.ReleaseNumber, commit.DirectoryName, DateTimeOffset.UnixEpoch, commit.Records.Length);
        Committed.Add(summary);
        ActiveReleaseNumber = summary.ReleaseNumber;
        receipts[(actorSubject, idempotencyKey)] = (payloadHash, summary);
        return Task.FromResult(summary);
    }

    public void SeedReplay(string subject, string key, string hash, PublicationReleaseSummary summary) =>
        receipts[(subject, key)] = (hash, summary);
}

internal sealed class FakeReleaseDirectory : IReleaseDirectory
{
    public Dictionary<string, Dictionary<string, byte[]>> Directories { get; } = [];
    public List<string> WriteOrder { get; } = [];
    public List<(string SourceDirectory, string RelativePath)> Links { get; } = [];
    public List<string> ReadPaths { get; } = [];
    public List<string>? Events { get; init; }
    public List<string> Deleted { get; } = [];
    public List<string> Activated { get; } = [];
    public int BeginCount { get; private set; }
    public int DiscardedStagings { get; set; }
    public string? Active { get; set; }
    public Exception? FailOnDelete { get; set; }
    public Exception? FailOnDeleteRetired { get; set; }
    public List<string> Retired { get; } = [];
    public List<string> RetiredEver { get; } = [];
    public Action? OnDeleteRetired { get; set; }
    public Exception? FailOnActivate { get; set; }

    public Task<IReleaseStaging> BeginAsync(CancellationToken cancellationToken)
    {
        BeginCount++;
        return Task.FromResult<IReleaseStaging>(new Staging(this));
    }

    public Task<byte[]> ReadFileAsync(string directoryName, string relativePath, int maximumBytes,
        CancellationToken cancellationToken)
    {
        ReadPaths.Add(relativePath);
        Events?.Add($"read {relativePath.Split('/', '.')[^2][..1]}");
        var content = Directories.TryGetValue(directoryName, out var directory) && directory.TryGetValue(relativePath, out var file)
            ? file
            : throw new FileNotFoundException("Release file was not found.", relativePath);
        if (content.Length > maximumBytes) throw new InvalidDataException("Release file exceeds the allowed size.");
        return Task.FromResult(content);
    }

    public Task<IReadOnlyList<string>> ListFilesAsync(string directoryName, string folder, int maximumFiles,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> files = Directories.TryGetValue(directoryName, out var directory)
            ? [.. directory.Keys.Where(path => path.StartsWith(folder + "/", StringComparison.Ordinal) && !path[(folder.Length + 1)..].Contains('/'))
                .Order(StringComparer.Ordinal)]
            : [];
        if (files.Count > maximumFiles) throw new InvalidDataException("Release folder holds more files than allowed.");
        return Task.FromResult(files);
    }

    public Task RetireOlderThanAsync(int releaseNumber, IReadOnlyCollection<string> keepDirectoryNames,
        CancellationToken cancellationToken)
    {
        foreach (var name in Directories.Keys.Where(name => NumberOf(name) < releaseNumber && !keepDirectoryNames.Contains(name)).ToList())
        {
            Directories.Remove(name);
            Retired.Add(name);
            RetiredEver.Add(name);
        }

        return Task.CompletedTask;
    }

    public Task DeleteRetiredAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OnDeleteRetired?.Invoke();
        if (FailOnDeleteRetired is not null) throw FailOnDeleteRetired;
        Retired.Clear();
        return Task.CompletedTask;
    }

    private static int NumberOf(string directoryName) => int.Parse(directoryName.Split('-')[1]);

    public Task DeleteAsync(string directoryName, CancellationToken cancellationToken)
    {
        if (FailOnDelete is not null) throw FailOnDelete;
        Directories.Remove(directoryName);
        Deleted.Add(directoryName);
        return Task.CompletedTask;
    }

    public Task ActivateAsync(string directoryName, CancellationToken cancellationToken)
    {
        if (FailOnActivate is not null) throw FailOnActivate;
        Active = directoryName;
        Activated.Add(directoryName);
        return Task.CompletedTask;
    }

    public Task<string?> GetActiveAsync(CancellationToken cancellationToken) => Task.FromResult(Active);

    private sealed class Staging(FakeReleaseDirectory owner) : IReleaseStaging
    {
        private readonly Dictionary<string, byte[]> files = [];
        private bool completed;

        public Task WriteFileAsync(string relativePath, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        {
            files[relativePath] = content.ToArray();
            owner.WriteOrder.Add(relativePath);
            return Task.CompletedTask;
        }

        public Task LinkFileAsync(string sourceDirectoryName, string relativePath, CancellationToken cancellationToken)
        {
            files[relativePath] = owner.Directories[sourceDirectoryName][relativePath];
            owner.Links.Add((sourceDirectoryName, relativePath));
            return Task.CompletedTask;
        }

        public Task<string> CompleteAsync(int releaseNumber, CancellationToken cancellationToken)
        {
            completed = true;
            var name = $"release-{releaseNumber}";
            owner.Directories[name] = files;
            return Task.FromResult(name);
        }

        public ValueTask DisposeAsync()
        {
            if (!completed) owner.DiscardedStagings++;
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class FakeReviewStore : IDocumentChangeReviewStore
{
    public Dictionary<string, PublishableDocumentChange> Items { get; } = [];
    public int ReadCount { get; private set; }

    public Task<IReadOnlyDictionary<string, PublishableDocumentChange>> GetForPublicationAsync(
        IReadOnlyCollection<string> draftIds, CancellationToken cancellationToken)
    {
        ReadCount++;
        IReadOnlyDictionary<string, PublishableDocumentChange> found = draftIds.Where(Items.ContainsKey)
            .ToDictionary(id => id, id => Items[id]);
        return Task.FromResult(found);
    }

    public Task<DocumentChangeReview?> GetAsync(string draftId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<DocumentChangeDraftRevision> CreateAsync(string actorSubject, DocumentChangeDraftRevision revision,
        string idempotencyKey, string payloadHash, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<DocumentChangeDraftRevision> SaveRevisionAsync(string actorSubject, DocumentChangeDraftRevision revision,
        int expectedRevisionNumber, bool changeDateChecked, string idempotencyKey, string payloadHash,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision, int expectedRevisionNumber,
        int expectedReviewStateVersion, string idempotencyKey, string payloadHash,
        ImmutableHashSet<string> allowedOfficialIds, ImmutableHashSet<string> allowedIssueIds,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<DocumentChangeReviewPage> ListAsync(PageCursor? cursor, int limit, DraftStatusFilter filter,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<NewDocumentChangePage> ListEligibleComparisonsAsync(PageCursor? cursor, int limit,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ReviewOverview> GetOverviewAsync(CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public List<UnpublishedApprovedDraft> Unpublished { get; } = [];
    public int UnpublishedLimit { get; private set; }
    public int UnpublishedReadCount { get; private set; }

    public Task<IReadOnlyList<UnpublishedApprovedDraft>> ListUnpublishedApprovedDraftsAsync(int limit,
        CancellationToken cancellationToken)
    {
        UnpublishedReadCount++;
        UnpublishedLimit = limit;
        return Task.FromResult<IReadOnlyList<UnpublishedApprovedDraft>>([.. Unpublished.Take(limit)]);
    }

    public List<ReviewActivityEvent> Activity { get; } = [];

    public Task<IReadOnlyList<ReviewActivityEvent>> ListRecentActivityAsync(int limit,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ReviewActivityEvent>>(Activity);
}
