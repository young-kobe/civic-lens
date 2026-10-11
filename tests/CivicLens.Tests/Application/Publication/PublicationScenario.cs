using CivicLens.Application.Paging;
using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
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
    public Dictionary<string, string> OfficialNames { get; } = new() { ["mayor"] = "Mayor Example" };

    public PublishDocumentChanges Publisher() => new(Publications, Releases, new FakeRenderer(), Reviews,
        new PublicationCatalog(OfficialNames), Clock);

    public void SeedRelease(int recordCount, bool withFiles)
    {
        var entries = Enumerable.Range(0, recordCount).Select(index => new PublishedRecordEntry
        {
            RecordId = index.ToString("x32"),
            RevisionNumber = 1,
            Headline = $"Seeded {index}",
            FirstPublishedAtUtc = ApprovedAt.AddMinutes(-index)
        }).ToArray();
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

    public static PublishDocumentChangesRequest Request(string key, params string[] draftIds) => new([.. draftIds], key);

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
            DocumentChangeReviewPolicy.GetUnresolvedConcerns(all));
        Reviews.Items[draftId] = new(review, eligible, before, after);
        return draftId;
    }

    public static ReviewDecision Decision(string draftId, int stateVersion, ReviewDecisionKind kind) =>
        new(new string((char)('a' + stateVersion), 32), draftId, 1, stateVersion, kind, "reviewer",
            kind == ReviewDecisionKind.Approve ? null : "Needs work.", [], ApprovedAt);

    private static DocumentExtraction Extraction(string text, string attemptId) => new(
        new(attemptId, "source", "https://example.test/", "https://example.test/", DateTimeOffset.UnixEpoch,
            new CollectionResponse(200, null, null, "text/html", []), new CaptureIdentity(new string('a', 64), 0)),
        "parser", "normalizer", text);
}

internal sealed class FakeRenderer : IReleaseRenderer
{
    public IReadOnlyList<ReleaseAsset> Assets { get; } = [new("assets/site.css", [1, 2, 3])];
    public Task<string> RenderRecordAsync(PublishedDocumentChange record, CancellationToken cancellationToken) =>
        Task.FromResult($"<html>{record.RecordId}</html>");
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

    public Task<EligibleDocumentComparisonPage> ListEligibleComparisonsAsync(PageCursor? cursor, int limit,
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
