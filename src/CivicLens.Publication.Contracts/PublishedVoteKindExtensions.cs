namespace CivicLens.Publication.Contracts;

public static class PublishedVoteKindExtensions
{
    public static bool IsSubstantive(this PublishedVoteKind kind) => kind is PublishedVoteKind.Passage or PublishedVoteKind.Suspension
        or PublishedVoteKind.Amendment or PublishedVoteKind.ConferenceReport or PublishedVoteKind.VetoOverride or PublishedVoteKind.Nomination;

    /// <summary>The names of the substantive kinds, as the vote views hold them in the column <c>kind</c>. This is the one list that a store sends to SQL.</summary>
    public static string[] SubstantiveNames() => [.. Enum.GetValues<PublishedVoteKind>().Where(IsSubstantive).Select(kind => kind.ToString())];
}
