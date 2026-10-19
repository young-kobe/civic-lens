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
    public void ConcurrencyAboveTheMaximumFailsAtStartup()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentChangeDraftSettings(dailyTokenLimit: 1_000_000, concurrency: 5));
    }

    [Fact]
    public void ADailyLimitBelowTheDefaultCallLimitCapsTheCallLimitInsteadOfFailing()
    {
        Assert.Equal(50_000, new DocumentChangeDraftSettings(dailyTokenLimit: 50_000).CallTokenLimit);
        Assert.Equal(DocumentChangeDraftSettings.DefaultCallTokenLimit, new DocumentChangeDraftSettings(1_000_000).CallTokenLimit);
    }

    [Fact]
    public void ADailyLimitBelowTheSmallestReservationFailsAtStartup()
    {
        var minimum = DocumentChangeDraftPrompt.MinimumReservation;
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentChangeDraftSettings(minimum - 1));
        Assert.Equal(minimum, new DocumentChangeDraftSettings(minimum).CallTokenLimit);
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
