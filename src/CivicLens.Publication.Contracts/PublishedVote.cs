using System.Globalization;

namespace CivicLens.Publication.Contracts;

public sealed record PublishedVote
{
    private const string StartedPrefix = "Started: ";

    public const int MaximumFacts = 64;
    public const int MaximumMembers = 600;
    public const int MaximumSources = 3;

    public required string RecordId { get; init; }
    public required int RevisionNumber { get; init; }
    public required string Headline { get; init; }
    public required string Summary { get; init; }
    public required int Congress { get; init; }
    public required int Session { get; init; }
    public required int RollCallNumber { get; init; }
    public required PublishedVoteKind Kind { get; init; }
    public PublishedCitation? KindCitation { get; init; }
    public required DateOnly VotedOn { get; init; }
    public required PublishedCitation VotedOnCitation { get; init; }
    public required PublishedVoteFact[] Facts { get; init; }
    public required PublishedMemberVote[] Members { get; init; }
    public string? BillType { get; init; }
    public int? BillNumber { get; init; }
    public required string[] TrackedIssueIds { get; init; }
    public required PublishedDocumentVersion[] Sources { get; init; }
    public required DateTimeOffset ApprovedAtUtc { get; init; }
    public required DateTimeOffset FirstPublishedAtUtc { get; init; }

    public PublishedRecordEntry ToEntry() => new()
    {
        Kind = PublishedRecordKind.Vote,
        RecordId = RecordId,
        RevisionNumber = RevisionNumber,
        Headline = Headline,
        FirstPublishedAtUtc = FirstPublishedAtUtc,
        IssueIds = TrackedIssueIds,
        SourceHost = Sources[0].Host,
        EventDate = VotedOn
    };

    public IEnumerable<PublishedCitation> Citations() => new[] { VotedOnCitation }.Concat(KindCitation is { } kind ? [kind] : [])
        .Concat(Facts.Select(fact => fact.Citation)).Concat(Members.Select(member => member.Citation));

    public void Validate()
    {
        if (!PublicationProtocol.IsRecordId(PublishedRecordKind.Vote, RecordId) || RevisionNumber < 1 ||
            string.IsNullOrWhiteSpace(Headline) || string.IsNullOrWhiteSpace(Summary))
            throw new InvalidDataException("Published record identity or wording is invalid.");
        if (Congress < 1 || Session is not (1 or 2) || RollCallNumber < 1)
            throw new InvalidDataException("Published vote requires a roll call identity.");
        if ((BillType is null) != (BillNumber is null) || BillType is not null && (string.IsNullOrWhiteSpace(BillType) || BillNumber < 1))
            throw new InvalidDataException("A published vote bill type has a number, and a number has a type.");
        PublishedEvidenceRules.RequireSources(Sources, MaximumSources, "vote");
        if (!Enum.IsDefined(Kind) || Kind != PublishedVoteKind.Other && KindCitation is null)
            throw new InvalidDataException("A published vote kind other than Other requires a citation.");
        ValidateFacts();
        ValidateMembers();
        if (TrackedIssueIds is null || !PublishedEvidenceRules.AreDistinctAndNotBlank(TrackedIssueIds))
            throw new InvalidDataException("Published vote tracked issue ids must be distinct and not blank.");
        foreach (var citation in Citations()) PublishedEvidenceRules.RequireCitation(Sources, citation, "vote");
        PublishedEvidenceRules.RequireEverySourceCited(Sources, Citations(), "vote");
        if (!VotedOnCitation.Quote.StartsWith(StartedPrefix + VotedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal))
            throw new InvalidDataException("The vote day must be the date of the cited start line.");
    }

    private void ValidateFacts()
    {
        if (Facts is null || Facts.Length > MaximumFacts || Facts.Any(fact => fact?.Citation is null || fact.Text != fact.Citation.Quote))
            throw new InvalidDataException($"Published vote requires at most {MaximumFacts} facts that equal their citations.");
    }

    private void ValidateMembers()
    {
        if (Members is null || Members.Length is 0 or > MaximumMembers)
            throw new InvalidDataException($"Published vote requires 1 to {MaximumMembers} member votes.");
        if (Members.Any(member => member?.Official is null || member.Citation is null || string.IsNullOrWhiteSpace(member.Official.Id) ||
                string.IsNullOrWhiteSpace(member.Official.Name) || string.IsNullOrWhiteSpace(member.Cast) ||
                member.Citation.Quote?.EndsWith(", " + member.Cast, StringComparison.Ordinal) != true))
            throw new InvalidDataException("A published member vote requires a named official and a cast that its citation shows.");
        if (!PublishedEvidenceRules.AreDistinctAndNotBlank(Members.Select(member => member.Official.Id)))
            throw new InvalidDataException("Published vote members must be distinct officials.");
    }
}
