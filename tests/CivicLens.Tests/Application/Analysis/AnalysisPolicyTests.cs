using CivicLens.Application.Analysis;

namespace CivicLens.Tests.Application.Analysis;

public sealed class AnalysisPolicyTests
{
    [Fact]
    public void RetriesDoubleFromThirtySecondsAndNeverWaitMoreThanThirtyMinutes()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), AnalysisPolicy.RetryDelay(0));
        Assert.Equal(TimeSpan.FromSeconds(60), AnalysisPolicy.RetryDelay(1));
        Assert.Equal(TimeSpan.FromMinutes(30), AnalysisPolicy.RetryDelay(7));
        Assert.Equal(TimeSpan.FromMinutes(30), AnalysisPolicy.RetryDelay(40));
    }

    [Fact]
    public void ARunCannotReserveMoreThanTheDailyLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentChangeAnalysisSettings(dailyTokenLimit: 1_000, runTokenLimit: 2_000));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentChangeAnalysisSettings(dailyTokenLimit: 1_000_000, concurrency: 5));
    }

    [Fact]
    public void ADailyLimitBelowTheDefaultRunLimitCapsTheRunLimitInsteadOfFailing()
    {
        Assert.Equal(50_000, new DocumentChangeAnalysisSettings(dailyTokenLimit: 50_000).RunTokenLimit);
        Assert.Equal(DocumentChangeAnalysisSettings.DefaultRunTokenLimit, new DocumentChangeAnalysisSettings(1_000_000).RunTokenLimit);
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
        foreach (var failure in Enum.GetValues<AnalysisModelFailure>())
            Assert.Equal(failure.ToString(), AnalysisErrorCodes.ToOutcome(
                AnalysisErrorCodes.ForOutcome(Enum.Parse<CivicLens.Core.Analysis.AnalysisRunOutcome>(failure.ToString())))
                .ToString());
    }
}
