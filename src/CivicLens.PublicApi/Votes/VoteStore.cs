using System.Data.Common;
using System.Text.Json;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Npgsql;
using NpgsqlTypes;

namespace CivicLens.PublicApi.Votes;

/// <summary>Reads the views <c>public_api.votes</c>, <c>public_api.vote_issues</c>, and <c>public_api.vote_members</c>. Every answer takes its release from the query that reads its rows.</summary>
public sealed class VoteStore(NpgsqlDataSource views)
{
    private const string Columns = """
        v.record_id, v.revision_number, v.chamber, v.congress, v.session_number, v.roll_call_number, v.voted_on, v.kind, v.bill_type, v.bill_number,
        v.headline, v.summary, v.text_origin, v.label, v.question, v.result, v.yea, v.nay, v.present, v.not_voting, v.alignment, v.bill_record_id
        """;
    internal const string Order = "v.voted_on DESC, v.chamber, v.session_number DESC, v.roll_call_number DESC, v.record_id";
    // The tuple comparison cannot mix directions, so the chain follows the ORDER BY key by key.
    private const string AfterCursor = """
         AND (v.voted_on < @cursor_date
            OR (v.voted_on = @cursor_date AND v.chamber > @cursor_chamber)
            OR (v.voted_on = @cursor_date AND v.chamber = @cursor_chamber AND v.session_number < @cursor_session)
            OR (v.voted_on = @cursor_date AND v.chamber = @cursor_chamber AND v.session_number = @cursor_session AND v.roll_call_number < @cursor_roll_call)
            OR (v.voted_on = @cursor_date AND v.chamber = @cursor_chamber AND v.session_number = @cursor_session AND v.roll_call_number = @cursor_roll_call
                AND v.record_id > @cursor_id))
        """;

    /// <summary>Reads one page. Null means that no release is active. The caller compares the release of the page with the release of the cursor.</summary>
    public async Task<VotePage?> ListAsync(VoteRequest request, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(ListSql(request));
        AddParameters(command, request);
        var rows = new List<VoteRow>();
        int? release = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                release = reader.GetInt32(0);
                if (!reader.IsDBNull(1)) rows.Add(ReadRow(reader));
            }
        if (release is not { } active) return null;
        var page = rows.Take(request.Limit).ToArray();
        var next = rows.Count > request.Limit ? VoteCursor.Of(active, page[^1]).Encode() : null;
        return new(active, page, next);
    }

    private static readonly string DetailSql = $"""
            SELECT v.release_number, {Columns},
                   ARRAY(SELECT i.issue_id FROM public_api.vote_issues i
                          WHERE i.record_id = v.record_id AND i.revision_number = v.revision_number ORDER BY i.issue_id),
                   (SELECT json_agg(json_build_object('position', f."position", 'text', f.fact_text, 'id', f.extraction_id,
                            'start', f.citation_start, 'length', f.citation_length) ORDER BY f."position")
                      FROM (SELECT f.* FROM public_api.vote_facts f
                             WHERE f.record_id = v.record_id AND f.revision_number = v.revision_number
                             ORDER BY f."position" LIMIT {PublishedVote.MaximumFacts + 1}) f),
                   e.vote_source, e.voted_on_citation_extraction_id, e.voted_on_citation_start, e.voted_on_citation_length,
                   e.kind_citation_extraction_id, e.kind_citation_start, e.kind_citation_length,
                   (SELECT json_agg(json_build_object('id', s.extraction_id, 'url', s.url,
                            'observedAt', s.observed_at_utc_ticks,
                            'text', CASE WHEN length(s.text) <= {PublishedDocumentVersion.MaximumTextLength} THEN s.text END)
                            ORDER BY s.extraction_id)
                      FROM (SELECT s.* FROM public_api.vote_sources s
                             WHERE s.record_id = v.record_id AND s.revision_number = v.revision_number
                             ORDER BY s.extraction_id LIMIT {PublishedVote.MaximumSources + 1}) s),
                   (SELECT json_agg(json_build_object('position', m."position", 'cast', m.vote_cast, 'id', m.extraction_id,
                            'start', m.citation_start, 'length', m.citation_length) ORDER BY m."position")
                      FROM (SELECT m.* FROM public_api.vote_members m
                             WHERE m.record_id = v.record_id AND m.revision_number = v.revision_number
                             ORDER BY m."position" LIMIT {PublishedVote.MaximumMembers + 1}) m),
                   (SELECT t.party_tally_count FROM public_api.vote_party_tallies t
                     WHERE t.record_id = v.record_id AND t.revision_number = v.revision_number LIMIT 1),
                   (SELECT json_agg(json_build_object('party', t.party, 'yea', t.yea, 'nay', t.nay,
                            'present', t.present, 'notVoting', t.not_voting, 'other', t.other) ORDER BY t.party)
                      FROM (SELECT t.* FROM public_api.vote_party_tallies t
                             WHERE t.record_id = v.record_id AND t.revision_number = v.revision_number AND t.party IS NOT NULL
                             ORDER BY t.party LIMIT {PublishedVote.MaximumParties + 1}) t)
              FROM public_api.votes v
              LEFT JOIN public_api.vote_evidence e ON e.record_id = v.record_id AND e.revision_number = v.revision_number
             WHERE v.record_id = @id
            """;

    public Task<VoteDetail?> FindAsync(string recordId, CancellationToken cancellationToken) =>
        ReadDetailAsync(views.CreateCommand(DetailSql), recordId, cancellationToken);

    /// <summary>Reads the detail inside the transaction of the caller, so that it shares a snapshot with the other reads of that transaction.</summary>
    public Task<VoteDetail?> FindAsync(NpgsqlTransaction transaction, string recordId, CancellationToken cancellationToken) =>
        ReadDetailAsync(new NpgsqlCommand(DetailSql, transaction.Connection, transaction), recordId, cancellationToken);

    private static async Task<VoteDetail?> ReadDetailAsync(NpgsqlCommand command, string recordId, CancellationToken cancellationToken)
    {
        await using var _ = command;
        command.Parameters.AddWithValue("id", recordId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var release = reader.GetInt32(0);
        var vote = ReadRow(reader);
        var issueIds = reader.GetFieldValue<string[]>(23);
        var evidence = ReadDetailEvidence(reader, vote);
        var facts = evidence.Facts;
        var marker = reader.IsDBNull(34) ? (short?)null : reader.GetInt16(34);
        var partyTallies = ReadPartyTallies(reader);
        ValidatePartyTallies(partyTallies, marker);
        return new VoteDetail(release, vote, issueIds, facts, evidence.Source, evidence.Sources, evidence.VotedOnCitation, evidence.KindCitation)
        {
            PartyTallies = partyTallies.Count == 0 ? null : partyTallies.ToArray()
        };
    }

    private static List<PublishedPartyTally> ReadPartyTallies(DbDataReader reader)
    {
        if (reader.IsDBNull(35)) return [];
        var rows = ReadJson<TallyRow[]>(reader, 35, "party tallies");
        if (rows.Length > PublishedVote.MaximumParties || rows.Any(row => row is null)) throw InvalidPartyTally();
        var tallies = rows.Select(row =>
        {
            if (row.Yea is null || row.Nay is null || row.Present is null || row.NotVoting is null || row.Other is null)
                throw InvalidPartyTally();
            var tally = new PublishedPartyTally
            {
                Party = row.Party,
                Yea = row.Yea.Value,
                Nay = row.Nay.Value,
                Present = row.Present.Value,
                NotVoting = row.NotVoting.Value,
                Other = row.Other.Value
            };
            tally.Validate();
            return tally;
        }).ToList();
        return tallies;
    }

    private static InvalidDataException InvalidPartyTally() => new("Stored vote party tally is incomplete.");

    private static void ValidatePartyTallies(List<PublishedPartyTally> tallies, short? marker)
    {
        if (marker is null or 0)
        {
            if (tallies.Count != 0) throw new InvalidDataException("Stored vote party tallies do not match their completeness marker.");
            return;
        }
        if (marker is < 1 or > PublishedVote.MaximumParties || tallies.Count != marker ||
            tallies.Select(tally => tally.Party).Distinct(StringComparer.Ordinal).Count() != tallies.Count)
            throw new InvalidDataException("Stored vote party tallies do not match their completeness marker.");
    }

    private static DetailEvidence ReadDetailEvidence(DbDataReader reader, VoteRow vote)
    {
        if (reader.IsDBNull(25)) throw new InvalidDataException("Published vote evidence is missing.");
        var sources = ReadSources(reader, 32, "sources");
        var votedOnCitation = ReadCitation(sources, ViewColumns.Text(reader, 26), ViewColumns.Number(reader, 27), ViewColumns.Number(reader, 28));
        var kindCitation = NullableCitation(sources, reader, 29, 30, 31);
        var memberRows = ReadJson<MemberEvidenceRow[]>(reader, 33, "member citations");
        if (memberRows.Length > PublishedVote.MaximumMembers || memberRows.Any(row => row is null) ||
            !memberRows.Select(row => row.Position).SequenceEqual(Enumerable.Range(0, memberRows.Length)))
            throw new InvalidDataException("Published vote member citations are incomplete, misordered, or exceed their limit.");
        var memberEvidence = memberRows.Select(row => (Cast: (string?)row.Cast, Citation: (PublishedCitation?)ReadCitation(sources, row.Id, row.Start, row.Length))).ToArray();
        var facts = ReadFacts(reader, sources);
        PublishedVote.ValidateEvidence(sources, vote.VotedOn, vote.Kind, kindCitation, votedOnCitation, vote.Result, facts, memberEvidence);
        return new(ViewColumns.Enum<PublishedVoteSource>(reader, 25), sources, votedOnCitation, kindCitation, facts);
    }

    private static PublishedVoteFact[] ReadFacts(DbDataReader reader, PublishedDocumentVersion[] sources)
    {
        var rows = ReadJson<FactRow[]>(reader, 24, "facts");
        if (rows.Length > PublishedVote.MaximumFacts || rows.Any(row => row is null) ||
            !rows.Select(row => row.Position).SequenceEqual(Enumerable.Range(0, rows.Length)))
            throw new InvalidDataException("Published vote facts are incomplete, misordered, or exceed their limit.");
        return rows.Select(row => new PublishedVoteFact
        {
            Text = row.Text,
            Citation = ReadCitation(sources, row.Id, row.Start, row.Length)
        }).ToArray();
    }

    private static PublishedDocumentVersion[] ReadSources(DbDataReader reader, int ordinal, string noun) =>
        ReadSources(ReadJson<SourceRow[]>(reader, ordinal, noun));

    private static PublishedDocumentVersion[] ReadSources(string json, string noun) =>
        ReadSources(DeserializeJson<SourceRow[]>(json, noun));

    private static PublishedDocumentVersion[] ReadSources(SourceRow[] rows)
    {
        if (rows.Length > PublishedVote.MaximumSources || rows.Any(row => row is null))
            throw new InvalidDataException($"A published vote has more than {PublishedVote.MaximumSources} sources.");
        return rows.Select(row =>
        {
            if (row.Id is null || row.Url is null || row.ObservedAt is null || row.Text is null || row.Text.Length > PublishedDocumentVersion.MaximumTextLength)
                throw new InvalidDataException("Published vote source metadata or bounded text is missing.");
            return new PublishedDocumentVersion
            {
                ExtractionId = row.Id,
                Url = row.Url,
                ObservedAtUtc = ObservedAtUtc(row.ObservedAt.Value),
                Text = row.Text
            };
        }).ToArray();
    }

    private static PublishedCitation? NullableCitation(PublishedDocumentVersion[] sources, DbDataReader reader, int id, int start, int length)
    {
        if (reader.IsDBNull(id) && reader.IsDBNull(start) && reader.IsDBNull(length)) return null;
        return ReadCitation(sources, ViewColumns.Text(reader, id), ViewColumns.Number(reader, start), ViewColumns.Number(reader, length));
    }

    private static PublishedCitation ReadCitation(PublishedDocumentVersion[] sources, string? extractionId, int? start, int? length)
    {
        if (extractionId is null || start is null || length is null)
            throw new InvalidDataException("Published vote citation is incomplete.");
        var source = sources.FirstOrDefault(item => item.ExtractionId == extractionId)
            ?? throw new InvalidDataException("Published citation must cite one of the vote's sources.");
        return new PublishedCitation
        {
            ExtractionId = extractionId,
            Start = start.Value,
            Length = length.Value,
            Quote = QuoteAt(source.Text, start.Value, length.Value)
        };
    }

    private static string QuoteAt(string text, int start, int length) =>
        start >= 0 && length > 0 && start <= text.Length - length
            ? text.Substring(start, length)
            : throw new InvalidDataException("Published citation quote does not match its offsets.");

    private static T ReadJson<T>(DbDataReader reader, int ordinal, string noun) => reader.IsDBNull(ordinal)
        ? throw new InvalidDataException($"The vote {noun} view returned no content.")
        : DeserializeJson<T>(reader.GetString(ordinal), noun);

    private static T DeserializeJson<T>(string json, string noun)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, PublicationProtocol.JsonOptions)
                ?? throw new InvalidDataException($"The vote {noun} view returned invalid content.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The vote {noun} view returned invalid content.", exception);
        }
    }

    private static DateTimeOffset ObservedAtUtc(long ticks)
    {
        try { return new(ticks, TimeSpan.Zero); }
        catch (ArgumentOutOfRangeException exception)
        { throw new InvalidDataException("Published source observation time is invalid.", exception); }
    }

    private sealed record FactRow(int Position, string Text, string Id, int Start, int Length);
    private sealed record SourceRow(string? Id, string? Url, long? ObservedAt, string? Text);
    private sealed record MemberEvidenceRow(int Position, string Cast, string Id, int Start, int Length);
    private sealed record RawVoteMember(int Position, string OfficialId, string Cast, string? Party, string? Caucus, string? Id, int? Start, int? Length);
    private sealed record TallyRow(string Party, int? Yea, int? Nay, int? Present, int? NotVoting, int? Other);
    private sealed record DetailEvidence(PublishedVoteSource Source, PublishedDocumentVersion[] Sources, PublishedCitation VotedOnCitation,
        PublishedCitation? KindCitation, PublishedVoteFact[] Facts);

    /// <summary>Reads one page of members. Null means that the active release holds no such vote. The caller compares the release of the page with the release of the cursor.</summary>
    public Task<VoteMemberPage?> ListMembersAsync(string recordId, VoteMemberRequest request, CancellationToken cancellationToken) =>
        ReadMembersAsync(views.CreateCommand(MemberSql(request, true)), recordId, request, null, cancellationToken);

    /// <summary>Reads one page of members inside the transaction of the caller. The sources are the ones that the detail of the same transaction already read, so no source text is read again.</summary>
    public Task<VoteMemberPage?> ListMembersAsync(NpgsqlTransaction transaction, string recordId, VoteMemberRequest request,
        PublishedDocumentVersion[] knownSources, CancellationToken cancellationToken) =>
        ReadMembersAsync(new NpgsqlCommand(MemberSql(request, false), transaction.Connection, transaction), recordId, request, knownSources, cancellationToken);

    private static string MemberSql(VoteMemberRequest request, bool withSources) => $"""
        WITH active AS (
            SELECT x.release_number, x.revision_number FROM public_api.active_records x
             WHERE x.kind = 'vote' AND x.record_id = @id
               AND EXISTS (SELECT 1 FROM public_api.vote_members h WHERE h.record_id = x.record_id AND h.revision_number = x.revision_number)
        ), selected AS MATERIALIZED (
            SELECT m.* FROM public_api.vote_members m
              JOIN active a ON a.revision_number = m.revision_number
             WHERE m.record_id = @id{MemberFilters(request)}
             ORDER BY m."position" LIMIT @take
        )
        SELECT a.release_number,
               (SELECT json_agg(json_build_object('position', m."position", 'officialId', m.official_id, 'cast', m.vote_cast,
                        'party', m.party, 'caucus', m.caucus, 'id', m.extraction_id,
                        'start', m.citation_start, 'length', m.citation_length) ORDER BY m."position")
                  FROM selected m),
               {(withSources ? MemberSourcesSql : "NULL::json")}
          FROM active a
        """;

    private static readonly string MemberSourcesSql = $"""
        (SELECT json_agg(json_build_object('id', s.extraction_id, 'url', s.url,
                        'observedAt', s.observed_at_utc_ticks,
                        'text', CASE WHEN length(s.text) <= {PublishedDocumentVersion.MaximumTextLength} THEN s.text END)
                        ORDER BY s.extraction_id)
                  FROM (SELECT s.* FROM public_api.vote_sources s
                         WHERE s.record_id = @id AND s.revision_number = a.revision_number
                           AND s.extraction_id IN (SELECT extraction_id FROM selected)
                         ORDER BY s.extraction_id LIMIT {PublishedVote.MaximumSources + 1}) s)
        """;

    private static async Task<VoteMemberPage?> ReadMembersAsync(NpgsqlCommand command, string recordId, VoteMemberRequest request,
        PublishedDocumentVersion[]? knownSources, CancellationToken cancellationToken)
    {
        await using var _ = command;
        command.Parameters.AddWithValue("id", recordId);
        command.Parameters.AddWithValue("take", request.Limit + 1);
        if (request.Cast is not null) command.Parameters.AddWithValue("cast", request.Cast);
        if (request.Officials.Length > 0) command.Parameters.AddWithValue("officials", request.Officials);
        if (request.Cursor is { } cursor) command.Parameters.AddWithValue("after", cursor.Position);
        int? release = null;
        RawVoteMember[] rows = [];
        string? sourceJson = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            if (await reader.ReadAsync(cancellationToken))
            {
                release = reader.GetInt32(0);
                if (!reader.IsDBNull(1)) rows = ReadJson<RawVoteMember[]>(reader, 1, "member rows");
                if (!reader.IsDBNull(2)) sourceJson = reader.GetString(2);
            }
        if (release is not { } active) return null;
        var sources = knownSources ?? (sourceJson is null ? [] : ReadSources(sourceJson, "member vote sources"));
        var evidenceRows = rows.Select(row => (Cast: (string?)row.Cast, Citation: (PublishedCitation?)ReadCitation(sources, row.Id, row.Start, row.Length))).ToArray();
        if (evidenceRows.Length > 0 && knownSources is null) PublishedVote.ValidateMemberEvidence(sources, evidenceRows);
        else if (evidenceRows.Length > 0) PublishedVote.ValidateMemberCitations(sources, evidenceRows);
        var members = rows.Select((row, index) => new VoteMember(row.Position, row.OfficialId, row.Cast, row.Party, row.Caucus, evidenceRows[index].Citation!)).ToArray();
        var page = members.Take(request.Limit).ToArray();
        var next = rows.Length > request.Limit ? new VoteMemberCursor(active, page[^1].Position).Encode() : null;
        return new(active, page, next);
    }

    private static string MemberFilters(VoteMemberRequest request) =>
        (request.Cast is null ? "" : " AND m.vote_cast = @cast") +
        (request.Officials.Length == 0 ? "" : " AND m.official_id = ANY(@officials)") +
        (request.Cursor is null ? "" : " AND m.\"position\" > @after");

    private static string ListSql(VoteRequest request) => $"""
        SELECT a.release_number, {Columns}
          FROM (SELECT release_number FROM public_api.active_release LIMIT 1) a
          LEFT JOIN LATERAL (
               SELECT v.* FROM public_api.votes v
                WHERE TRUE{Filters(request)}{(request.Cursor is null ? "" : AfterCursor)}
                ORDER BY {Order} LIMIT @take) v
            ON TRUE
         ORDER BY {Order}
        """;

    private static string Filters(VoteRequest request) =>
        (request.Chamber is null ? "" : " AND v.chamber = @chamber") +
        (request.Congress is null ? "" : " AND v.congress = @congress") +
        (request.Session is null ? "" : " AND v.session_number = @session") +
        (request.Kind is null ? "" : " AND v.kind = @kind") +
        (request.BillType is null ? "" : " AND v.bill_type = @bill_type") +
        (request.BillNumber is null ? "" : " AND v.bill_number = @bill_number") +
        (request.Issue is null ? "" : """
             AND EXISTS (SELECT 1 FROM public_api.vote_issues i
                          WHERE i.record_id = v.record_id AND i.revision_number = v.revision_number AND i.issue_id = @issue)
            """) +
        (request.From is null ? "" : " AND v.voted_on >= @from") +
        (request.To is null ? "" : " AND v.voted_on <= @to");

    private static void AddParameters(NpgsqlCommand command, VoteRequest request)
    {
        var parameters = command.Parameters;
        parameters.AddWithValue("take", request.Limit + 1);
        if (request.Chamber is { } chamber) parameters.AddWithValue("chamber", chamber.ToString());
        if (request.Congress is { } congress) parameters.AddWithValue("congress", congress);
        if (request.Session is { } session) parameters.AddWithValue("session", session);
        if (request.Kind is { } kind) parameters.AddWithValue("kind", kind.ToString());
        if (request.BillType is not null) parameters.AddWithValue("bill_type", request.BillType);
        if (request.BillNumber is { } billNumber) parameters.AddWithValue("bill_number", billNumber);
        if (request.Issue is not null) parameters.AddWithValue("issue", request.Issue);
        if (request.From is { } from) parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.Date) { Value = from });
        if (request.To is { } to) parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.Date) { Value = to });
        if (request.Cursor is not { } cursor) return;
        parameters.Add(new NpgsqlParameter("cursor_date", NpgsqlDbType.Date) { Value = cursor.VotedOn });
        parameters.AddWithValue("cursor_chamber", cursor.Chamber.ToString());
        parameters.AddWithValue("cursor_session", cursor.Session);
        parameters.AddWithValue("cursor_roll_call", cursor.RollCall);
        parameters.AddWithValue("cursor_id", cursor.RecordId);
    }

    // Column 0 holds the release. The row starts at column 1.
    private static VoteRow ReadRow(DbDataReader reader) => new()
    {
        RecordId = reader.GetString(1),
        RevisionNumber = reader.GetInt32(2),
        Chamber = ViewColumns.Enum<PublishedChamber>(reader, 3),
        Congress = reader.GetInt32(4),
        Session = reader.GetInt32(5),
        RollCall = reader.GetInt32(6),
        VotedOn = reader.GetFieldValue<DateOnly>(7),
        Kind = ViewColumns.Enum<PublishedVoteKind>(reader, 8),
        BillType = ViewColumns.Text(reader, 9),
        BillNumber = ViewColumns.Number(reader, 10),
        Headline = reader.GetString(11),
        Summary = reader.GetString(12),
        TextOrigin = ViewColumns.Enum<PublishedTextOrigin>(reader, 13),
        Label = ViewColumns.Text(reader, 14),
        Question = ViewColumns.Text(reader, 15),
        Result = reader.GetString(16),
        Yea = ViewColumns.Number(reader, 17),
        Nay = ViewColumns.Number(reader, 18),
        Present = ViewColumns.Number(reader, 19),
        NotVoting = ViewColumns.Number(reader, 20),
        Alignment = reader.IsDBNull(21) ? null : ViewColumns.Enum<PublishedVoteAlignment>(reader, 21),
        BillRecordId = ViewColumns.Text(reader, 22)
    };
}
