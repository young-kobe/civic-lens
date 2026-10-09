using System.Collections.Immutable;
using CivicLens.Application.Documents;
using CivicLens.Application.Publication;
using CivicLens.Application.Review;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

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
    public FakeExtractionStore Extractions { get; } = new();
    public Dictionary<string, string> OfficialNames { get; } = new() { ["mayor"] = "Mayor Example" };

    public PublishDocumentChanges Publisher() => new(Publications, Releases, new FakeRenderer(), Reviews, Extractions,
        new PublicationCatalog(OfficialNames), Clock);

    public static PublishDocumentChangesRequest Request(string key, params string[] draftIds) => new([.. draftIds], key);

    public string AddDraft(char id, ImmutableArray<ReviewDecision>? decisions = null, string[]? officialIds = null,
        int citationStart = 6)
    {
        var draftId = new string(id, 32);
        var before = Extraction($"alpha\nold {id}\nomega\n", $"before-{id}");
        var after = Extraction($"alpha\nnew {id}\nomega\n", $"after-{id}");
        var comparison = DocumentComparison.Create(before, after);
        Extractions.Items[before.ExtractionId] = before;
        Extractions.Items[after.ExtractionId] = after;
        Reviews.Comparisons[comparison.ComparisonId] = new EligibleDocumentComparison("source", "https://example.test/",
            "https://example.test/before", "https://example.test/after", ApprovedAt.AddDays(-9), ApprovedAt.AddDays(-1), comparison);

        var revision = new DocumentChangeDraftRevision(draftId, 1, comparison.ComparisonId, "author", ApprovedAt,
            $"Headline {id}", "Summary", null, null, "City Council", null, null, [.. officialIds ?? []], ["housing"],
            [new(before.ExtractionId, citationStart, 5)]);
        var all = decisions ?? [Decision(draftId, 1, ReviewDecisionKind.Approve)];
        Reviews.Items[draftId] = new(draftId, revision, all.Length, [revision], all,
            DocumentChangeReviewPolicy.GetUnresolvedConcerns(all));
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

internal sealed class FakeClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class FakeRenderer : IReleaseRenderer
{
    public IReadOnlyList<ReleaseAsset> Assets { get; } = [new("assets/site.css", [1, 2, 3])];
    public Task<string> RenderRecordAsync(PublishedDocumentChange record, CancellationToken cancellationToken) =>
        Task.FromResult($"<html>{record.RecordId}</html>");
    public Task<string> RenderIndexAsync(PublicationRelease release, CancellationToken cancellationToken) =>
        Task.FromResult($"<html>index {release.Records.Length}</html>");
}

internal sealed class FakePublicationStore : IPublicationStore
{
    private readonly Dictionary<(string Subject, string Key), (string Hash, PublicationReleaseSummary Summary)> receipts = [];

    public List<PublicationReleaseSummary> Committed { get; } = [];
    public PublicationCommit? LastCommit { get; private set; }
    public bool ConflictOnCommit { get; set; }

    public Task<PublicationReleaseSummary?> GetLatestAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Committed.LastOrDefault());

    public Task<PublicationReleaseSummary?> GetAsync(int releaseNumber, CancellationToken cancellationToken) =>
        Task.FromResult(Committed.FirstOrDefault(item => item.ReleaseNumber == releaseNumber));

    public Task<IReadOnlyList<PublicationReleaseSummary>> ListAsync(int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PublicationReleaseSummary>>([.. Committed.AsEnumerable().Reverse().Take(limit)]);

    public Task<PublicationReleaseSummary?> FindReplayAsync(string actorSubject, string idempotencyKey,
        string payloadHash, CancellationToken cancellationToken)
    {
        if (!receipts.TryGetValue((actorSubject, idempotencyKey), out var receipt)) return Task.FromResult<PublicationReleaseSummary?>(null);
        if (receipt.Hash != payloadHash) throw new PublicationConflictException("Key reused with a different request.");
        return Task.FromResult<PublicationReleaseSummary?>(receipt.Summary);
    }

    public Task<PublicationReleaseSummary> CommitAsync(string actorSubject, PublicationCommit commit,
        string idempotencyKey, string payloadHash, CancellationToken cancellationToken)
    {
        if (ConflictOnCommit) throw new PublicationConflictException("Review state changed.");
        LastCommit = commit;
        var summary = new PublicationReleaseSummary(commit.ReleaseNumber, commit.DirectoryName, commit.PublishedAtUtc, commit.Records.Length);
        Committed.Add(summary);
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
    public List<string> Deleted { get; } = [];
    public List<string> Activated { get; } = [];
    public int BeginCount { get; private set; }
    public int DiscardedStagings { get; set; }
    public string? Active { get; private set; }

    public Task<IReleaseStaging> BeginAsync(CancellationToken cancellationToken)
    {
        BeginCount++;
        return Task.FromResult<IReleaseStaging>(new Staging(this));
    }

    public Task<byte[]> ReadFileAsync(string directoryName, string relativePath, int maximumBytes,
        CancellationToken cancellationToken) => Task.FromResult(Directories[directoryName][relativePath]);

    public Task DeleteAsync(string directoryName, CancellationToken cancellationToken)
    {
        Directories.Remove(directoryName);
        Deleted.Add(directoryName);
        return Task.CompletedTask;
    }

    public Task ActivateAsync(string directoryName, CancellationToken cancellationToken)
    {
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
    public Dictionary<string, DocumentChangeReview> Items { get; } = [];
    public Dictionary<string, EligibleDocumentComparison> Comparisons { get; } = [];
    public int ReadCount { get; private set; }

    public Task<DocumentChangeReview?> GetAsync(string draftId, CancellationToken cancellationToken)
    {
        ReadCount++;
        return Task.FromResult(Items.GetValueOrDefault(draftId));
    }

    public Task<EligibleDocumentComparison?> GetEligibleComparisonAsync(string comparisonId, CancellationToken cancellationToken) =>
        Task.FromResult(Comparisons.GetValueOrDefault(comparisonId));

    public Task<DocumentChangeDraftRevision> CreateAsync(string actorSubject, DocumentChangeDraftRevision revision,
        string idempotencyKey, string payloadHash, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<DocumentChangeDraftRevision> SaveRevisionAsync(string actorSubject, DocumentChangeDraftRevision revision,
        int expectedRevisionNumber, string idempotencyKey, string payloadHash, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<ReviewDecision> DecideAsync(string actorSubject, ReviewDecision decision, int expectedRevisionNumber,
        int expectedReviewStateVersion, string idempotencyKey, string payloadHash,
        ImmutableHashSet<string> allowedOfficialIds, ImmutableHashSet<string> allowedIssueIds,
        CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<DocumentChangeReviewPage> ListAsync(string? cursor, int limit, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<EligibleDocumentComparisonPage> ListEligibleComparisonsAsync(string? cursor, int limit,
        CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class FakeExtractionStore : IDocumentExtractionStore
{
    public Dictionary<string, DocumentExtraction> Items { get; } = [];

    public Task<DocumentExtraction?> GetAsync(string extractionId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.GetValueOrDefault(extractionId));

    public Task<DocumentExtraction> SaveAsync(DocumentExtraction extraction, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
