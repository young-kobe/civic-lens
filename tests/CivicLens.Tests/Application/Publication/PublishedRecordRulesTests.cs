using System.Collections.Immutable;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishedRecordRulesTests
{
    [Fact]
    public void ApproverAndTimeComeFromTheSameDecisionWhenCodeApprovedThenAPersonApprovedAfterAReopen()
    {
        var id = new string('b', 64);
        var at = PublicationScenario.ApprovedAt;
        var code = PublicationScenario.Decision(id, 1, ReviewDecisionKind.Approve) with { ActorSubject = ReviewAuthor.CodeSubject, CreatedAtUtc = at };
        var reopened = PublicationScenario.Decision(id, 2, ReviewDecisionKind.WithdrawApproval) with { CreatedAtUtc = at.AddHours(1) };
        var person = PublicationScenario.Decision(id, 3, ReviewDecisionKind.Approve) with { CreatedAtUtc = at.AddHours(2) };

        var decisions = new[] { person, code, reopened }.ToImmutableArray();

        Assert.Equal(PublishedApprover.Person, PublishedRecordRules.ApproverOf(1, decisions));
        Assert.Equal(at.AddHours(2), PublishedRecordRules.GetApprovalTime(1, decisions));
    }
}
