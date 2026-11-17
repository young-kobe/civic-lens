using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Records;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Npgsql;

namespace CivicLens.PublicApi.Statements;

/// <summary>Reads the newest relationships from one statement to votes and their saved, cited-revision evidence in one bounded query.</summary>
public sealed class StatementLinkedVoteReader
{
    private const string Order = "r.cited_vote_date DESC, r.target_record_id";
    private const string After = "r.cited_vote_date < @cursor_date OR (r.cited_vote_date = @cursor_date AND r.target_record_id > @cursor_id)";

    private static string Sql(StatementLinkedVoteRequest request) => $"""
        SELECT a.release_number, r.record_id, r.revision_number, r.link_type, r.reason,
               r.quote_start, r.quote_length, r.quote_source_text, r.fact, r.fact_label, r.fact_text_origin,
               r.statement_revision_number, r.target_record_id, r.target_revision_number, r.target_headline, r.target_text_origin, r.target_label,
               r.linked_official_id, r.cited_vote_cast, r.cited_vote_date, r.text_origin, r.label
          FROM (SELECT release_number FROM public_api.active_release LIMIT 1) a
          LEFT JOIN LATERAL (
               SELECT r.* FROM public_api.relationships r
                WHERE r.statement_record_id = @statement_id AND r.target_kind = 'vote'{Cursor(request)}
                ORDER BY {KeysetPage.Order(Order, request.Backward)} LIMIT @take
          ) r ON TRUE
         ORDER BY {KeysetPage.Order(Order, request.Backward)}
        """;

    public async Task<StatementLinkedVotePage?> ReadAsync(NpgsqlTransaction transaction, string statementRecordId,
        StatementLinkedVoteRequest request, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(Sql(request), transaction.Connection, transaction);
        command.Parameters.AddWithValue("statement_id", statementRecordId);
        command.Parameters.AddWithValue("take", request.Limit + 1);
        if (request.Cursor is { } cursor)
        {
            command.Parameters.AddWithValue("cursor_date", cursor.VotedOn);
            command.Parameters.AddWithValue("cursor_id", cursor.RecordId);
        }

        var rows = new List<StatementLinkedVoteRow>();
        int? release = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                release = reader.GetInt32(0);
                if (!reader.IsDBNull(1)) rows.Add(ReadRow(reader));
            }
        if (release is not { } active) return null;
        var (votes, next, previous) = KeysetPage.Cut(rows, request.Limit, request.Cursor?.Encode(), request.Backward,
            row => StatementLinkedVoteCursor.Of(active, row).Encode());
        return new(votes.Select(row => row.Data).ToArray(), next, previous);
    }

    private static StatementLinkedVoteRow ReadRow(NpgsqlDataReader reader)
    {
        var text = Required(reader, 7);
        var quote = PublishedDocumentVersion.QuoteAt(text, reader.GetInt32(5), reader.GetInt32(6))
            ?? throw new InvalidDataException("The relationship quote does not match its offsets.");
        if (reader.IsDBNull(19)) throw new InvalidDataException("The cited vote revision has no vote date.");
        var row = new StatementLinkedVoteData(
            reader.GetString(1), reader.GetInt32(2), ViewColumns.Enum<PublishedRelationshipType>(reader, 3), reader.GetInt32(11), Required(reader, 4),
            ViewColumns.Enum<PublishedTextOrigin>(reader, 20), ViewColumns.Text(reader, 21), quote,
            Required(reader, 8), ViewColumns.Text(reader, 9), ViewColumns.NullableEnum<PublishedTextOrigin>(reader, 10),
            reader.GetString(12), reader.GetInt32(13), Required(reader, 14), ViewColumns.Enum<PublishedTextOrigin>(reader, 15),
            ViewColumns.Text(reader, 16), reader.GetString(17), null, ViewColumns.Text(reader, 18), reader.GetFieldValue<DateOnly>(19));
        if (row.RelationshipRevisionNumber < 1 || row.CitedVoteRevisionNumber < 1 || row.Quote.Length > PublishedRelationship.MaximumQuoteLength ||
            row.CitedStatementRevisionNumber < 1 || row.Reason.Length > PublishedRelationship.MaximumReasonLength || row.Fact.Length > PublishedRelationship.MaximumFactLength)
            throw new InvalidDataException("Published linked-vote evidence exceeds its limits.");
        return new(row.VotedOn, row.VoteRecordId, row);
    }

    private static string Required(NpgsqlDataReader reader, int ordinal) => ViewColumns.Text(reader, ordinal) is { } text && !string.IsNullOrWhiteSpace(text)
        ? text
        : throw new InvalidDataException("The relationship view has no text for a required linked-vote field.");

    private static string Cursor(StatementLinkedVoteRequest request) => request.Cursor is null
        ? ""
        : KeysetPage.Window(After, "r.target_record_id", "@cursor_id", request.Backward);
}

internal sealed record StatementLinkedVoteRow(DateOnly VotedOn, string VoteRecordId, StatementLinkedVoteData Data);
