using System.Collections.Immutable;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Documents;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;
using CivicLens.Core.Statements;

namespace CivicLens.Application.Actions;

public sealed class ActionBillReader(ICollectionJobStore jobs, ApiResponseReader responses, IDocumentTextExtractor textExtractor,
    string artifactRoot)
{
    public async Task<ActionBillRead> ReadAsync(ActionDraftEvidence evidence, CancellationToken cancellationToken)
    {
        var candidate = evidence.Candidate;
        var configuration = evidence.ConfigurationRevision.Configuration;
        var detailPage = evidence.Pages.Single(page => page.Resource == ApiResource.Bill);
        if (!evidence.Pages.All(HasCurrentVersion)) return new(null, ActionDraftErrorCodes.ExtractionVersionChanged);
        var (detailJob, detailResponse) = await LoadAsync(detailPage, cancellationToken);
        var bill = CongressBillDetailJob.Parse(detailJob, detailResponse);
        if (TextError(detailPage, CongressBillRenderer.RenderDetail(bill)) is { } detailError) return new(null, detailError);

        var source = configuration.Sources.Single(item => item.Id == candidate.SourceId);
        var people = configuration.CoveredBioguidePeopleOf(source, BillGate.DayOf(bill.UpdateDate));
        var detailLines = DocumentLines.Split(detailPage.Text);
        var sponsorships = ImmutableArray.CreateBuilder<ActionSponsorship>();
        sponsorships.AddRange(SponsorsOf(bill, people, detailPage, detailLines));
        foreach (var page in evidence.Pages.Where(page => page.Resource == ApiResource.BillCosponsors))
        {
            var read = await ReadCosponsorPageAsync(page, candidate, people, cancellationToken);
            if (read.ErrorCode is not null) return new(null, read.ErrorCode);
            sponsorships.AddRange(read.Sponsorships);
        }
        var summaries = new List<(ActionDraftPage Page, CongressBillSummaryText Text)>();
        foreach (var page in evidence.Pages.Where(page => page.Resource == ApiResource.BillSummaries))
        {
            var read = await ReadSummaryPageAsync(page, candidate, cancellationToken);
            if (read.ErrorCode is not null) return new(null, read.ErrorCode);
            summaries.AddRange(read.Summaries.Select(text => (page, text)));
        }
        var subjects = new List<string>();
        foreach (var page in evidence.Pages.Where(page => page.Resource == ApiResource.BillSubjects))
        {
            var read = await ReadSubjectPageAsync(page, cancellationToken);
            if (read.ErrorCode is not null) return new(null, read.ErrorCode);
            subjects.AddRange(read.Subjects);
        }
        return new(Assemble(bill, detailPage, detailLines, sponsorships.ToImmutable(), summaries, [.. subjects], configuration.People), null);
    }

    private static ActionBillSnapshot Assemble(CongressBill bill, ActionDraftPage detailPage, ImmutableArray<DocumentLine> detailLines,
        ImmutableArray<ActionSponsorship> found, IReadOnlyList<(ActionDraftPage Page, CongressBillSummaryText Text)> summaries,
        ImmutableArray<string> subjects, PersonConfiguration[] configuredPeople)
    {
        var sponsorships = found.DistinctBy(Identity).OrderBy(item => item.BioguideId, StringComparer.Ordinal).ThenBy(item => item.Role).ToImmutableArray();
        var newest = BillSummarySelection.Newest(summaries.Select(item => item.Text));
        var newestPage = summaries.FirstOrDefault(item => item.Text == newest).Page;
        var involved = sponsorships.Select(item => item.PersonId).ToHashSet(StringComparer.Ordinal);
        return new(bill, sponsorships, ActionFingerprint.Compute(sponsorships), subjects, newest,
            newest is null ? null : BillSummarySelection.VersionOf(newest.Summary),
            newestPage is null ? null : ActionCitations.SummaryBlock(newestPage, newest!.Summary),
            bill.PolicyArea is null ? null : LineCitations.Line(detailPage.ExtractionId, detailLines, "Policy area: "),
            configuredPeople.Where(person => involved.Contains(person.Id)).ToImmutableDictionary(person => person.Id, person => person.Name),
            [.. configuredPeople.Where(person => !involved.Contains(person.Id)).Select(person => person.Name)],
            ActionCitations.TitleAndIntroduced(detailPage.ExtractionId, detailLines));
    }

    private static (string, ActionSponsorshipRole, bool, DateOnly, DateOnly?) Identity(ActionSponsorship item) =>
        (item.BioguideId, item.Role, item.IsOriginal, item.SponsoredOn, item.WithdrawnOn);

    private static IEnumerable<ActionSponsorship> SponsorsOf(CongressBill bill, PersonConfiguration[] people, ActionDraftPage detailPage,
        ImmutableArray<DocumentLine> detailLines)
    {
        foreach (var person in people.Where(person => bill.SponsorBioguideIds.Contains(person.BioguideId!)))
            yield return new ActionSponsorship(person.Id, person.BioguideId!, ActionSponsorshipRole.Sponsor, false, bill.IntroducedDate, null,
                [LineCitations.Line(detailPage.ExtractionId, detailLines, $"Sponsor: {person.BioguideId}")!, LineCitations.Line(detailPage.ExtractionId, detailLines, "Introduced: ")!]);
    }

    private async Task<(string? ErrorCode, IEnumerable<ActionSponsorship> Sponsorships)> ReadCosponsorPageAsync(ActionDraftPage page,
        ActionDraftCandidate candidate, PersonConfiguration[] people, CancellationToken cancellationToken)
    {
        var (job, response) = await LoadAsync(page, cancellationToken);
        var source = SourceOf(job, candidate);
        var bill = PageBillOf(source, ApiResource.BillCosponsors, job);
        var parsed = CongressBillCosponsorsParser.Parse(response.Body, response.Request, source.SourceUrl, bill);
        if (TextError(page, CongressBillRenderer.RenderCosponsors(bill, parsed)) is { } error) return (error, []);
        var citations = LineCitations.ExactLines(page.ExtractionId, page.Text);
        var found = parsed.Cosponsors.Join(people, cosponsor => cosponsor.BioguideId, person => person.BioguideId,
            (cosponsor, person) => new ActionSponsorship(person.Id, person.BioguideId!, ActionSponsorshipRole.Cosponsor,
                cosponsor.IsOriginalCosponsor, cosponsor.SponsorshipDate, cosponsor.SponsorshipWithdrawnDate,
                [citations.GetValueOrDefault(CongressBillRenderer.CosponsorLine(cosponsor))!]));
        return (null, found.ToList());
    }

    private async Task<(string? ErrorCode, IReadOnlyList<CongressBillSummaryText> Summaries)> ReadSummaryPageAsync(ActionDraftPage page,
        ActionDraftCandidate candidate, CancellationToken cancellationToken)
    {
        var (job, response) = await LoadAsync(page, cancellationToken);
        var source = SourceOf(job, candidate);
        var bill = PageBillOf(source, ApiResource.BillSummaries, job);
        var parsed = CongressBillSummariesParser.Parse(response.Body, response.Request, source.SourceUrl, bill);
        var texts = new List<CongressBillSummaryText>();
        foreach (var summary in parsed.Summaries)
            texts.Add(new CongressBillSummaryText(summary, await textExtractor.ExtractHtmlFragmentAsync(summary.Html, cancellationToken)));
        return (TextError(page, CongressBillRenderer.RenderSummaries(bill, texts)), texts);
    }

    private async Task<(string? ErrorCode, IReadOnlyList<string> Subjects)> ReadSubjectPageAsync(ActionDraftPage page,
        CancellationToken cancellationToken)
    {
        var (job, response) = await LoadAsync(page, cancellationToken);
        var (bill, parsed) = CongressBillSubjectsJob.Parse(job, response);
        return (TextError(page, CongressBillRenderer.RenderSubjects(bill, parsed)), parsed.Subjects);
    }

    private async Task<(CollectionJobRecord Job, ApiResponse Response)> LoadAsync(ActionDraftPage page, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(page.JobId, cancellationToken) ?? throw new InvalidDataException("A bill page job is missing.");
        return (job, await responses.ReadAsync(job, artifactRoot, cancellationToken));
    }

    private static ConfiguredCollectionSource SourceOf(CollectionJobRecord job, ActionDraftCandidate candidate) =>
        new(job.Definition.ConfigurationRevision!, candidate.SourceId, job.Definition.CoverageAsOf);

    private static CongressBillListItem PageBillOf(ConfiguredCollectionSource source, ApiResource resource, CollectionJobRecord job) =>
        BillPageUrl.TryRead(source.SourceUrl, resource, job.Definition.Url)?.Bill
        ?? throw new InvalidDataException("Bill page job URL is not a page of its bill resource.");

    private bool HasCurrentVersion(ActionDraftPage page) =>
        page.ExtractionVersion == page.Resource switch
        {
            ApiResource.Bill => CongressBillParser.ExtractionVersion,
            ApiResource.BillCosponsors => CongressBillCosponsorsParser.ExtractionVersion,
            ApiResource.BillSubjects => CongressBillSubjectsParser.ExtractionVersion,
            _ => CongressBillSummariesParser.ExtractionVersion(textExtractor)
        };

    private static string? TextError(ActionDraftPage page, string rendered) =>
        string.Equals(rendered, page.Text, StringComparison.Ordinal) ? null : ActionDraftErrorCodes.RenderedTextMismatch;
}
