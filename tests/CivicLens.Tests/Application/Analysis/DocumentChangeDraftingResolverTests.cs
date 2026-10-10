using System.Collections.Immutable;
using CivicLens.Application.Analysis;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Analysis;

public sealed class DocumentChangeDraftingResolverTests
{
    private static readonly DocumentChangeAnalysisCatalog Catalog = new([new("warren", "Elizabeth Warren")], ["housing"]);
    private static readonly string DraftId = new('d', 32);

    [Fact]
    public void AUniqueQuoteBecomesExactOffsetsIntoItsExtraction()
    {
        var evidence = FakeDocumentChangeDraftingModel.Evidence("Intro.\nDeadline: October 15.\nEnd.\n", "Intro.\nDeadline: October 30.\nEnd.\n");
        var resolution = Resolve(FakeDocumentChangeDraftingModel.Output(("h1", "after", "October 30"), ("h1", "before", "October 15")), evidence);

        var revision = Assert.IsType<DocumentChangeDraftRevision>(resolution.Revision);
        Assert.Equal(ReviewAuthor.AnalysisSubject, revision.AuthorSubject);
        Assert.Equal(1, revision.RevisionNumber);
        Assert.Collection(revision.Citations,
            citation => Assert.Equal("October 30", Quote(evidence.After, citation)),
            citation => Assert.Equal("October 15", Quote(evidence.Before, citation)));
        Assert.Equal(evidence.After.ExtractionId, revision.Citations[0].ExtractionId);
    }

    [Theory]
    [InlineData("h1", "after", "October 31", "does not occur")]
    [InlineData("h1", "after", "e", "occurs more than once")]
    [InlineData("h2", "after", "October 30", "unknown hunk")]
    [InlineData("h1", "middle", "October 30", "Use \"before\" or \"after\"")]
    [InlineData("h1", "before", "October 30", "does not occur")]
    public void AMissingAmbiguousOrMisplacedQuoteRejectsTheWholeDraft(string hunkId, string side, string quote, string reason)
    {
        var evidence = FakeDocumentChangeDraftingModel.Evidence("Deadline: October 15.\n", "Deadline: October 30.\n");
        var resolution = Resolve(FakeDocumentChangeDraftingModel.Output(("h1", "after", "October 30"), (hunkId, side, quote)), evidence);

        Assert.Null(resolution.Revision);
        Assert.True(resolution.HasCitationErrors);
        Assert.Contains(resolution.Errors, error => error.Contains(reason, StringComparison.Ordinal));
    }

    [Fact]
    public void AQuoteWithHalfOfASurrogatePairNeverBecomesACitation()
    {
        var evidence = FakeDocumentChangeDraftingModel.Evidence("Mood: calm.\n", "Mood: \U0001F600 happy.\n");
        var output = FakeDocumentChangeDraftingModel.Output(("h1", "after", "Mood: HALF")).Replace("HALF", "\\ud83d", StringComparison.Ordinal);
        var resolution = Resolve(output, evidence);

        Assert.Null(resolution.Revision);
        Assert.NotEmpty(resolution.Errors);
    }

    [Fact]
    public void SelectionsMustComeFromTheCatalogAndAChangeDateMustNameACitation()
    {
        var evidence = FakeDocumentChangeDraftingModel.Evidence("Deadline: October 15.\n", "Updated 2026-10-03. Deadline: October 30.\n");
        var unknown = Resolve(FakeDocumentChangeDraftingModel.Output("Headline", [("h1", "after", "October 30")], officialIds: ["sanders"]), evidence);
        Assert.Null(unknown.Revision);
        Assert.False(unknown.HasCitationErrors);

        var badDate = Resolve(FakeDocumentChangeDraftingModel.Output("Headline", [("h1", "after", "Updated 2026-10-03")],
            changeDate: new { date = "2026-10-03", citationIndex = 4 }), evidence);
        Assert.Null(badDate.Revision);

        var dated = Resolve(FakeDocumentChangeDraftingModel.Output("Headline", [("h1", "after", "October 30"), ("h1", "after", "Updated 2026-10-03")],
            officialIds: ["warren"], issueIds: ["housing"], changeDate: new { date = "2026-10-03", citationIndex = 1 }), evidence);
        var revision = Assert.IsType<DocumentChangeDraftRevision>(dated.Revision);
        Assert.Equal(new DateOnly(2026, 10, 3), revision.ChangeDate);
        Assert.Equal(revision.Citations[1], revision.ChangeDateEvidence);
        Assert.Equal(["warren"], revision.OfficialIds.ToArray());
    }

    [Theory]
    [InlineData("Updated 2026-10-03.")]
    [InlineData("Updated October 3, 2026.")]
    [InlineData("Updated Oct. 3, 2026.")]
    [InlineData("Updated 3 October 2026.")]
    [InlineData("Updated 10/3/2026.")]
    public void AChangeDateIsKeptOnlyWhenItsCitedPassageStatesIt(string line)
    {
        var evidence = FakeDocumentChangeDraftingModel.Evidence("Deadline: October 15.\n", line + " Deadline: October 30.\n");
        var stated = Resolve(FakeDocumentChangeDraftingModel.Output("Headline", [("h1", "after", line)],
            changeDate: new { date = "2026-10-03", citationIndex = 0 }), evidence);
        Assert.Equal(new DateOnly(2026, 10, 3), Assert.IsType<DocumentChangeDraftRevision>(stated.Revision).ChangeDate);

        var other = Resolve(FakeDocumentChangeDraftingModel.Output("Headline", [("h1", "after", line)],
            changeDate: new { date = "2026-10-04", citationIndex = 0 }), evidence);
        Assert.Null(other.Revision);
        Assert.True(other.HasCitationErrors);
        Assert.Contains(other.Errors, error => error.Contains("does not state 2026-10-04", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"headline\":\"Only a headline\"}")]
    public void OutputThatDoesNotMatchTheSchemaIsRejected(string output)
    {
        var evidence = FakeDocumentChangeDraftingModel.Evidence("A.\n", "B.\n");
        var resolution = Resolve(output, evidence);
        Assert.Null(resolution.Revision);
        Assert.NotEmpty(resolution.Errors);
    }

    private static DocumentChangeDraftingResolution Resolve(string output, DocumentChangeAnalysisEvidence evidence) =>
        DocumentChangeDraftingResolver.Resolve(output, evidence, Catalog, DraftId, DateTimeOffset.UnixEpoch);

    private static string Quote(DocumentExtraction extraction, DocumentChangeCitation citation) =>
        new DocumentTextSpan(extraction, citation.Start, citation.Length).Quote;
}
