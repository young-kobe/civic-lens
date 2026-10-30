using System.Globalization;

namespace CivicLens.Publication.Contracts;

public sealed record PublishedVote
{
    private const string StartedPrefix = "Started: ";

    public const int MaximumHeadlineLength = 256;
    public const int MaximumBillTypeLength = 16;
    public const int MaximumCastLength = 32;
    public const int MaximumFacts = 64;
    public const int MaximumMembers = 600;
    public const int MaximumSources = 3;
    public const int MaximumParties = 16;
    public const string ResultPrefix = "Result: ";
    public const string CandidateTotalPrefix = "Candidate total ";
    public const string PartyTotalPrefix = "Party total ";

    public required string RecordId { get; init; }
    public required int RevisionNumber { get; init; }
    public required string Headline { get; init; }
    public required string Summary { get; init; }
    public required PublishedTextOrigin TextOrigin { get; init; }
    public required PublishedChamber Chamber { get; init; }
    public required int Congress { get; init; }
    public required int Session { get; init; }
    public required int RollCallNumber { get; init; }
    public PublishedVoteSource Source { get; init; } = PublishedVoteSource.CongressGov;
    public required PublishedVoteKind Kind { get; init; }
    public PublishedCitation? KindCitation { get; init; }
    public required DateOnly VotedOn { get; init; }
    public required string Result { get; init; }
    public PublishedVoteTotals? Totals { get; init; }
    public required PublishedPartyMajority[] PartyMajorities { get; init; }
    public required PublishedCitation VotedOnCitation { get; init; }
    public required PublishedVoteFact[] Facts { get; init; }
    public required PublishedMemberVote[] Members { get; init; }
    public string? BillType { get; init; }
    public int? BillNumber { get; init; }
    public required string[] TrackedIssueIds { get; init; }
    public required PublishedDocumentVersion[] Sources { get; init; }
    public required DateTimeOffset ApprovedAtUtc { get; init; }
    public required DateTimeOffset FirstPublishedAtUtc { get; init; }
    public required PublishedCorrection[] Corrections { get; init; }

    private bool HasCandidateTotals => IsCandidateVote(Facts.Select(fact => fact.Text));

    public static bool IsCandidateVote(IEnumerable<string> factTexts)
    {
        var texts = factTexts.ToArray();
        return texts.Any(text => text.StartsWith(CandidateTotalPrefix, StringComparison.Ordinal)) &&
            !texts.Any(text => text.StartsWith(PartyTotalPrefix, StringComparison.Ordinal));
    }

    public bool IsSplit() => SubstantiveMajorities() is { } pair && pair.Democrat != pair.Republican;

    public bool IsBipartisan() => SubstantiveMajorities() is { } pair && pair.Democrat == pair.Republican;

    public string? MajorityOf(string party) => PartyMajorities.SingleOrDefault(majority => majority.Party == party)?.Majority;

    private (string Democrat, string Republican)? SubstantiveMajorities() =>
        Kind.IsSubstantive() && MajorityOf(PublishedPartyMajority.Democrat) is { } democrat && MajorityOf(PublishedPartyMajority.Republican) is { } republican
            ? (democrat, republican)
            : null;

    public PublishedRecordEntry ToEntry() => new()
    {
        Kind = PublishedRecordKind.Vote,
        RecordId = RecordId,
        RevisionNumber = RevisionNumber,
        Headline = Headline,
        FirstPublishedAtUtc = FirstPublishedAtUtc,
        Officials = [],
        IssueIds = TrackedIssueIds,
        PolicyAreaIds = [],
        SourceHost = Sources[0].Host,
        EventDate = VotedOn
    };

    public IEnumerable<PublishedCitation> Citations() => new[] { VotedOnCitation }.Concat(KindCitation is { } kind ? [kind] : [])
        .Concat(Facts.Select(fact => fact.Citation)).Concat(Members.Select(member => member.Citation));

    public void Validate()
    {
        PublishedCorrection.ValidateAll(Corrections, RevisionNumber);
        if (!PublicationProtocol.IsRecordId(PublishedRecordKind.Vote, RecordId) || RevisionNumber < 1 ||
            string.IsNullOrWhiteSpace(Headline) || Headline.Length > MaximumHeadlineLength || string.IsNullOrWhiteSpace(Summary))
            throw new InvalidDataException("Published record identity or wording is invalid.");
        if (!Enum.IsDefined(Chamber) || !Enum.IsDefined(Source) || Congress < 1 || Session is not (1 or 2) || RollCallNumber < 1)
            throw new InvalidDataException("Published vote requires a roll call identity.");
        if ((BillType is null) != (BillNumber is null) || BillType is not null && (string.IsNullOrWhiteSpace(BillType) || BillType.Length > MaximumBillTypeLength || BillNumber < 1))
            throw new InvalidDataException($"A published vote bill type has at most {MaximumBillTypeLength} characters and a number, and a number has a type.");
        PublishedEvidenceRules.RequireSources(Sources, MaximumSources, "vote");
        if (!Enum.IsDefined(Kind) || Kind != PublishedVoteKind.Other && KindCitation is null)
            throw new InvalidDataException("A published vote kind other than Other requires a citation.");
        ValidateFacts();
        ValidateResult();
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

    private void ValidateResult()
    {
        if (!PublishedEvidenceRules.IsBoundedText(Result) || Facts.All(fact => fact.Text != ResultPrefix + Result))
            throw new InvalidDataException("A published vote result must be the text of a cited result line.");
        if (HasCandidateTotals)
        {
            if (Totals is not null || PartyMajorities is not { Length: 0 })
                throw new InvalidDataException("A published vote with candidate totals has no totals and no party majorities.");
            return;
        }
        if (Totals is null || PartyMajorities is null || PartyMajorities.Length is 0 or > MaximumParties || PartyMajorities.Any(party => party is null))
            throw new InvalidDataException($"Published vote requires totals and 1 to {MaximumParties} party majorities.");
        Totals.Validate();
        foreach (var party in PartyMajorities) party.Validate();
        if (!PublishedEvidenceRules.AreDistinctAndNotBlank(PartyMajorities.Select(party => party.Party)) ||
            PartyMajorities.Sum(party => party.YeaCount) != Totals.Yea || PartyMajorities.Sum(party => party.NayCount) != Totals.Nay)
            throw new InvalidDataException("Published party majorities must name distinct parties and add up to the vote totals.");
    }

    private bool IsKnownParty(string? party) =>
        HasCandidateTotals ? PublishedEvidenceRules.IsBoundedText(party) : PartyMajorities.Any(majority => majority.Party == party);

    private void ValidateMembers()
    {
        if (Members is null || Members.Length is 0 or > MaximumMembers)
            throw new InvalidDataException($"Published vote requires 1 to {MaximumMembers} member votes.");
        if (Members.Any(member => member?.Official is null || member.Citation is null || string.IsNullOrWhiteSpace(member.Official.Id) ||
                string.IsNullOrWhiteSpace(member.Official.Name) || string.IsNullOrWhiteSpace(member.Cast) ||
                !IsKnownParty(member.Party) || member.Cast.Length > MaximumCastLength ||
                member.Citation.Quote?.EndsWith(", " + member.Cast, StringComparison.Ordinal) != true))
            throw new InvalidDataException($"A published member vote requires a named official and a cast of at most {MaximumCastLength} characters that its citation shows.");
        if (!PublishedEvidenceRules.AreDistinctAndNotBlank(Members.Select(member => member.Official.Id)))
            throw new InvalidDataException("Published vote members must be distinct officials.");
    }
}
