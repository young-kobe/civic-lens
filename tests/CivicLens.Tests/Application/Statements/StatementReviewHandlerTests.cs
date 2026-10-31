using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Application.Statements;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Statements;

public sealed class StatementReviewHandlerTests
{
    private static readonly ReviewActor Reviewer = new("auth0|reviewer", ReviewRole.Reviewer);
    private static readonly StatementDraftEvidence Evidence = StatementDraftFixtures.DraftEvidence();
    private static readonly string CandidateId = Evidence.Article.CandidateId;
    private const string DirectQuote = "This cap protects families,";
    private const string ReleaseSentence = "Senator Warren announced a rent cap for tenants.";

    private readonly FakeStatementReviewStore store = new();

    private StatementReviewContextReader Reader() =>
        new(new FakeStatementDraftStore(Evidence), new StatementSelectionResolver(StatementGateFixtures.Configuration()));

    private static StatementSelectionResolver Resolver => new(StatementGateFixtures.Configuration());

    private static SourceSpan Span(string quoted) => new(Evidence.Article.Extraction.ExtractionId,
        Evidence.Article.Extraction.Text.IndexOf(quoted, StringComparison.Ordinal), quoted.Length);

    private static SaveStatementDraftRequest Request(
        ImmutableArray<SourceSpan>? quotations = null, ImmutableArray<string>? speakers = null,
        ImmutableArray<string>? issues = null, ImmutableArray<string>? policyAreas = null) =>
        new(CandidateId, 1, "Warren announces a rent cap", "The senator announced a rent cap.", null, null,
            speakers ?? [StatementDraftFixtures.Warren], ["Jo Rivera"], issues ?? [StatementGateFixtures.RentIssue],
            policyAreas ?? [StatementGateFixtures.HousingArea], quotations ?? [Span(DirectQuote), Span(ReleaseSentence)], "save-key");

    [Fact]
    public async Task CreateStartsRevisionOneForThePersonWithEmptyText()
    {
        var revision = await new CreateStatementDraft(store).ExecuteAsync(Reviewer, new(CandidateId, "create-key"));

        Assert.Equal(1, revision.RevisionNumber);
        Assert.Equal(Reviewer.Subject, revision.AuthorSubject);
        Assert.Equal("", revision.Headline);
        Assert.Empty(revision.Quotations);
        Assert.Same(revision, store.Created);
    }

    [Fact]
    public async Task SaveSetsQuotationKindsAndBillNumbersFromTheArticleNotFromThePerson()
    {
        var revision = await new SaveStatementDraft(store, Reader()).ExecuteAsync(Reviewer, Request());

        Assert.Equal([StatementQuotationKind.DirectQuotation, StatementQuotationKind.ReleaseText], revision.Quotations.Select(item => item.Kind));
        Assert.Equal(["S. 1234", "H.R. 56"], revision.BillNumbers.AsEnumerable());
        Assert.Equal(2, revision.RevisionNumber);
        Assert.Equal(Reviewer.Subject, revision.AuthorSubject);
    }

    [Fact]
    public async Task SaveForwardsTheDateCheckedFlagSoTheStoreCanDropAnUnconfirmedAiDate()
    {
        _ = await new SaveStatementDraft(store, Reader()).ExecuteAsync(Reviewer, Request() with { StatementDateChecked = true });

        Assert.True(store.SavedDateChecked);
    }

    [Fact]
    public async Task SaveRefusesASpeakerWhoDidNotCoverTheSourceOnTheCaptureDate()
    {
        var save = new SaveStatementDraft(store, Reader());

        await Assert.ThrowsAsync<ArgumentException>(() => save.ExecuteAsync(Reviewer, Request(speakers: [StatementDraftFixtures.Markey])));
        Assert.Null(store.Saved);
    }

    [Fact]
    public async Task SaveRefusesIssuesAndPolicyAreasOutsideTheIssueConfiguration()
    {
        var save = new SaveStatementDraft(store, Reader());

        await Assert.ThrowsAsync<ArgumentException>(() => save.ExecuteAsync(Reviewer, Request(issues: ["unknown-issue"])));
        await Assert.ThrowsAsync<ArgumentException>(() => save.ExecuteAsync(Reviewer, Request(policyAreas: ["unknown-area"])));
        Assert.Null(store.Saved);
    }

    [Fact]
    public async Task SaveRefusesSpansOutsideTheTextOrOnAnotherExtraction()
    {
        var save = new SaveStatementDraft(store, Reader());
        var text = Evidence.Article.Extraction.Text;

        await Assert.ThrowsAsync<ArgumentException>(() => save.ExecuteAsync(Reviewer,
            Request(quotations: [new(Evidence.Article.Extraction.ExtractionId, text.Length - 2, 10)])));
        await Assert.ThrowsAsync<ArgumentException>(() => save.ExecuteAsync(Reviewer,
            Request(quotations: [Span(DirectQuote) with { ExtractionId = new string('b', 64) }])));
        Assert.Null(store.Saved);
    }

    [Fact]
    public async Task SaveRefusesAnOtherSpeakerNameWithSurroundingSpace()
    {
        var save = new SaveStatementDraft(store, Reader());

        await Assert.ThrowsAsync<ArgumentException>(() => save.ExecuteAsync(Reviewer, Request() with { OtherSpeakers = [" Jo Rivera"] }));
    }

    [Fact]
    public async Task ApprovalChecksSelectionsAgainstTheRosterButOtherDecisionsNeedNoEvidenceRead()
    {
        var decide = new DecideStatementReview(store, Reader());

        _ = await decide.ExecuteAsync(Reviewer, new(CandidateId, 2, 0, ReviewDecisionKind.Approve, null, [], "approve-key"));
        var allowed = store.ApprovalSelections;
        Assert.NotNull(allowed);
        Assert.Contains(StatementDraftFixtures.Warren, allowed.OfficialIds);
        Assert.DoesNotContain(StatementDraftFixtures.Markey, allowed.OfficialIds);
        Assert.Contains(StatementGateFixtures.RentIssue, allowed.IssueIds);
        Assert.Contains(StatementGateFixtures.HousingArea, allowed.PolicyAreaIds);

        _ = await decide.ExecuteAsync(Reviewer, new(CandidateId, 2, 0, ReviewDecisionKind.RequestChanges, "Add context.", [], "changes-key"));
        Assert.Null(store.ApprovalSelections);
        Assert.Equal(1, store.Decided!.ReviewStateVersion);
    }

    [Fact]
    public async Task TheAnalysisAuthorAndOutsidersCannotWriteOrRead()
    {
        var analysis = new ReviewActor(ReviewAuthor.AnalysisSubject, ReviewRole.Owner);
        var outsider = new ReviewActor("auth0|nobody", (ReviewRole)99);

        foreach (var actor in new[] { analysis, outsider })
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new CreateStatementDraft(store).ExecuteAsync(actor, new(CandidateId, "k")));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new SaveStatementDraft(store, Reader()).ExecuteAsync(actor, Request()));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new DecideStatementReview(store, Reader()).ExecuteAsync(actor,
                new(CandidateId, 1, 0, ReviewDecisionKind.Approve, null, [], "k")));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new GetStatementReview(store, Resolver).ExecuteAsync(actor, CandidateId));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new ListStatementReviews(store).ExecuteAsync(actor, null));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new ListStatementsNotInReview(store).ExecuteAsync(actor, null));
        }
        Assert.False(store.DecideCalled);
        Assert.Null(store.Created);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("o.1.a b")]
    [InlineData("")]
    public async Task ListsRejectATamperedCursorBeforeAnyRead(string cursor)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new ListStatementReviews(null!).ExecuteAsync(Reviewer, cursor, 10));
        await Assert.ThrowsAsync<ArgumentException>(() => new ListStatementsNotInReview(null!).ExecuteAsync(Reviewer, cursor, 10));
    }

    [Fact]
    public async Task ListRejectsAnUnknownFilterAndABadLimit()
    {
        var list = new ListStatementReviews(null!);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => list.ExecuteAsync(Reviewer, null, 10, new DraftListFilter((DraftStatusFilter)99)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => list.ExecuteAsync(Reviewer, null, 0));
    }

    [Fact]
    public async Task ListRejectsAReversedDateRangeAndABlankOrOverlongFilterId()
    {
        var list = new ListStatementReviews(null!);
        var all = DraftStatusFilter.All;

        var reversed = await Assert.ThrowsAsync<DraftListDateRangeException>(() => list.ExecuteAsync(Reviewer, null, 10, new(all, From: new DateOnly(2026, 5, 2), To: new DateOnly(2026, 5, 1))));
        Assert.Equal("The start date must not be after the end date.", reversed.Message);
        await Assert.ThrowsAsync<ArgumentException>(() => list.ExecuteAsync(Reviewer, null, 10, new(all, IssueId: " ")));
        await Assert.ThrowsAsync<ArgumentException>(() => list.ExecuteAsync(Reviewer, null, 10, new(all, OfficialId: new string('a', 129))));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData("ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    public async Task CandidateIdsMustBeSha256Hex(string candidateId)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new GetStatementReview(store, Resolver).ExecuteAsync(Reviewer, candidateId));
        await Assert.ThrowsAsync<ArgumentException>(() => new CreateStatementDraft(store).ExecuteAsync(Reviewer, new(candidateId, "k")));
    }

    [Fact]
    public async Task GetReturnsTheSelectionsOfTheRevisionThatAdmittedTheArticleSoThePageNeedsNoSecondRead()
    {
        store.Read = new StatementReviewRead(new(CandidateId, null, 0, ReviewStatus.NotDrafted, [], [], []), "source", "https://example.test/a",
            Evidence.Article.Extraction, null, StatementDraftFixtures.SpeakerRevision());

        var detail = (await new GetStatementReview(store, Resolver).ExecuteAsync(Reviewer, CandidateId))!;

        Assert.Equal([StatementDraftFixtures.Warren], detail.Allowed.OfficialIds);
        Assert.Equal([StatementDraftFixtures.Warren], detail.Officials.Select(official => official.Id));
        Assert.Contains(StatementGateFixtures.RentIssue, detail.Allowed.IssueIds);
        Assert.Contains(StatementGateFixtures.HousingArea, detail.Allowed.PolicyAreaIds);
    }

    [Fact]
    public async Task GetAllowsNoOfficialWhenTheAdmittingRevisionIsMissing()
    {
        store.Read = new StatementReviewRead(new(CandidateId, null, 0, ReviewStatus.NotDrafted, [], [], []), "source", "https://example.test/a",
            Evidence.Article.Extraction, null, null);

        var detail = (await new GetStatementReview(store, Resolver).ExecuteAsync(Reviewer, CandidateId))!;

        Assert.Empty(detail.Allowed.OfficialIds);
        Assert.Empty(detail.Officials);
    }

    [Fact]
    public async Task GetShowsWhyApprovalWillFailWhenAnIssueOfTheSavedRevisionIsNoLongerConfigured()
    {
        var revision = await new SaveStatementDraft(store, Reader()).ExecuteAsync(Reviewer, Request());
        store.Read = new StatementReviewRead(new(CandidateId, revision, 0, ReviewStatus.AwaitingReview, [revision], [], []), "source",
            "https://example.test/a", Evidence.Article.Extraction, null, StatementDraftFixtures.SpeakerRevision());
        var narrower = new StatementSelectionResolver(StatementGateFixtures.Configuration() with { Issues = [] });

        var configured = (await new GetStatementReview(store, Resolver).ExecuteAsync(Reviewer, CandidateId))!;
        var retired = (await new GetStatementReview(store, narrower).ExecuteAsync(Reviewer, CandidateId))!;

        Assert.True(configured.Approval!.Passed);
        Assert.Contains("no longer configured", retired.Approval!.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetShowsNoApprovalFailureForAnApprovedAiRevisionWithADateBecauseCodeReadTheDate()
    {
        var saved = await new SaveStatementDraft(store, Reader()).ExecuteAsync(Reviewer, Request());
        var aiRevision = saved with
        {
            AuthorSubject = ReviewAuthor.AnalysisSubject,
            StatementDate = new DateOnly(2026, 5, 5),
            StatementDateEvidence = Span(ReleaseSentence)
        };
        var awaiting = await ReadAsync(ReviewStatus.AwaitingReview);
        var approved = await ReadAsync(ReviewStatus.Approved);

        Assert.Contains("AI-proposed", awaiting.Approval!.Failure, StringComparison.Ordinal);
        Assert.Null(approved.Approval);

        async Task<StatementReviewDetail> ReadAsync(ReviewStatus status)
        {
            store.Read = new StatementReviewRead(new(CandidateId, aiRevision, 0, status, [aiRevision], [], []), "source",
                "https://example.test/a", Evidence.Article.Extraction, null, StatementDraftFixtures.SpeakerRevision());
            return (await new GetStatementReview(store, Resolver).ExecuteAsync(Reviewer, CandidateId))!;
        }
    }
}
