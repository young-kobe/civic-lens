using System.Collections.Immutable;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Collection.Votes;
using CivicLens.Application.Collection.VoteviewVotes;
using CivicLens.Application.Documents;
using CivicLens.Core.Documents;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public sealed class VoteReader(ICollectionJobStore jobs, ApiResponseReader responses, string artifactRoot)
{
    private sealed record ChamberRead(DateOnly Day, string[] DetailPrefixes, ImmutableArray<MemberVote> Members);

    public async Task<VoteRead> ReadAsync(VoteDraftEvidence evidence, CancellationToken cancellationToken)
    {
        if (BillError(evidence) is { } billError) return new(null, billError);
        var (chamberRead, chamberError) = await ReadChamberAsync(evidence, cancellationToken);
        if (chamberRead is null) return new(null, chamberError);

        CongressBill? bill = null;
        if (evidence.BillDetail is { } billPage)
        {
            bill = await LoadBillAsync(billPage, cancellationToken);
            if (TextError(billPage, CongressBillRenderer.RenderDetail(bill)) is { } billTextError) return new(null, billTextError);
        }
        var (subjects, subjectsError) = await ReadSubjectsAsync(evidence.BillSubjects, cancellationToken);
        if (subjectsError is not null) return new(null, subjectsError);
        return new(Assemble(evidence, chamberRead, bill?.PolicyArea, subjects), null);
    }

    private Task<(ChamberRead?, string? Error)> ReadChamberAsync(VoteDraftEvidence evidence, CancellationToken cancellationToken) =>
        VoteSources.Of(SourceOf(evidence)) switch
        {
            VoteSource.CongressGov => ReadHouseClerkAsync(evidence, cancellationToken),
            VoteSource.Voteview => ReadVoteviewAsync(evidence, cancellationToken),
            var source => throw new ArgumentOutOfRangeException(nameof(evidence), source, "Vote source has no reader.")
        };

    private async Task<(ChamberRead?, string? Error)> ReadHouseClerkAsync(VoteDraftEvidence evidence, CancellationToken cancellationToken)
    {
        var candidate = evidence.Candidate;
        if (evidence.Detail.ExtractionVersion != HouseVoteParser.ExtractionVersion ||
            evidence.Members.ExtractionVersion != HouseVoteMembersParser.ExtractionVersion)
            return (null, VoteDraftErrorCodes.ExtractionVersionChanged);
        var roll = new HouseVoteRoll(new VoteSession(candidate.Congress, candidate.SessionNumber), candidate.RollCallNumber);
        var detail = HouseVoteParser.Parse((await LoadAsync(evidence.Detail.JobId, cancellationToken)).Body, roll);
        if (TextError(evidence.Detail, HouseVoteRenderer.RenderDetail(detail)) is { } detailError) return (null, detailError);
        var members = HouseVoteMembersParser.Parse((await LoadAsync(evidence.Members.JobId, cancellationToken)).Body, roll);
        if (TextError(evidence.Members, HouseVoteRenderer.RenderMembers(members)) is { } membersError) return (null, membersError);
        if (detail.StartDate is not { } start) return (null, VoteDraftErrorCodes.EvidenceUnavailable);
        var day = DateOnly.FromDateTime(start.DateTime);
        var tracked = TrackedOn(evidence, day);
        return (new(day, VoteFactPrefixes.Detail, HouseMemberVotesOf(evidence.Members, members, tracked)), null);
    }

    private async Task<(ChamberRead?, string? Error)> ReadVoteviewAsync(VoteDraftEvidence evidence, CancellationToken cancellationToken)
    {
        var candidate = evidence.Candidate;
        var page = evidence.Detail;
        if (page.ExtractionVersion != VoteviewParser.ExtractionVersion)
            return (null, VoteDraftErrorCodes.ExtractionVersionChanged);
        if (candidate.RosterJobId is not { } rosterJobId) return (null, VoteDraftErrorCodes.EvidenceUnavailable);
        var congress = candidate.Congress;
        var rollCall = VoteviewParser.ParseRollCalls((await LoadAsync(page.JobId, cancellationToken)).Body, congress)
            .SingleOrDefault(item => item.Roll == new SenateVoteRoll(new VoteSession(congress, candidate.SessionNumber), candidate.RollCallNumber));
        if (rollCall is null) return (null, VoteDraftErrorCodes.EvidenceUnavailable);
        var members = VoteviewParser.ParseMembers((await LoadAsync(rosterJobId, cancellationToken)).Body, congress);
        var casts = VoteviewParser.ParseCasts((await LoadAsync(evidence.Members.JobId, cancellationToken)).Body, congress);
        if (VoteviewRoll.Join([rollCall], casts, members).SingleOrDefault() is not { } roll || TextError(page, VoteviewRenderer.Render(roll)) is not null)
            return (null, VoteDraftErrorCodes.RenderedTextMismatch);
        var tracked = TrackedOn(evidence, rollCall.Date);
        return (new(rollCall.Date, VoteFactPrefixes.SenateDetail, VoteviewMemberVotesOf(page, roll, tracked)), null);
    }

    private static WatchedSourceConfiguration SourceOf(VoteDraftEvidence evidence) =>
        evidence.ConfigurationRevision.Configuration.Sources.Single(item => item.Id == evidence.Candidate.SourceId);

    private static PersonConfiguration[] TrackedOn(VoteDraftEvidence evidence, DateOnly day) =>
        evidence.ConfigurationRevision.Configuration.CoveredBioguidePeopleOf(SourceOf(evidence), day);

    private static VoteSnapshot Assemble(VoteDraftEvidence evidence, ChamberRead read, string? policyArea, ImmutableArray<string> subjects)
    {
        var configuredPeople = evidence.ConfigurationRevision.Configuration.People;
        var facts = FactsOf(evidence.Detail, read.DetailPrefixes);
        if (evidence.BillDetail is { } billPage) facts = facts.AddRange(FactsOf(billPage, VoteFactPrefixes.Bill));
        var involved = read.Members.Select(item => item.PersonId).ToHashSet(StringComparer.Ordinal);
        var chamber = evidence.Candidate.Chamber;
        var (kind, kindCitation) = chamber == Chamber.Senate ? SenateVoteKindTable.Classify(facts) : VoteKindTable.Classify(facts);
        var votedOnCitation = VoteFacts.Find(facts, VoteFactPrefixes.Started)!.Citation;
        return new(chamber, VoteSources.Of(SourceOf(evidence)), facts, votedOnCitation, kind, kindCitation, read.Members, VoteFingerprint.Compute(facts, read.Members),
            policyArea, subjects, read.Day,
            configuredPeople.Where(person => involved.Contains(person.Id)).ToImmutableDictionary(person => person.Id, person => person.Name),
            [.. configuredPeople.Select(person => person.Name)]);
    }

    private static string? BillError(VoteDraftEvidence evidence)
    {
        if (evidence.BillDetail is not { } page) return evidence.Candidate.BillType is null ? null : VoteDraftErrorCodes.EvidenceUnavailable;
        return page.ExtractionVersion == CongressBillParser.ExtractionVersion
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

    private static ImmutableArray<MemberVote> VoteviewMemberVotesOf(VoteDraftPage page, VoteviewRoll roll, PersonConfiguration[] tracked)
    {
        var citations = LineCitations.ExactLines(page.ExtractionId, page.Text);
        return [.. roll.Votes.Join(tracked, vote => vote.Member.BioguideId, person => person.BioguideId,
                (vote, person) => new MemberVote(person.Id, vote.Member.BioguideId, RenderedText.SingleLine(vote.Cast), citations[VoteviewRenderer.MemberLineOf(vote)]))
            .OrderBy(vote => vote.BioguideId, StringComparer.Ordinal)];
    }

    private async Task<(ImmutableArray<string> Subjects, string? Error)> ReadSubjectsAsync(IReadOnlyList<VoteDraftPage> pages,
        CancellationToken cancellationToken)
    {
        var subjects = ImmutableArray.CreateBuilder<string>();
        foreach (var page in pages)
        {
            if (page.ExtractionVersion != CongressBillSubjectsParser.ExtractionVersion) return (default, VoteDraftErrorCodes.ExtractionVersionChanged);
            var job = await jobs.GetAsync(page.JobId, cancellationToken) ?? throw new InvalidDataException("A bill subjects job is missing.");
            var (bill, parsed) = CongressBillSubjectsJob.Parse(job, await responses.ReadAsync(job, artifactRoot, cancellationToken));
            if (TextError(page, CongressBillRenderer.RenderSubjects(bill, parsed)) is { } error) return (default, error);
            subjects.AddRange(parsed.Subjects);
        }
        return (subjects.ToImmutable(), null);
    }

    private async Task<CongressBill> LoadBillAsync(VoteDraftPage page, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(page.JobId, cancellationToken) ?? throw new InvalidDataException("A bill detail job is missing.");
        return CongressBillDetailJob.Parse(job, await responses.ReadAsync(job, artifactRoot, cancellationToken));
    }

    private async Task<ApiResponse> LoadAsync(string jobId, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(jobId, cancellationToken) ?? throw new InvalidDataException("A vote page job is missing.");
        return await responses.ReadAsync(job, artifactRoot, cancellationToken);
    }

    private static string? TextError(VoteDraftPage page, string rendered) =>
        string.Equals(rendered, page.Text, StringComparison.Ordinal) ? null : VoteDraftErrorCodes.RenderedTextMismatch;
}
