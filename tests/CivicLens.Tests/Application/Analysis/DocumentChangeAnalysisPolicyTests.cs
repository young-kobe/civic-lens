using CivicLens.Application.Analysis;

namespace CivicLens.Tests.Application.Analysis;

public sealed class DocumentChangeAnalysisPolicyTests
{
    [Fact]
    public void RetriesDoubleFromThirtySecondsAndNeverWaitMoreThanThirtyMinutes()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), DocumentChangeAnalysisPolicy.RetryDelay(0));
        Assert.Equal(TimeSpan.FromSeconds(60), DocumentChangeAnalysisPolicy.RetryDelay(1));
        Assert.Equal(TimeSpan.FromMinutes(30), DocumentChangeAnalysisPolicy.RetryDelay(7));
        Assert.Equal(TimeSpan.FromMinutes(30), DocumentChangeAnalysisPolicy.RetryDelay(40));
    }

    [Fact]
    public void ARunCannotReserveMoreThanTheDailyLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentChangeAnalysisSettings(dailyTokenLimit: 1_000, runTokenLimit: 2_000));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentChangeAnalysisSettings(dailyTokenLimit: 1_000_000, concurrency: 5));
    }

    [Fact]
    public void ALimitBelowTheSmallestReservationFailsAtStartup()
    {
        var minimum = DocumentChangeDraftingPrompt.MinimumReservation;
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentChangeAnalysisSettings(minimum - 1, minimum - 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentChangeAnalysisSettings(minimum * 10, minimum - 1));
        Assert.Equal(minimum, new DocumentChangeAnalysisSettings(minimum, minimum).RunTokenLimit);
    }

    [Fact]
    public void EachModelFailureHasARunOutcomeAndAnErrorCode()
    {
        foreach (var failure in Enum.GetValues<DocumentChangeDraftingFailure>())
            Assert.Equal(failure.ToString(), DocumentChangeAnalysisErrorCodes.ToOutcome(
                DocumentChangeAnalysisErrorCodes.ForOutcome(Enum.Parse<CivicLens.Core.Analysis.AnalysisRunOutcome>(failure.ToString())))
                .ToString());
    }
}
