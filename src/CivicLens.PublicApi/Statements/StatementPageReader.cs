using CivicLens.PublicApi.Records;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Statements;

/// <summary>Reads everything that the server-rendered statement page shows: the statement detail with the term of each speaker, the names of its speakers and issues, its linked votes, and its corrections. One snapshot holds every read, so the page comes from one release.
/// The seat words are the words of the officials list, from <see cref="OfficialSeat"/>.</summary>
public sealed class StatementPageReader(StatementStore statements, CorrectionHistoryStore corrections, RecordSnapshot snapshot, StatementLinkedVoteReader linkedVotes)
{
    /// <summary>Reads the page of one statement. Null means the active release holds no such statement.</summary>
    public async Task<StatementPageData?> ReadAsync(string recordId, StatementLinkedVoteRequest linkedVoteRequest, CancellationToken cancellationToken)
    {
        var (connection, transaction) = await snapshot.OpenAsync(cancellationToken);
        await using var _ = connection;
        await using var __ = transaction;
        if ((await statements.FindAsync(transaction, recordId, cancellationToken)).Detail is not { } detail) return null;
        var row = detail.Statement;
        var linkedVotePage = await linkedVotes.ReadAsync(transaction, recordId, linkedVoteRequest, cancellationToken)
            ?? throw new InvalidDataException("The active statement has no active release for its linked votes.");
        var (names, issues) = await snapshot.ReadNamesAsync(transaction, row.SpeakerIds, row.IssueIds, cancellationToken);
        var corrected = await corrections.ListAsync(transaction, PublishedRecordKind.Statement, recordId, row.RevisionNumber, cancellationToken);
        var speakerNames = names.ToDictionary(speaker => speaker.OfficialId, speaker => speaker.Name, StringComparer.Ordinal);
        var missingLinkedNames = linkedVotePage.Votes.Select(vote => vote.LinkedOfficialId)
            .Where(id => !speakerNames.ContainsKey(id)).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var (officialId, name) in await snapshot.ReadOptionalOfficialNamesAsync(transaction, missingLinkedNames, cancellationToken))
            speakerNames[officialId] = name;
        linkedVotePage = linkedVotePage with
        {
            Votes = [.. linkedVotePage.Votes.Select(vote => vote with
            {
                LinkedOfficialName = speakerNames.GetValueOrDefault(vote.LinkedOfficialId)
            })]
        };
        var speakers = names.Zip(detail.SpeakerTerms, (name, term) => name with
        {
            Seat = OfficialSeat.Text(term.Kind, term.Office, term.State, term.District),
            Party = term.Party
        });
        return new(detail.Release, row.RecordId, row.RevisionNumber, row.StatementDate, row.Headline, row.Summary, row.TextOrigin, row.Label, detail.ApprovedBy,
            [.. speakers], issues, detail.Source, detail.ArticleUrl, detail.StatementDateEvidence, detail.Quotations, detail.ApprovedAtUtc, detail.FirstPublishedAtUtc)
        {
            Corrections = corrected,
            LinkedVotes = linkedVotePage
        };
    }
}
