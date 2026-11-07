using System.Reflection;
using CivicLens.Core.Actions;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Infrastructure.Actions;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Infrastructure.Actions;

public sealed class ActionCrsSummaryTests
{
    private const string Version = "00@2025-01-05T10:00:00Z";
    private const string Block = "Summary version: 00\nAction date: 2025-01-03\nAction: Introduced\nUpdated: 2025-01-05T10:00:00Z\nText:\n  Exact body\nText: stays body.  ";
    private const string Prefix = "Bill: 119 hr 1\n\n";

    [Fact]
    public void ExactRevisionSpanSelectsItsBodyEvenWhenAnotherSummaryFollows()
    {
        var extraction = Extraction("original", Prefix + Block + "\n\n" + Block.Replace("Exact body", "Later body", StringComparison.Ordinal));
        var revision = WithSummary(extraction, Prefix.Length, Block.Length);

        var row = ReadRow(revision, [extraction]);

        Assert.Equal("  Exact body\nText: stays body.  ", (string?)row.GetType().GetProperty("CrsSummaryText")!.GetValue(row));
        Assert.True((bool)row.GetType().GetProperty("CrsSummaryRead")!.GetValue(row)!);
    }

    [Fact]
    public void CompletedReadMarksAttemptWhenSummaryBodyIsLegitimatelyNull()
    {
        var row = ReadRow(ActionRevisionFixtures.Model(), []);

        Assert.Null((string?)row.GetType().GetProperty("CrsSummaryText")!.GetValue(row));
        Assert.True((bool)row.GetType().GetProperty("CrsSummaryRead")!.GetValue(row)!);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(int.MaxValue, 1)]
    [InlineData(0, int.MaxValue)]
    public void InvalidSpanBoundsStayUnavailable(int start, int length)
    {
        var extraction = Extraction("original", Prefix + Block);

        Assert.Null(ReadBody(WithSummary(extraction, start, length), [extraction]));
    }

    [Fact]
    public void PartialBlocksAndWrongVersionsStayUnavailable()
    {
        var extraction = Extraction("original", Prefix + Block);
        var revision = WithSummary(extraction, Prefix.Length, Block.Length);

        Assert.Null(ReadBody(revision with { SummaryCitation = revision.SummaryCitation! with { Length = Block.Length - 1 } }, [extraction]));
        Assert.Null(ReadBody(revision with { SummaryVersion = "00@2025-01-06T10:00:00Z" }, [extraction]));
        Assert.Null(ReadBody(revision with { SummaryCitation = new(extraction.ExtractionId, Prefix.Length + 1, Block.Length - 1) }, [extraction]));
    }

    [Fact]
    public void MissingPinnedExtractionNeverUsesANewerExtractionOrInventsABodyWithoutACitation()
    {
        var original = Extraction("original", Prefix + Block);
        var newer = Extraction("newer", Prefix + Block.Replace("Exact body", "Later body", StringComparison.Ordinal));
        var revision = WithSummary(original, Prefix.Length, Block.Length);

        Assert.Null(ReadBody(revision, [newer]));
        Assert.Null(ReadBody(revision with { SummaryCitation = null, SummaryVersion = null }, [original]));
    }

    private static ActionDraftRevision WithSummary(DocumentExtraction extraction, int start, int length) =>
        ActionRevisionFixtures.Model() with { SummaryVersion = Version, SummaryCitation = new(extraction.ExtractionId, start, length) };

    private static DocumentExtraction Extraction(string attemptId, string text) => new(new CapturedAttemptResult(attemptId, "congress-bills",
        "https://example.test/", "https://example.test/", DateTimeOffset.UnixEpoch,
        new CollectionResponse(200, null, null, "application/json", []), new CaptureIdentity(new string('a', 64), 0)), "summaries", text);

    private static string? ReadBody(ActionDraftRevision revision, DocumentExtraction[] extractions)
    {
        var row = ReadRow(revision, extractions);
        return (string?)row.GetType().GetProperty("CrsSummaryText")!.GetValue(row);
    }

    private static object ReadRow(ActionDraftRevision revision, DocumentExtraction[] extractions)
    {
        var owner = typeof(PostgresActionReviewStore).Assembly.GetType("CivicLens.Infrastructure.Actions.ActionRevisionRows", throwOnError: true)!;
        var row = owner.GetMethod("Create")!.Invoke(null, [revision, "run", TestCodeVersion.Current])!;
        owner.GetMethod("FillCrsSummary", BindingFlags.Public | BindingFlags.Static)!.Invoke(null,
            [row, revision, extractions.ToDictionary(item => item.ExtractionId, StringComparer.Ordinal)]);
        return row;
    }
}
