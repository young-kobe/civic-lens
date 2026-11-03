using System.Collections.Immutable;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Review;

/// <summary>The reason codes that a rejection of each record kind may use. The decision tables and the editor list both read it.</summary>
public static class ReviewReasons
{
    public static ImmutableArray<ReviewReasonCode> For(PublishedRecordKind kind) => kind switch
    {
        PublishedRecordKind.Statement => [ReviewReasonCode.NotAStatement, ReviewReasonCode.WrongSpeaker, ReviewReasonCode.IndexPage,
            ReviewReasonCode.LowSubstance, ReviewReasonCode.InaccurateSummary, ReviewReasonCode.Duplicate],
        PublishedRecordKind.Relationship => [ReviewReasonCode.NotRelated, ReviewReasonCode.Duplicate],
        PublishedRecordKind.DocumentChange or PublishedRecordKind.Brief => [ReviewReasonCode.InaccurateSummary, ReviewReasonCode.Duplicate,
            ReviewReasonCode.LowSubstance],
        PublishedRecordKind.Action or PublishedRecordKind.Vote or PublishedRecordKind.ExecutiveAction => [ReviewReasonCode.InaccurateSummary,
            ReviewReasonCode.Duplicate],
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static void Require(PublishedRecordKind kind, ReviewReasonCode? reasonCode)
    {
        if (reasonCode is { } code && !For(kind).Contains(code))
            throw new ArgumentException("This reason does not apply to this kind of record.", nameof(reasonCode));
    }
}
