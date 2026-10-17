using System.Collections.Immutable;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Documents;
using CivicLens.Core.Documents;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public sealed class VoteReader(ICollectionJobStore jobs, ApiResponseReader responses, string artifactRoot)
{
    private const string BillTitlePrefix = "Title: ";
    private const string PolicyAreaPrefix = "Policy area: ";

    private static readonly string[] FactPrefixes = ["Started: ", "Question: ", "Result: ", "Legislation: ", "Party total ", "Candidate total "];

    public async Task<VoteRead> ReadAsync(VoteDraftEvidence evidence, CancellationToken cancellationToken)
    {
        var candidate = evidence.Candidate;
        if (BillError(evidence) is { } billError) return new(null, billError);
        if (!HasCurrentVersions(evidence.Detail, HouseVoteParser.Version) || !HasCurrentVersions(evidence.Members, HouseVoteMembersParser.Version))
            return new(null, VoteDraftErrorCodes.ExtractionVersionChanged);
        var roll = new HouseVoteRoll(new HouseVoteSession(candidate.Congress, candidate.SessionNumber), candidate.RollCallNumber);
        var detail = HouseVoteParser.Parse((await LoadAsync(evidence.Detail, cancellationToken)).Body, roll);
        if (TextError(evidence.Detail, HouseVoteRenderer.RenderDetail(detail)) is { } detailError) return new(null, detailError);
        var members = HouseVoteMembersParser.Parse((await LoadAsync(evidence.Members, cancellationToken)).Body, roll);
        if (TextError(evidence.Members, HouseVoteRenderer.RenderMembers(members)) is { } membersError) return new(null, membersError);

        var configuration = evidence.ConfigurationRevision.Configuration;
        var source = configuration.Sources.Single(item => item.Id == candidate.SourceId);
        var day = VoteDayOf(detail, candidate.UpdateDate);
        var tracked = configuration.CoveredBioguidePeopleOf(source, day);
        return new(Assemble(evidence, members, tracked, configuration.People, day), null);
    }

    private static DateOnly VoteDayOf(HouseVoteDetail detail, DateTimeOffset updateDate) =>
        detail.StartDate is { } start ? DateOnly.FromDateTime(start.DateTime) : DateOnly.FromDateTime(updateDate.UtcDateTime);

    private static VoteSnapshot Assemble(VoteDraftEvidence evidence, HouseVoteMemberVotes members, PersonConfiguration[] tracked,
        PersonConfiguration[] configuredPeople, DateOnly day)
    {
        var billFacts = BillFactsOf(evidence.BillDetail);
        var facts = FactsOf(evidence.Detail).AddRange(billFacts);
        var memberVotes = MemberVotesOf(evidence.Members, members, tracked);
        var involved = memberVotes.Select(item => item.PersonId).ToHashSet(StringComparer.Ordinal);
        return new(facts, memberVotes, VoteFingerprint.Compute(facts, memberVotes),
            billFacts.FirstOrDefault(fact => fact.Text.StartsWith(PolicyAreaPrefix, StringComparison.Ordinal))?.Text[PolicyAreaPrefix.Length..], day,
            configuredPeople.Where(person => involved.Contains(person.Id)).ToImmutableDictionary(person => person.Id, person => person.Name),
            [.. configuredPeople.Select(person => person.Name)]);
    }

    private static string? BillError(VoteDraftEvidence evidence)
    {
        if (evidence.BillDetail is not { } page) return evidence.Candidate.BillType is null ? null : VoteDraftErrorCodes.EvidenceUnavailable;
        if (page.ParserVersion != CongressBillParser.Version || page.NormalizationVersion != CongressBillRenderer.NormalizationVersion)
            return VoteDraftErrorCodes.ExtractionVersionChanged;
        return BillFactOf(page, DocumentLines.Split(page.Text), BillTitlePrefix) is null ? VoteDraftErrorCodes.RenderedTextMismatch : null;
    }

    private static ImmutableArray<VoteFact> BillFactsOf(VoteDraftPage? page)
    {
        if (page is null) return [];
        var lines = DocumentLines.Split(page.Text);
        return [.. new[] { BillTitlePrefix, PolicyAreaPrefix }.Select(prefix => BillFactOf(page, lines, prefix)).OfType<VoteFact>()];
    }

    private static VoteFact? BillFactOf(VoteDraftPage page, ImmutableArray<DocumentLine> lines, string prefix) =>
        LineCitations.Line(page.ExtractionId, lines, prefix) is { } span ? new VoteFact(page.Text.Substring(span.Start, span.Length), span) : null;

    private static ImmutableArray<VoteFact> FactsOf(VoteDraftPage page) =>
        [.. DocumentLines.Split(page.Text).Where(line => FactPrefixes.Any(prefix => line.Text.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(line => new VoteFact(LineCitations.Content(line), LineCitations.Span(page.ExtractionId, line)))];

    private static ImmutableArray<MemberVote> MemberVotesOf(VoteDraftPage page, HouseVoteMemberVotes members, PersonConfiguration[] tracked)
    {
        var citations = LineCitations.ExactLines(page.ExtractionId, page.Text);
        return [.. members.Members.Join(tracked, member => member.BioguideId, person => person.BioguideId,
                (member, person) => new MemberVote(person.Id, member.BioguideId, member.VoteCast, citations[HouseVoteRenderer.MemberLine(member)]))
            .DistinctBy(vote => vote.BioguideId).OrderBy(vote => vote.BioguideId, StringComparer.Ordinal)];
    }

    private async Task<ApiResponse> LoadAsync(VoteDraftPage page, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(page.JobId, cancellationToken) ?? throw new InvalidDataException("A House vote page job is missing.");
        return await responses.ReadAsync(job, artifactRoot, cancellationToken);
    }

    private static bool HasCurrentVersions(VoteDraftPage page, string parserVersion) =>
        page.ParserVersion == parserVersion && page.NormalizationVersion == HouseVoteRenderer.NormalizationVersion;

    private static string? TextError(VoteDraftPage page, string rendered) =>
        string.Equals(rendered, page.Text, StringComparison.Ordinal) ? null : VoteDraftErrorCodes.RenderedTextMismatch;
}
