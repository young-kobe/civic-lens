using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    public const string QuestionPrefix = "Question: ";
    public const string LegislationPrefix = "Legislation: ";
    public const string TitlePrefix = "Title: ";
    public const string ResultPrefix = "Result: ";
    public const string CountPrefix = "Count: ";
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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PublishedPartyTally[]? PartyTallies { get; init; }
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

    public static IReadOnlyDictionary<string, int>? CandidateCountsOf(IEnumerable<string> factTexts)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var total = 0;
        foreach (var text in factTexts.Where(text => text.StartsWith(CandidateTotalPrefix, StringComparison.Ordinal)))
        {
            var separator = text.LastIndexOf(": ", StringComparison.Ordinal);
            if (separator <= CandidateTotalPrefix.Length) return null;
            var label = text[CandidateTotalPrefix.Length..separator];
            if (label.Contains(", ", StringComparison.Ordinal) || !PublishedEvidenceRules.IsBoundedText(label) ||
                !int.TryParse(text.AsSpan(separator + 2), NumberStyles.None, CultureInfo.InvariantCulture, out var count) ||
                count is < 0 or > MaximumMembers || !counts.TryAdd(label, count))
                return null;
            total += count;
            if (total > MaximumMembers) return null;
        }
        return total > 0 ? counts : null;
    }

    /// <summary>True when only the headline or the summary differs from the earlier revision of the same record. Computed party data counts as a vote fact.</summary>
    public bool IsTextRewriteOf(PublishedVote earlier) =>
        TrackedIssueIds.SequenceEqual(earlier.TrackedIssueIds) && (Headline != earlier.Headline || Summary != earlier.Summary) &&
        FactsKey(this) == FactsKey(earlier);

    private static string FactsKey(PublishedVote vote) => JsonSerializer.Serialize(vote with
    {
        RevisionNumber = 0,
        Headline = "",
        Summary = "",
        TextOrigin = default,
        TrackedIssueIds = [],
        ApprovedAtUtc = default,
        FirstPublishedAtUtc = default,
        Corrections = []
    }, PublicationProtocol.JsonOptions);

    public bool IsSplit() => SubstantiveMajorities() is { } pair && pair.Democrat != pair.Republican;

    public bool PartiesAgree() => SubstantiveMajorities() is { } pair && pair.Democrat == pair.Republican;

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
        ApprovedAtUtc = ApprovedAtUtc,
        Officials = [],
        IssueIds = TrackedIssueIds,
        PolicyAreaIds = [],
        SourceHost = Sources[0].Host,
        EventDate = VotedOn,
        YeaCount = Totals?.Yea,
        NayCount = Totals?.Nay,
        Alignment = AlignmentOfParties(),
        Chamber = Chamber,
        VoteKind = Kind,
        Congress = Congress
    };

    public PublishedVoteAlignment? AlignmentOfParties()
    {
        if (!Kind.IsSubstantive() || Totals is null || !HasBothMajorParties) return null;
        return IsSplit() ? PublishedVoteAlignment.Split : PartiesAgree() ? PublishedVoteAlignment.PartiesAgree : PublishedVoteAlignment.Mixed;
    }

    private bool HasBothMajorParties =>
        PartyMajorities.Any(party => party.Party == PublishedPartyMajority.Democrat) && PartyMajorities.Any(party => party.Party == PublishedPartyMajority.Republican);

    public IEnumerable<PublishedCitation> Citations() => new[] { VotedOnCitation }.Concat(KindCitation is { } kind ? [kind] : [])
        .Concat(Facts.Select(fact => fact.Citation)).Concat(Members.Select(member => member.Citation));

    public static void ValidateEvidence(
        PublishedDocumentVersion[]? sources,
        DateOnly votedOn,
        PublishedVoteKind kind,
        PublishedCitation? kindCitation,
        PublishedCitation? votedOnCitation,
        string? result,
        PublishedVoteFact[]? facts,
        (string? Cast, PublishedCitation? Citation)[]? memberEvidence)
    {
        PublishedEvidenceRules.RequireSources(sources, MaximumSources, "vote");
        if (!Enum.IsDefined(kind) || kind != PublishedVoteKind.Other && kindCitation is null)
            throw new InvalidDataException("A published vote kind other than Other requires a citation.");
        if (facts is null || facts.Length > MaximumFacts || facts.Any(fact => fact?.Citation is null || fact.Text != fact.Citation.Quote))
            throw new InvalidDataException($"Published vote requires at most {MaximumFacts} facts that equal their citations.");
        if (!PublishedEvidenceRules.IsBoundedText(result) || facts.All(fact => fact.Text != ResultPrefix + result))
            throw new InvalidDataException("A published vote result must be the text of a cited result line.");
        if (memberEvidence is null || memberEvidence.Length is 0 or > MaximumMembers)
            throw new InvalidDataException($"Published vote requires 1 to {MaximumMembers} member vote citations.");

        var citations = new[] { votedOnCitation! }
            .Concat(kindCitation is { } kindEvidence ? [kindEvidence] : [])
            .Concat(facts.Select(fact => fact.Citation))
            .Concat(memberEvidence.Select(member => member.Citation!));
        ValidateMemberEvidenceCore(memberEvidence);
        foreach (var citation in citations) PublishedEvidenceRules.RequireCitation(sources, citation, "vote");
        PublishedEvidenceRules.RequireEverySourceCited(sources, citations, "vote");
        if (!votedOnCitation!.Quote.StartsWith(StartedPrefix + votedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal))
            throw new InvalidDataException("The vote day must be the date of the cited start line.");
    }

    public static void ValidateMemberEvidence(
        PublishedDocumentVersion[]? sources,
        (string? Cast, PublishedCitation? Citation)[]? memberEvidence)
    {
        PublishedEvidenceRules.RequireSources(sources, MaximumSources, "vote");
        if (memberEvidence is null || memberEvidence.Length is 0 or > MaximumMembers)
            throw new InvalidDataException($"Published vote requires 1 to {MaximumMembers} member vote citations.");

        ValidateMemberCitations(sources, memberEvidence);
        PublishedEvidenceRules.RequireEverySourceCited(sources, memberEvidence.Select(member => member.Citation!), "vote");
    }

    /// <summary>Checks each member cast against its cited line and each citation against the sources. A caller that holds the sources of the whole vote uses this check, because other citations of the vote may cite some sources.</summary>
    public static void ValidateMemberCitations(PublishedDocumentVersion[] sources, (string? Cast, PublishedCitation? Citation)[] memberEvidence)
    {
        ValidateMemberEvidenceCore(memberEvidence);
        foreach (var member in memberEvidence) PublishedEvidenceRules.RequireCitation(sources, member.Citation!, "vote");
    }

    private static void ValidateMemberEvidenceCore((string? Cast, PublishedCitation? Citation)[] memberEvidence)
    {
        if (memberEvidence.Any(member => string.IsNullOrWhiteSpace(member.Cast) || member.Cast.Length > MaximumCastLength ||
                member.Citation?.Quote?.EndsWith(", " + member.Cast, StringComparison.Ordinal) != true))
            throw new InvalidDataException($"A published member vote requires a named official and a cast of at most {MaximumCastLength} characters that its citation shows.");
    }

    public void Validate()
    {
        PublishedCorrection.ValidateAll(Corrections, RevisionNumber, PublishedRecordKind.Vote);
        if (!PublicationProtocol.IsRecordId(PublishedRecordKind.Vote, RecordId) || RevisionNumber < 1 ||
            string.IsNullOrWhiteSpace(Headline) || Headline.Length > MaximumHeadlineLength || string.IsNullOrWhiteSpace(Summary))
            throw new InvalidDataException("Published record identity or wording is invalid.");
        if (!Enum.IsDefined(Chamber) || !Enum.IsDefined(Source) || Congress < 1 || Session is not (1 or 2) || RollCallNumber < 1)
            throw new InvalidDataException("Published vote requires a roll call identity.");
        if ((BillType is null) != (BillNumber is null) || BillType is not null && (string.IsNullOrWhiteSpace(BillType) || BillType.Length > MaximumBillTypeLength || BillNumber < 1))
            throw new InvalidDataException($"A published vote bill type has at most {MaximumBillTypeLength} characters and a number, and a number has a type.");
        ValidateEvidence(Sources, VotedOn, Kind, KindCitation, VotedOnCitation, Result, Facts,
            Members?.Take(MaximumMembers + 1).Select(member => (member?.Cast, member?.Citation)).ToArray());
        ValidateResult();
        ValidateMembers();
        ValidateTrackedPartyTallies();
        if (TrackedIssueIds is null || !PublishedEvidenceRules.AreDistinctAndNotBlank(TrackedIssueIds))
            throw new InvalidDataException("Published vote tracked issue ids must be distinct and not blank.");
    }

    private void ValidateResult()
    {
        if (HasCandidateTotals)
        {
            if (Totals is not null || PartyMajorities is not { Length: 0 })
                throw new InvalidDataException("A published vote with candidate totals has no totals and no party majorities.");
            ValidateCandidatePartyTallies();
            return;
        }
        if (Totals is null || PartyMajorities is null || PartyMajorities.Length is 0 or > MaximumParties || PartyMajorities.Any(party => party is null))
            throw new InvalidDataException($"Published vote requires totals and 1 to {MaximumParties} party majorities.");
        Totals.Validate();
        foreach (var party in PartyMajorities) party.Validate();
        if (!PublishedEvidenceRules.AreDistinctAndNotBlank(PartyMajorities.Select(party => party.Party)) ||
            PartyMajorities.Sum(party => party.YeaCount) != Totals.Yea || PartyMajorities.Sum(party => party.NayCount) != Totals.Nay)
            throw new InvalidDataException("Published party majorities must name distinct parties and add up to the vote totals.");
        ValidatePartyTallies();
    }

    private void ValidatePartyTallies()
    {
        if (PartyTallies is null) return;
        if (PartyTallies.Length is 0 or > MaximumParties || PartyTallies.Any(tally => tally is null))
            throw new InvalidDataException($"Published vote requires 1 to {MaximumParties} party tallies when they are available.");
        foreach (var tally in PartyTallies) tally.Validate();
        if (!PublishedEvidenceRules.AreDistinctAndNotBlank(PartyTallies.Select(tally => tally.Party)))
            throw new InvalidDataException("Published party tallies must name distinct parties.");

        var majorities = PartyMajorities.ToDictionary(party => party.Party, StringComparer.Ordinal);
        if (PartyTallies.Length != majorities.Count || PartyTallies.Any(tally => !majorities.TryGetValue(tally.Party, out var majority) ||
                tally.Yea != majority.YeaCount || tally.Nay != majority.NayCount || tally.Other != 0) ||
            PartyTallies.Sum(tally => tally.Yea) != Totals!.Yea || PartyTallies.Sum(tally => tally.Nay) != Totals.Nay ||
            PartyTallies.Sum(tally => tally.Present) != Totals.Present || PartyTallies.Sum(tally => tally.NotVoting) != Totals.NotVoting)
            throw new InvalidDataException("Published party tallies must match the party majorities and vote totals.");
    }

    private void ValidateCandidatePartyTallies()
    {
        if (PartyTallies is null) return;
        if (PartyTallies.Length is 0 or > MaximumParties || PartyTallies.Any(tally => tally is null))
            throw new InvalidDataException($"Published candidate vote requires 1 to {MaximumParties} party tallies when they are available.");
        foreach (var tally in PartyTallies) tally.Validate();
        var counts = CandidateCountsOf(Facts.Select(fact => fact.Text));
        if (!PublishedEvidenceRules.AreDistinctAndNotBlank(PartyTallies.Select(tally => tally.Party)) ||
            PartyTallies.Any(tally => tally.Yea != 0 || tally.Nay != 0) || counts is null ||
            counts.Any(pair => pair.Value > 0 && PublishedOfficialVote.SideOf(pair.Key) is not null) ||
            PartyTallies.Sum(tally => tally.Present) != counts.GetValueOrDefault(PublishedOfficialVote.Present) ||
            PartyTallies.Sum(tally => tally.NotVoting) != counts.GetValueOrDefault(PublishedOfficialVote.NotVoting) ||
            PartyTallies.Sum(tally => tally.Present + tally.NotVoting + tally.Other) != counts.Values.Sum())
            throw new InvalidDataException("Published candidate party tallies must match the cited candidate cast counts under distinct parties.");
    }

    private void ValidateTrackedPartyTallies()
    {
        if (PartyTallies is null) return;
        var tallies = PartyTallies.ToDictionary(tally => tally.Party, StringComparer.Ordinal);
        foreach (var group in Members.GroupBy(member => member.Party, StringComparer.Ordinal))
        {
            if (!tallies.TryGetValue(group.Key, out var tally))
                throw new InvalidDataException("Published party tallies must include every tracked member's party.");
            var casts = group.ToLookup(member => PublishedOfficialVote.SideOf(member.Cast) ?? member.Cast);
            var yea = casts[PublishedPartyMajority.Yea].Count();
            var nay = casts[PublishedPartyMajority.Nay].Count();
            var present = casts[PublishedOfficialVote.Present].Count();
            var notVoting = casts[PublishedOfficialVote.NotVoting].Count();
            if (yea > tally.Yea || nay > tally.Nay || present > tally.Present || notVoting > tally.NotVoting ||
                group.Count() - yea - nay - present - notVoting > tally.Other)
                throw new InvalidDataException("Published party tallies must cover every tracked member's cast.");
        }
    }

    private bool IsKnownParty(string? party) =>
        HasCandidateTotals ? PublishedEvidenceRules.IsBoundedText(party) : PartyMajorities.Any(majority => majority.Party == party);

    private void ValidateMembers()
    {
        if (Members is null || Members.Length is 0 or > MaximumMembers)
            throw new InvalidDataException($"Published vote requires 1 to {MaximumMembers} member votes.");
        if (Members.Any(member => member?.Official is null || member.Citation is null || string.IsNullOrWhiteSpace(member.Official.Id) ||
                string.IsNullOrWhiteSpace(member.Official.Name) || !IsKnownParty(member.Party)))
            throw new InvalidDataException($"A published member vote requires a named official and a cast of at most {MaximumCastLength} characters that its citation shows.");
        if (!PublishedEvidenceRules.AreDistinctAndNotBlank(Members.Select(member => member.Official.Id)))
            throw new InvalidDataException("Published vote members must be distinct officials.");
    }
}
