using System.Text.Json;
using CivicLens.Core.Review;

namespace CivicLens.Tests.Core.Review;

public sealed class ReviewDecisionJsonTests
{
    [Fact]
    public void ADecisionStoredBeforeCorrectionNotesReadsBackWithNoNoteBecauseStoredRowsAreNeverRewritten()
    {
        const string stored = """
            {"decisionId":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","draftId":"d","revisionNumber":1,"reviewStateVersion":1,
             "kind":0,"actorSubject":"auth0|editor","note":null,"resolvedDecisionIds":[],"createdAtUtc":"2026-01-01T00:00:00+00:00"}
            """;

        var decision = JsonSerializer.Deserialize<ReviewDecision>(stored, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Null(decision!.CorrectionNote);
        Assert.Equal(ReviewDecisionKind.Approve, decision.Kind);
    }
}
