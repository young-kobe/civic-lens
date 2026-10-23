namespace CivicLens.Publication.Contracts;

public static class PublishedVoteKindExtensions
{
    public static bool IsSubstantive(this PublishedVoteKind kind) => kind is PublishedVoteKind.Passage or PublishedVoteKind.Suspension
        or PublishedVoteKind.Amendment or PublishedVoteKind.ConferenceReport or PublishedVoteKind.VetoOverride or PublishedVoteKind.Nomination;
}
