using System.Collections.Immutable;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Collection.SenateVotes;
using CivicLens.Application.Collection.Votes;
using CivicLens.Application.Documents;
using CivicLens.Application.Roster;
using CivicLens.Core.Documents;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public sealed class VoteReader(ICollectionJobStore jobs, IMemberRosterStore roster, ApiResponseReader responses, string artifactRoot)
{
    private sealed record RosterLisIds(Dictionary<string, string> Tracked, HashSet<string> Known);

    private sealed record ChamberRead(DateOnly Day, string[] DetailPrefixes, ImmutableArray<MemberVote> Members);

    public async Task<VoteRead> ReadAsync(VoteDraftEvidence evidence, CancellationToken cancellationToken)
    {
        if (BillError(evidence) is { } billError) return new(null, billError);
        var (chamberRead, chamberError) = evidence.Candidate.Chamber == Chamber.Senate
            ? await ReadSenateAsync(evidence, cancellationToken)
            : await ReadHouseAsync(evidence, cancellationToken);
        if (chamberRead is null) return new(null, chamberError);

        CongressBill? bill = null;
        if (evidence.BillDetail is { } billPage)
        {
            bill = await LoadBillAsync(billPage, cancellationToken);
            if (TextError(billPage, CongressBillRenderer.RenderDetail(bill)) is { } billTextError) return new(null, billTextError);
        }
        return new(Assemble(evidence, chamberRead, bill?.PolicyArea), null);
    }

    private async Task<(ChamberRead?, string? Error)> ReadHouseAsync(VoteDraftEvidence evidence, CancellationToken cancellationToken)
    {
        var candidate = evidence.Candidate;
        if (!HasCurrentVersions(evidence.Detail, HouseVoteParser.Version) || !HasCurrentVersions(evidence.Members, HouseVoteMembersParser.Version))
            return (null, VoteDraftErrorCodes.ExtractionVersionChanged);
        var roll = new HouseVoteRoll(new VoteSession(candidate.Congress, candidate.SessionNumber), candidate.RollCallNumber);
        var detail = HouseVoteParser.Parse((await LoadAsync(evidence.Detail, cancellationToken)).Body, roll);
        if (TextError(evidence.Detail, HouseVoteRenderer.RenderDetail(detail)) is { } detailError) return (null, detailError);
        var members = HouseVoteMembersParser.Parse((await LoadAsync(evidence.Members, cancellationToken)).Body, roll);
        if (TextError(evidence.Members, HouseVoteRenderer.RenderMembers(members)) is { } membersError) return (null, membersError);
        if (detail.StartDate is not { } start) return (null, VoteDraftErrorCodes.EvidenceUnavailable);
        var day = DateOnly.FromDateTime(start.DateTime);
        var tracked = TrackedOn(evidence, day);
        return (new(day, VoteFactPrefixes.Detail, HouseMemberVotesOf(evidence.Members, members, tracked)), null);
    }

    private async Task<(ChamberRead?, string? Error)> ReadSenateAsync(VoteDraftEvidence evidence, CancellationToken cancellationToken)
    {
        var candidate = evidence.Candidate;
        var page = evidence.Detail;
        if (page.ParserVersion != SenateVoteParser.Version || page.NormalizationVersion != SenateVoteRenderer.NormalizationVersion)
            return (null, VoteDraftErrorCodes.ExtractionVersionChanged);
        var roll = new SenateVoteRoll(new VoteSession(candidate.Congress, candidate.SessionNumber), candidate.RollCallNumber);
        var detail = SenateVoteParser.Parse((await LoadAsync(page, cancellationToken)).Body, roll);
        if (TextError(page, SenateVoteRenderer.Render(detail)) is { } textError) return (null, textError);
        var day = DateOnly.FromDateTime(detail.VoteDate.DateTime);
        var tracked = TrackedOn(evidence, day);
        var lisIds = await LisIdsOfAsync(evidence, tracked, cancellationToken);
        if (lisIds is null || detail.Members.Any(member => !lisIds.Known.Contains(member.LisId)))
            return (null, VoteDraftErrorCodes.EvidenceUnavailable);
        return (new(day, VoteFactPrefixes.SenateDetail, SenateMemberVotesOf(page, detail, tracked, lisIds.Tracked)), null);
    }

    private async Task<RosterLisIds?> LisIdsOfAsync(VoteDraftEvidence evidence, PersonConfiguration[] tracked,
        CancellationToken cancellationToken)
    {
        var rosterSource = evidence.ConfigurationRevision.Configuration.RosterSourceOf(ApiParser.CongressLegislators);
        if (rosterSource is null) return null;
        var trackedIds = tracked.Select(person => person.BioguideId!).ToHashSet(StringComparer.Ordinal);
        var all = await roster.ReadSenatorLisIdsAsync(rosterSource.Id, cancellationToken);
        var senators = all.Where(item => trackedIds.Contains(item.BioguideId)).GroupBy(item => item.BioguideId);
        var lisIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var senator in senators)
        {
            var ids = senator.Select(item => item.LisId).Where(id => id is not null).Distinct(StringComparer.Ordinal).ToArray();
            if (ids.Length != 1) return null;
            lisIds[senator.Key] = ids[0]!;
        }
        return new(lisIds, [.. all.Select(item => item.LisId).OfType<string>()]);
    }

    private static PersonConfiguration[] TrackedOn(VoteDraftEvidence evidence, DateOnly day)
    {
        var configuration = evidence.ConfigurationRevision.Configuration;
        var source = configuration.Sources.Single(item => item.Id == evidence.Candidate.SourceId);
        return configuration.CoveredBioguidePeopleOf(source, day);
    }

    private static VoteSnapshot Assemble(VoteDraftEvidence evidence, ChamberRead read, string? policyArea)
    {
        var configuredPeople = evidence.ConfigurationRevision.Configuration.People;
        var facts = FactsOf(evidence.Detail, read.DetailPrefixes);
        if (evidence.BillDetail is { } billPage) facts = facts.AddRange(FactsOf(billPage, VoteFactPrefixes.Bill));
        var involved = read.Members.Select(item => item.PersonId).ToHashSet(StringComparer.Ordinal);
        var chamber = evidence.Candidate.Chamber;
        var (kind, kindCitation) = chamber == Chamber.Senate ? SenateVoteKindTable.Classify(facts) : VoteKindTable.Classify(facts);
        var votedOnCitation = VoteFacts.Find(facts, VoteFactPrefixes.Started)!.Citation;
        return new(chamber, facts, votedOnCitation, kind, kindCitation, read.Members, VoteFingerprint.Compute(facts, read.Members),
            policyArea, read.Day,
            configuredPeople.Where(person => involved.Contains(person.Id)).ToImmutableDictionary(person => person.Id, person => person.Name),
            [.. configuredPeople.Select(person => person.Name)]);
    }

    private static string? BillError(VoteDraftEvidence evidence)
    {
        if (evidence.BillDetail is not { } page) return evidence.Candidate.BillType is null ? null : VoteDraftErrorCodes.EvidenceUnavailable;
        return page.ParserVersion == CongressBillParser.Version && page.NormalizationVersion == CongressBillRenderer.NormalizationVersion
            ? null : VoteDraftErrorCodes.ExtractionVersionChanged;
    }

    private static ImmutableArray<VoteFact> FactsOf(VoteDraftPage page, string[] prefixes) =>
        [.. DocumentLines.Split(page.Text).Where(line => prefixes.Any(prefix => line.Text.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(line => new VoteFact(LineCitations.Content(line), LineCitations.Span(page.ExtractionId, line)))];

    private static ImmutableArray<MemberVote> HouseMemberVotesOf(VoteDraftPage page, HouseVoteMemberVotes members, PersonConfiguration[] tracked)
    {
        var citations = LineCitations.ExactLines(page.ExtractionId, page.Text);
        return [.. members.Members.Join(tracked, member => member.BioguideId, person => person.BioguideId,
                (member, person) => new MemberVote(person.Id, member.BioguideId, RenderedText.SingleLine(member.VoteCast), citations[HouseVoteRenderer.MemberLine(member)]))
            .DistinctBy(vote => vote.BioguideId).OrderBy(vote => vote.BioguideId, StringComparer.Ordinal)];
    }

    private static ImmutableArray<MemberVote> SenateMemberVotesOf(VoteDraftPage page, SenateVoteDetail detail, PersonConfiguration[] tracked,
        Dictionary<string, string> lisIds)
    {
        var citations = LineCitations.ExactLines(page.ExtractionId, page.Text);
        var people = tracked.Where(person => lisIds.ContainsKey(person.BioguideId!));
        return [.. detail.Members.Join(people, member => member.LisId, person => lisIds[person.BioguideId!],
                (member, person) => new MemberVote(person.Id, person.BioguideId!, RenderedText.SingleLine(member.VoteCast), citations[SenateVoteRenderer.MemberLine(member)]))
            .DistinctBy(vote => vote.BioguideId).OrderBy(vote => vote.BioguideId, StringComparer.Ordinal)];
    }

    private async Task<CongressBill> LoadBillAsync(VoteDraftPage page, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(page.JobId, cancellationToken) ?? throw new InvalidDataException("A bill detail job is missing.");
        return CongressBillDetailJob.Parse(job, await responses.ReadAsync(job, artifactRoot, cancellationToken));
    }

    private async Task<ApiResponse> LoadAsync(VoteDraftPage page, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(page.JobId, cancellationToken) ?? throw new InvalidDataException("A vote page job is missing.");
        return await responses.ReadAsync(job, artifactRoot, cancellationToken);
    }

    private static bool HasCurrentVersions(VoteDraftPage page, string parserVersion) =>
        page.ParserVersion == parserVersion && page.NormalizationVersion == HouseVoteRenderer.NormalizationVersion;

    private static string? TextError(VoteDraftPage page, string rendered) =>
        string.Equals(rendered, page.Text, StringComparison.Ordinal) ? null : VoteDraftErrorCodes.RenderedTextMismatch;
}
