using CivicLens.Application.Statements;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Statements;

public sealed class SaveGateLabelTests
{
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);
    private static readonly ReviewActor Reviewer = new("auth0|friend", ReviewRole.Reviewer);
    private static readonly string CandidateId = new('a', 64);
    private static readonly string ExtractionId = new('b', 64);
    private readonly RecordingStore store = new();
    private readonly SaveGateLabel save;

    public SaveGateLabelTests() => save = new SaveGateLabel(store, StatementGateFixtures.Configuration());

    [Fact]
    public async Task AnOwnerLabelIsStoredAsAHumanLabelWithNoRunSoItCanBecomeTheLabelOfRecord()
    {
        await save.ExecuteAsync(Owner, Request([StatementGateFixtures.RentIssue], true));

        var entry = Assert.Single(store.Entries);
        Assert.Equal((CandidateId, ExtractionId, Owner.Subject, ReviewAuthorKind.Person, null),
            (entry.CandidateId, entry.ExtractionId, entry.AuthorSubject, entry.AuthorKind, entry.RunId));
        Assert.Equal([StatementGateFixtures.RentIssue], entry.Label.IssueIds.AsEnumerable());
        Assert.True(entry.Label.NamesBillOrVote);
    }

    [Fact]
    public async Task AReviewerCannotLabelBecauseLabelsFeedTheOwnersBenchmark()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => save.ExecuteAsync(Reviewer, Request([], false)));

        Assert.Empty(store.Entries);
    }

    [Fact]
    public async Task TheAnalysisSubjectCannotLabelSoAModelNeverPassesAsAPerson()
    {
        var model = new ReviewActor(ReviewAuthor.AnalysisSubject, ReviewRole.Owner);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => save.ExecuteAsync(model, Request([], false)));
    }

    [Theory]
    [InlineData("not-a-tracked-issue")]
    [InlineData("health")]
    public async Task AnIssueThatTheIssueFileDoesNotTrackIsRefusedSoLabelsStayInTheGateVocabulary(string issueId)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => save.ExecuteAsync(Owner, Request([issueId], false)));

        Assert.Empty(store.Entries);
    }

    [Fact]
    public async Task ARepeatedIssueIsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            save.ExecuteAsync(Owner, Request([StatementGateFixtures.RentIssue, StatementGateFixtures.RentIssue], false)));
    }

    [Theory]
    [InlineData("not-an-id", "b")]
    [InlineData("a", "not-an-id")]
    public async Task AMalformedCandidateOrExtractionIdIsRefused(string candidate, string extraction)
    {
        var request = new SaveGateLabelRequest(candidate == "a" ? CandidateId : candidate, extraction == "b" ? ExtractionId : extraction, [], false);

        await Assert.ThrowsAsync<ArgumentException>(() => save.ExecuteAsync(Owner, request));
    }

    private static SaveGateLabelRequest Request(string[] issues, bool namesBillOrVote) =>
        new(CandidateId, ExtractionId, [.. issues], namesBillOrVote);

    private sealed class RecordingStore : IGateLabelStore
    {
        public List<GateLabelEntry> Entries { get; } = [];

        public Task AddAsync(GateLabelEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<GateLabelQueue> ListUnlabeledAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GateLabelTask?> GetAsync(string candidateId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<GateLabelExportRow>> ListLabelsOfRecordAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
