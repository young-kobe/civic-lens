using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Application.Issues;
using CivicLens.Core.Actions;

namespace CivicLens.Application.Actions;

/// <summary>The shared parts of the action prompts: the bill facts and the run context.</summary>
public static class ActionDraftPrompt
{
    public static string SerializeContext(ActionBillSnapshot snapshot, BillIssueChoice issues, string issueRevisionId) =>
        JsonSerializer.Serialize(new
        {
            officials = snapshot.Sponsorships.Select(item => new { id = item.PersonId, name = snapshot.OfficialNames[item.PersonId] }).DistinctBy(item => item.id),
            issueCandidateIds = issues.CandidateIds,
            issueRevisionId,
            fingerprint = snapshot.Fingerprint,
            summaryVersion = snapshot.SummaryVersion
        }, AnalysisPromptText.CompactJson);

    /// <summary>The bill facts of the issue prompt and the text prompt. Neither sends the CRS summary text: the page shows that text itself.</summary>
    internal static string UserPrompt(ActionBillSnapshot snapshot, BillIssueChoice issues,
        ImmutableArray<string> previousErrors, ActionDraftBillText? billText, BillDefinitionsSection? definitions = null)
    {
        var bill = snapshot.Bill;
        var document = new
        {
            bill = new
            {
                congress = bill.Congress,
                type = bill.Type,
                number = bill.Number,
                title = bill.Title,
                introducedOn = Date(bill.IntroducedDate),
                policyArea = bill.PolicyArea
            },
            candidateIssues = issues.PromptSection(),
            subjects = issues.SubjectsSection(),
            sponsors = snapshot.Sponsorships.Where(item => item.Role == ActionSponsorshipRole.Sponsor)
                .Select(item => snapshot.OfficialNames[item.PersonId]),
            billText = billText is null ? null : BillTextSection(billText, definitions)
        };
        var builder = new StringBuilder("Facts:\n").Append(JsonSerializer.Serialize(document, AnalysisPromptText.EvidenceJson));
        return AnalysisPromptText.WithRejectionReasons(builder, previousErrors);
    }

    /// <summary>The text is the normalized body. Both counts use that normalized text: the characters read are the length of the text that is
    /// sent, and the total is the stored total less what the normalization removed from the part that is sent. A whole text then reads as whole.</summary>
    private static object BillTextSection(ActionDraftBillText billText, BillDefinitionsSection? definitions)
    {
        var text = GpoQuoteMarks.Normalize(billText.Body);
        var removedByNormalizing = billText.Body.Length - text.Length;
        return new
        {
            version = billText.VersionType,
            versionDate = billText.VersionDate is { } date ? Date(date) : null,
            charactersRead = text.Length,
            charactersTotal = billText.CharsTotal - removedByNormalizing,
            definitionsSection = definitions is null ? null : new { heading = definitions.Heading, endsBefore = definitions.NextHeading },
            text
        };
    }

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
