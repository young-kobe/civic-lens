using System.Collections.Immutable;
using System.Text.RegularExpressions;
using CivicLens.Application.Votes;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;

namespace CivicLens.Tests.Application.Votes;

public sealed class VoteCodeTextTests
{
    private const string Title = "Retirement Fairness Act of 2025.";

    [Theory]
    [InlineData(VoteKind.Passage, "Passed", "The House voted on passage of H.R. 1013, Retirement Fairness Act of 2025. The bill passed.")]
    [InlineData(VoteKind.Suspension, "Failed", "The House voted on passing H.R. 1013, Retirement Fairness Act of 2025 with limited debate. The motion failed.")]
    [InlineData(VoteKind.Amendment, "Failed", "The House voted on an amendment to H.R. 1013, Retirement Fairness Act of 2025. The amendment failed.")]
    [InlineData(VoteKind.MotionToRecommit, "Failed", "The House voted on sending H.R. 1013, Retirement Fairness Act of 2025 back to committee. The motion failed.")]
    [InlineData(VoteKind.Rule, "Passed", "The House voted on the rules for debate set by H.R. 1013, Retirement Fairness Act of 2025. The rule passed.")]
    [InlineData(VoteKind.PreviousQuestion, "Passed", "The House voted on moving H.R. 1013, Retirement Fairness Act of 2025 toward a decision. The motion passed.")]
    [InlineData(VoteKind.VetoOverride, "Failed", "The House voted on overriding the veto of H.R. 1013, Retirement Fairness Act of 2025. The override failed.")]
    [InlineData(VoteKind.ConferenceReport, "Agreed to", "The House voted on the final text of H.R. 1013, Retirement Fairness Act of 2025 from both chambers. The report passed.")]
    [InlineData(VoteKind.Cloture, "Cloture Motion Agreed to", "The House voted on ending debate on H.R. 1013, Retirement Fairness Act of 2025. The motion passed.")]
    [InlineData(VoteKind.MotionToProceed, "Motion to Proceed Rejected", "The House voted on starting debate on H.R. 1013, Retirement Fairness Act of 2025. The motion failed.")]
    [InlineData(VoteKind.Other, "Passed", "The House voted on H.R. 1013, Retirement Fairness Act of 2025. The question passed.")]
    public void EachKindOfABillVoteHasOneHouseStyleHeadlineInThePastTense(VoteKind kind, string result, string expected)
    {
        var text = VoteCodeText.Of(Snapshot(kind, Chamber.House, result, title: Title), "hr", 1013);

        Assert.Equal(expected, text.Headline);
        Assert.Equal(expected, text.Summary);
        Assert.Equal((ReviewTextOrigin.Code, null), (text.Origin, text.Suggestion));
    }

    [Theory]
    [InlineData(VoteKind.Nomination, "Nomination Confirmed", "The Senate voted on a nomination: Jane Doe, of Ohio, to be Secretary of Labor. The nominee was confirmed.")]
    [InlineData(VoteKind.Cloture, "Cloture Motion Rejected", "The Senate voted on ending debate: Jane Doe, of Ohio, to be Secretary of Labor. The motion failed.")]
    [InlineData(VoteKind.Amendment, "Amendment Rejected", "The Senate voted on an amendment: Jane Doe, of Ohio, to be Secretary of Labor. The amendment failed.")]
    public void AVoteWithNoBillNamesItsSourceDescriptionWordForWord(VoteKind kind, string result, string expected)
    {
        var text = VoteCodeText.Of(Snapshot(kind, Chamber.Senate, result, description: "Jane Doe, of Ohio, to be Secretary of Labor"), null, null);

        Assert.Equal(expected, text.Headline);
        Assert.Equal(expected, text.Summary);
    }

    [Fact]
    public void ABillVoteKeepsTheSourceDescriptionOutOfTheHeadlineAndAddsItWordForWordToTheSummary()
    {
        const string description = "To strike the funding cap.  ";

        var text = VoteCodeText.Of(Snapshot(VoteKind.Amendment, Chamber.Senate, "Amendment Rejected", title: Title, description: description), "hr", 1013);

        Assert.Equal("The Senate voted on an amendment to H.R. 1013, Retirement Fairness Act of 2025. The amendment failed.", text.Headline);
        Assert.Equal(text.Headline + " To strike the funding cap.", text.Summary);
    }

    [Fact]
    public void AVoteWithNoBillAndNoDescriptionStillReadsAsASentence()
    {
        var nomination = VoteCodeText.Of(Snapshot(VoteKind.Nomination, Chamber.Senate, "Nomination Rejected"), null, null);
        var other = VoteCodeText.Of(Snapshot(VoteKind.Other, Chamber.Senate, "Agreed to", question: "On the Resolution of Ratification"), null, null);

        Assert.Equal("The Senate voted on a nomination. The nominee was not confirmed.", nomination.Headline);
        Assert.Equal("The Senate voted on a question: On the Resolution of Ratification. The question passed.", other.Headline);
    }

    [Fact]
    public void TheHeadlineHoldsNoCountEvenWhenTheFactsHoldTheTotals()
    {
        var text = VoteCodeText.Of(Snapshot(VoteKind.Passage, Chamber.House, "Passed", title: Title,
            extra: ["Party total D: Yea 1, Nay 212, Present 0, Not voting 2", "Count: Yea 217, Nay 214, Present 0, Not voting 3"]), "hr", 1013);

        var withoutBill = text.Headline.Replace("1013", "", StringComparison.Ordinal).Replace("2025", "", StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\d", withoutBill);
        Assert.DoesNotMatch("Yea|Nay|vote count", text.Summary);
    }

    [Fact]
    public void ATitleThatMakesTheHeadlineTooLongIsLeftOutWholeAndNeverCutInsideASentence()
    {
        var title = string.Join(' ', Enumerable.Repeat("To amend the Internal Revenue Code of 1986 to provide relief.", 5));

        var text = VoteCodeText.Of(Snapshot(VoteKind.Passage, Chamber.House, "Passed", title: title), "hr", 1013);

        Assert.Equal("The House voted on passage of H.R. 1013. The bill passed.", text.Headline);
        Assert.True(text.Headline.Length <= VoteDraftRevision.MaximumHeadlineLength);
    }

    [Fact]
    public void AResultThatNoPlainWordKnowsIsQuotedAsTheSourceStatesIt()
    {
        var text = VoteCodeText.Of(Snapshot(VoteKind.Passage, Chamber.House, "Decision of Chair Sustained", title: Title), "hres", 5);

        Assert.Equal("The House voted on passage of H.Res. 5, Retirement Fairness Act of 2025. The result was Decision of Chair Sustained.", text.Headline);
    }

    [Fact]
    public void APassageVoteOnAResolutionCallsItAResolution()
    {
        var text = VoteCodeText.Of(Snapshot(VoteKind.Passage, Chamber.House, "Passed", title: Title), "hconres", 14);

        Assert.EndsWith("The resolution passed.", text.Headline, StringComparison.Ordinal);
    }

    private static VoteSnapshot Snapshot(VoteKind kind, Chamber chamber, string result, string? title = null, string? description = null,
        string question = "On Passage", string[]? extra = null)
    {
        var lines = new List<string> { "Started: 2025-04-10T11:08:00-04:00", $"Question: {question}" };
        if (description is not null) lines.Add($"Vote title: {description}");
        lines.Add($"Result: {result}");
        lines.AddRange(extra ?? []);
        if (title is not null) lines.Add($"Title: {title}");
        var span = new SourceSpan(new string('a', 64), 0, 1);
        ImmutableArray<VoteFact> facts = [.. lines.Select(line => new VoteFact(line, span))];
        return new(chamber, chamber == Chamber.Senate ? VoteSource.Voteview : VoteSource.CongressGov, facts, span, kind, span, [], new string('b', 64), null, [],
            new DateOnly(2025, 4, 10), ImmutableDictionary<string, string>.Empty);
    }
}
