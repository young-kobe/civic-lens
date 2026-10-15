using CivicLens.Application.Paging;
using System.Collections.Immutable;
using CivicLens.Application.Actions;
using System.Text.Json;
using System.Text.Json.Nodes;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Core.Actions;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using CivicLens.Tests.Application.Actions;
using CivicLens.Tests.Application.Statements;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Publication;

internal sealed class PublicationScenario
{
    public static readonly ReviewActor Owner = new("owner", ReviewRole.Owner);
    public static readonly ReviewActor Reviewer = new("reviewer", ReviewRole.Reviewer);
    public static readonly DateTimeOffset ApprovedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public FakeClock Clock { get; } = new(new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
    public FakePublicationStore Publications { get; } = new();
    public FakeReleaseDirectory Releases { get; } = new();
    public FakeReviewStore Reviews { get; } = new();
    public FakeStatementReviewStore Statements { get; } = new();
    public FakeActionReviewStore Actions { get; } = new();
    public Dictionary<string, string> OfficialNames { get; } = new() { ["mayor"] = "Mayor Example", ["chair"] = "Council Chair Example" };

    public PublishedRecordLoaders Loaders() => new(Reviews, Statements, Actions);

    public PublishRelease Publisher() => new(Publications, Releases, new FakeRenderer(), Loaders(),
        new PublicationCatalog(OfficialNames), Clock);

    private const string PunctuatedHeadline = "Mayor\u2019s \u201Chousing\u201D plan: caf\u00E9 owners, r\u00E9sum\u00E9 rules; it\u2019s 100% ready? ";

    public static PublishedRecordEntry FilledStatementEntry(int index, int headlineLength, int extraIssueIds = 0) => new()
    {
        Kind = PublishedRecordKind.Statement,
        RecordId = index.ToString("x64"),
        RevisionNumber = 1,
        Headline = string.Concat(Enumerable.Repeat(PunctuatedHeadline, headlineLength / PunctuatedHeadline.Length + 1))[..headlineLength],
        FirstPublishedAtUtc = ApprovedAt.AddMinutes(-index),
        Officials = [new() { Id = "mayor", Name = "Mayor Example" }, new() { Id = "council-chair", Name = "Council Chair Example" }],
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
        SourceHost = "api.congress.gov"
    };

    public void SeedRelease(int recordCount, bool withFiles) =>
        SeedRelease(withFiles, [.. Enumerable.Range(0, recordCount).Select(index => new PublishedRecordEntry
        {
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
            Records = entries
        };
        var files = new Dictionary<string, byte[]>
        {
            [PublicationProtocol.ManifestPath] = JsonSerializer.SerializeToUtf8Bytes(manifest, PublicationProtocol.JsonOptions)
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

    public void SeedVersionOneRelease(string draftId)
    {
        var manifest = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["releaseNumber"] = 1,
            ["publishedAtUtc"] = ApprovedAt,
            ["records"] = new JsonArray(new JsonObject
            {
                ["recordId"] = draftId,
                ["revisionNumber"] = 1,
                ["headline"] = "Seeded by version one",
                ["firstPublishedAtUtc"] = ApprovedAt
            })
        };
        Releases.Directories["seeded"] = new()
        {
            [PublicationProtocol.ManifestPath] = JsonSerializer.SerializeToUtf8Bytes(manifest),
            [PublicationProtocol.RecordDataPath(draftId)] = [],
            [PublicationProtocol.RecordPagePath(draftId)] = []
        };
        Releases.Active = "seeded";
        Publications.Committed.Add(new(1, "seeded", ApprovedAt, 1));
        Publications.ActiveReleaseNumber = 1;
    }

    public static PublishReleaseRequest Request(string key, params string[] draftIds) =>
        new([.. draftIds.Select(id => new PublishedRecordReference(PublishedRecordKind.DocumentChange, id))], key);

    public static PublishReleaseRequest Request(string key, params PublishedRecordReference[] records) => new([.. records], key);

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

    public string AddStatement(char id, ImmutableArray<ReviewDecision>? decisions = null, string[]? speakerIds = null)
    {
        var candidateId = new string(id, 64);
        var extraction = Extraction($"Mayor said: housing matters.\nOther text {id}.\n", $"statement-{id}");
        var revision = new StatementDraftRevision(candidateId, 1, "author", ApprovedAt, $"Statement {id}", "Summary", null, null,
            [.. speakerIds ?? ["mayor"]], ["Council Member"], ["housing"], ["land-use"],
            [new(new(extraction.ExtractionId, 12, 15), StatementQuotationKind.DirectQuotation)], ["HB 12"]);
        var all = decisions ?? [Decision(candidateId, 1, ReviewDecisionKind.Approve)];
        var concerns = ReviewPolicy.GetUnresolvedConcerns(all);
        var review = new StatementReview(candidateId, revision, all.Length, ReviewPolicy.GetCurrentStatus(1, all, concerns),
            [revision], all, concerns);
        Statements.Publishable[candidateId] = new(review, "https://example.test/article", extraction);
        return candidateId;
    }

    public const string ActionDetail = "Bill: 119 hr 7\nIntroduced: 2026-01-03\nPolicy area: Housing and Community Development\nSponsor: M000001\n";
    public const string ActionCosponsors = "Bill: 119 hr 7\nCosponsor: C000001, original, 2026-01-05, withdrawn 2026-01-20\n";

    // The sponsor cites the detail page and the cosponsor cites a second API page, so the record has two sources.
    public string AddAction(char id, ImmutableArray<ReviewDecision>? decisions = null, string sponsorId = "mayor",
        string? policyAreaId = "housing-development")
    {
        var recordId = new string(id, 64);
        var detail = Extraction(ActionDetail, $"action-detail-{id}", "https://api.congress.gov/v3/bill/119/hr/7");
        var cosponsors = Extraction(ActionCosponsors, $"action-cosponsors-{id}", "https://api.congress.gov/v3/bill/119/hr/7/cosponsors");
        StatementSpan DetailLine(string line) => new(detail.ExtractionId, ActionDetail.IndexOf(line, StringComparison.Ordinal), line.Length);
        var revision = new ActionDraftRevision(recordId, 1, "civic-lens:analysis", ApprovedAt, $"Action {id}", "The mayor sponsored a housing bill.",
            ActionTextOrigin.Model, null, "congress-bills", 119, "hr", 7, new string('b', 64), ApprovedAt, false, new string('d', 64), null, null,
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

    public static ReviewDecision Decision(string draftId, int stateVersion, ReviewDecisionKind kind) =>
        new(new string((char)('a' + stateVersion), 32), draftId, 1, stateVersion, kind, "reviewer",
            kind == ReviewDecisionKind.Approve ? null : "Needs work.", [], ApprovedAt);

    internal static DocumentExtraction Extraction(string text, string attemptId, string url = "https://example.test/") => new(
        new(attemptId, "source", url, url, DateTimeOffset.UnixEpoch,
            new CollectionResponse(200, null, null, "text/html", []), new CaptureIdentity(new string('a', 64), 0)),
        "parser", "normalizer", text);
}

internal sealed class FakeRenderer : IReleaseRenderer
{
    public IReadOnlyList<ReleaseAsset> Assets { get; } = [new("assets/site.css", [1, 2, 3])];
    public Task<string> RenderRecordAsync(PublishedDocumentChange record, CancellationToken cancellationToken) =>
        Task.FromResult($"<html>{record.RecordId}</html>");
    public Task<string> RenderStatementAsync(PublishedStatement record, CancellationToken cancellationToken) =>
        Task.FromResult($"<html>statement {record.RecordId}</html>");
    public Task<string> RenderActionAsync(PublishedAction record, CancellationToken cancellationToken) =>
        Task.FromResult($"<html>action {record.RecordId}</html>");
    public Task<string> RenderIndexAsync(PublicationRelease release, int page, CancellationToken cancellationToken) =>
        Task.FromResult($"<html>index {page} {release.Records.Length}</html>");
}

internal sealed class FakePublicationStore : IPublicationStore
{
    private readonly Dictionary<(string Subject, string Key), (string Hash, PublicationReleaseSummary Summary)> receipts = [];

    public List<PublicationReleaseSummary> Committed { get; } = [];
    public PublicationCommit? LastCommit { get; private set; }
    public bool ConflictOnCommit { get; set; }
    public Exception? FailOnCommit { get; set; }
    public PublicationReleaseSummary? ConcurrentWinner { get; set; }

    public int? ActiveReleaseNumber { get; set; }
    public Action? BeforeServe { get; set; }

    public Task<PublicationState> GetStateAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new PublicationState(Committed.Select(item => item.ReleaseNumber).DefaultIfEmpty().Max(),
            Committed.SingleOrDefault(item => item.ReleaseNumber == ActiveReleaseNumber)));

    public Task<PublicationReleaseSummary> ActivateAsync(int releaseNumber, CancellationToken cancellationToken)
    {
        var summary = Committed.SingleOrDefault(item => item.ReleaseNumber == releaseNumber)
            ?? throw new ArgumentException($"Release {releaseNumber} has not been committed.");
        ActiveReleaseNumber = releaseNumber;
        return Task.FromResult(summary);
    }

    public async Task<PublicationReleaseSummary?> ServeActiveAsync(Func<string, CancellationToken, Task> serve,
        CancellationToken cancellationToken)
    {
        BeforeServe?.Invoke();
        var active = Committed.SingleOrDefault(item => item.ReleaseNumber == ActiveReleaseNumber);
        if (active is not null) await serve(active.DirectoryName, cancellationToken);
        return active;
    }

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
        var summary = new PublicationReleaseSummary(commit.ReleaseNumber, commit.DirectoryName, commit.PublishedAtUtc, commit.Records.Length);
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
    public List<string> Deleted { get; } = [];
    public List<string> Activated { get; } = [];
    public int BeginCount { get; private set; }
    public int DiscardedStagings { get; set; }
    public string? Active { get; set; }
    public Exception? FailOnDelete { get; set; }
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
        return Task.FromResult(Directories[directoryName][relativePath]);
    }

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
