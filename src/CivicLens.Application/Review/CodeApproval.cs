using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Review;

internal static class CodeApproval
{
    public static bool CanApprove(PublishedRecordKind kind) =>
        kind is PublishedRecordKind.Action or PublishedRecordKind.Vote or PublishedRecordKind.Statement or PublishedRecordKind.ExecutiveAction;
}
