using CivicLens.PublicWeb.Publication;
using System.Data.Common;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Officials;
using Npgsql;
using NpgsqlTypes;

namespace CivicLens.PublicApi.Bills;

/// <summary>Reads the views <c>public_api.bills</c>, <c>public_api.bill_keys</c>, <c>public_api.bill_sponsorships</c>, <c>public_api.bill_issues</c>, <c>public_api.bill_key_terms</c>, <c>public_api.bill_text_sentences</c>, <c>public_api.officials</c>, <c>public_api.release_officials</c>, <c>public_api.release_issues</c>,
/// <c>public_api.record_entries</c> (the stored approval time), and <c>public_api.record_first_publications</c>. Every answer takes its release from the query that reads its rows.</summary>
public sealed class BillStore(NpgsqlDataSource views)
{
    /// <summary>One row of a list read and the latest action date of its key.</summary>
    private sealed record ListedBill(BillRow Bill, DateOnly? LatestActionDate);

    private const string Columns = """
        b.record_id, b.revision_number, b.congress, b.bill_type, b.bill_number, b.bill_title, b.headline, b.summary, b.text_origin, b.label,
        b.policy_area, b.policy_area_id, b.event_date, b.sponsor_count, b.cosponsor_count, b.bill_line,
        b.why_it_matters, b.text_version_name, b.text_version_date, b.text_chars_read, b.text_chars_total, b.public_law
        """;
    /// <summary>The sort of every bill list and of the bills of the issue page: the latest official action that the active release pinned, newest first, with bills of no stored status last.
    /// The one owner of that order. It reads <c>b.latest_action_date</c> and <c>b.record_id</c> of the keys or of the rows of the page.</summary>
    internal const string Order = "b.latest_action_date DESC NULLS LAST, b.record_id";

    // A list row holds the bill columns and then the latest action date of its key, which the cursor of the next page takes.
    private const int LatestActionColumn = 23;

    /// <summary>The name of the first sponsor of the bill <c>b</c>, in stored order, or null when the release names none. The one owner of that rule.</summary>
    internal const string SponsorNameSql = """
        (SELECT o.name FROM public_api.bill_sponsorships s
           JOIN public_api.officials o ON o.official_id = s.official_id
          WHERE s.record_id = b.record_id AND s.revision_number = b.revision_number AND s.role = 'Sponsor'
          ORDER BY s."position" LIMIT 1)
        """;

    /// <summary>Joins the stored status <c>t</c> of each bill <c>b</c> of a row set. The one owner of that join.
    /// Postgres underestimates the rows of a view on <c>active_records</c>, so a plain join rebuilds the whole view for each bill. OFFSET 0 keeps the lookup correlated.</summary>
    internal const string StatusSql = """
        LEFT JOIN LATERAL (
             SELECT t.* FROM public_api.bill_status t
              WHERE t.record_id = b.record_id AND t.revision_number = b.revision_number OFFSET 0) t
          ON TRUE
        """;

    /// <summary>Joins the bill <c>b</c> of each key <c>k</c> of a bounded page of keys from <c>public_api.bill_keys</c>. The one owner of that join.
    /// OFFSET 0 keeps the lookup correlated, so Postgres reads the wide bill columns only for the keys of the page.</summary>
    internal const string BillsOfKeysSql = """
        CROSS JOIN LATERAL (
             SELECT b.* FROM public_api.bills b
              WHERE b.record_id = k.record_id AND b.revision_number = k.revision_number OFFSET 0) b
        """;

    private const string KeyTermsSql = """
        LEFT JOIN LATERAL (
             SELECT array_agg(k.term ORDER BY k."position") AS names,
                    array_agg(k.definition ORDER BY k."position") AS definitions,
                    array_agg(k.cut ORDER BY k."position") AS cuts,
                    array_agg(k.quote ORDER BY k."position") AS quotes,
                    min(k.label) AS label
               FROM public_api.bill_key_terms k
              WHERE k.record_id = b.record_id AND k.revision_number = b.revision_number
                AND b.text_origin = 'Model') terms ON TRUE
        """;

    /// <summary>The FROM clause of the keys <c>b</c> of the bills of the issue <c>@issue</c>. The one owner of that join. The few issue rows drive the read, and OFFSET 0 keeps each key lookup correlated,
    /// because Postgres underestimates the rows of a view on <c>active_records</c> and a plain semi join reads the whole issue view once for each bill.</summary>
    internal static readonly string IssueKeysSql = KeysOf("SELECT i.record_id, i.revision_number FROM public_api.bill_issues i WHERE i.issue_id = @issue");

    private const string PinFactsSql = $"""
        SELECT b.record_id, {SponsorNameSql}, t.latest_action_text, t.latest_action_date, v.record_id, v.chamber
          FROM public_api.bills b
          {StatusSql}
          LEFT JOIN LATERAL (
               SELECT v.record_id, v.chamber FROM public_api.votes v
                WHERE v.congress = b.congress AND v.bill_type = b.bill_type AND v.bill_number = b.bill_number
                  AND v.chamber IN ('{nameof(PublishedChamber.House)}', '{nameof(PublishedChamber.Senate)}')
                ORDER BY v.chamber = '{nameof(PublishedChamber.House)}' DESC, v.voted_on DESC, v.session_number DESC, v.roll_call_number DESC LIMIT 1) v
            ON TRUE
         WHERE b.record_id = ANY(@ids)
        """;
    private const string ListFactsSql = $"""
        SELECT b.record_id, b.revision_number, {SponsorNameSql}, t.latest_action_text, t.latest_action_date, t.status,
               terms.names, terms.definitions, terms.cuts, terms.quotes, terms.label
          FROM unnest(@ids, @revisions) k(record_id, revision_number)
          {BillsOfKeysSql}
          {StatusSql}
          {KeyTermsSql}
        """;

    private const string PolicyAreasSql = """
        SELECT DISTINCT b.policy_area_id, b.policy_area FROM public_api.bills b
         WHERE b.congress = @congress AND b.policy_area_id IS NOT NULL AND b.policy_area IS NOT NULL ORDER BY b.policy_area, b.policy_area_id
        """;

    // The cursor tests follow the order: a bill with no stored status comes after every dated bill. StatementStore pages its undated statements the same way.
    private const string AfterDated = "b.latest_action_date < @cursor_date OR b.latest_action_date IS NULL OR (b.latest_action_date = @cursor_date AND b.record_id > @cursor_id)";
    private const string AfterNoAction = "b.latest_action_date IS NULL AND b.record_id > @cursor_id";

    /// <summary>Reads one page. Null means that no release is active. The caller compares the release of the page with the release of the cursor.</summary>
    public async Task<BillPage?> ListAsync(BillRequest request, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(ListSql(request));
        AddParameters(command, request);
        var rows = new List<ListedBill>();
        int? release = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                release = reader.GetInt32(0);
                if (!reader.IsDBNull(1)) rows.Add(ReadListed(reader));
            }
        return release is { } active ? PageOf(active, rows, request) : null;
    }

    /// <summary>Reads one page of the bills of the newest Congress that the active release holds and that Congress, in one query, inside the transaction of the caller.
    /// The Congress and its total come from the stored counts of the release, which <c>ReleaseBillCounts</c> owns, so no live count runs.
    /// Null means that no release is active. A release with no bill or with no stored count gives a null Congress and an empty page.</summary>
    public async Task<NewestCongressBills?> ListNewestCongressAsync(NpgsqlTransaction transaction, BillRequest request, CancellationToken cancellationToken)
    {
        if (request.Congress is not null) throw new ArgumentException("The newest Congress list takes its own Congress.", nameof(request));
        await using var command = new NpgsqlCommand(NewestCongressSql(request), transaction.Connection, transaction);
        AddParameters(command, request);
        var rows = new List<ListedBill>();
        int? release = null, congress = null;
        var total = 0;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                release = reader.GetInt32(0);
                congress = ViewColumns.Number(reader, reader.FieldCount - 2);
                total = ViewColumns.Total(reader);
                if (!reader.IsDBNull(1)) rows.Add(ReadListed(reader));
            }
        return release is { } active ? new(PageOf(active, rows, request), congress, total) : null;
    }

    public async Task<BillDetail?> FindAsync(string recordId, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand($"""
            SELECT b.release_number, {Columns}, b.crs_summary_text, b.crs_summary_version,
                   b.history_observed_at_utc_ticks, b.history_action_count,
                   ARRAY(SELECT i.issue_id FROM public_api.bill_issues i
                          WHERE i.record_id = b.record_id AND i.revision_number = b.revision_number ORDER BY i."position"),
                   s.latest_action_text, s.latest_action_date, s.read_at_utc_ticks,
                   terms.names, terms.definitions, terms.cuts, terms.quotes, terms.label,
                   e.approved_at_utc_ticks, f.first_published_at_utc_ticks, b.summary_from_crs,
                   ARRAY(SELECT t.part FROM public_api.bill_text_sentences t
                          WHERE t.record_id = b.record_id AND t.revision_number = b.revision_number ORDER BY t."position"),
                   ARRAY(SELECT t.sentence FROM public_api.bill_text_sentences t
                          WHERE t.record_id = b.record_id AND t.revision_number = b.revision_number ORDER BY t."position"),
                   ARRAY(SELECT t.quote FROM public_api.bill_text_sentences t
                          WHERE t.record_id = b.record_id AND t.revision_number = b.revision_number ORDER BY t."position"),
                   b.introduced_on
              FROM public_api.bills b
              {KeyTermsSql}
              LEFT JOIN public_api.bill_status s ON s.record_id = b.record_id AND s.revision_number = b.revision_number
              LEFT JOIN public_api.record_entries e ON e.kind = 'action' AND e.record_id = b.record_id AND e.revision_number = b.revision_number
              LEFT JOIN public_api.record_first_publications f ON f.kind = 'action' AND f.record_id = b.record_id
             WHERE b.record_id = @id
            """);
        command.Parameters.AddWithValue("id", recordId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new(reader.GetInt32(0), ReadRow(reader), reader.GetFieldValue<string[]>(27), ReadLatestAction(reader), ReadHistory(reader, 25, 26),
            ViewColumns.Text(reader, 23), ViewColumns.Text(reader, 24), ReadKeyTerms(reader, 31), ReadTextSentences(reader))
        {
            KeyTermsLabel = ViewColumns.Text(reader, 35),
            CheckedAtUtc = ViewColumns.Utc(reader, 36),
            FirstPublishedAtUtc = ViewColumns.Utc(reader, 37),
            SummaryFromCrs = reader.GetBoolean(38),
            IntroducedOn = ViewColumns.Date(reader, 42)
        };
    }

    /// <summary>The four arrays come from one view, ordered by the same position, so they pair by index.</summary>
    private static BillKeyTermData[] ReadKeyTerms(DbDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal)) return [];
        var terms = reader.GetFieldValue<string[]>(ordinal);
        var definitions = reader.GetFieldValue<string[]>(ordinal + 1);
        var cuts = reader.GetFieldValue<bool[]>(ordinal + 2);
        var quotes = reader.GetFieldValue<string[]>(ordinal + 3);
        return [.. terms.Select((term, index) => new BillKeyTermData(term, definitions[index], cuts[index], quotes[index]))];
    }

    /// <summary>The three arrays come from one view, ordered by the same position, so they pair by index.</summary>
    private static BillTextSentenceData[] ReadTextSentences(DbDataReader reader)
    {
        var parts = reader.GetFieldValue<string[]>(39);
        var sentences = reader.GetFieldValue<string[]>(40);
        var quotes = reader.GetFieldValue<string[]>(41);
        return [.. parts.Select((part, index) => new BillTextSentenceData(ViewColumns.Enum<PublishedTextPart>(part), sentences[index], quotes[index]))];
    }

    /// <summary>Reads the facts of the bill number match for a bounded set of bills in one query. A bill that left the release has no entry.
    /// The vote is the newest House roll call on the bill. A bill with no House roll call gets its newest Senate roll call.</summary>
    public async Task<Dictionary<string, BillPinFacts>> ReadPinFactsAsync(string[] recordIds, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(PinFactsSql);
        command.Parameters.AddWithValue("ids", recordIds);
        var facts = new Dictionary<string, BillPinFacts>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var lawDate = ViewColumns.Text(reader, 2) is { } text && BillStatusMeaning.PublicLawNumberOf(text) is not null ? ViewColumns.Date(reader, 3) : null;
            var vote = ViewColumns.Text(reader, 4) is { } voteId ? new SearchBillVoteData(voteId, Enum.Parse<PublishedChamber>(reader.GetString(5))) : null;
            facts[reader.GetString(0)] = new(reader.GetString(0), ViewColumns.Text(reader, 1), lawDate, vote);
        }
        return facts;
    }

    /// <summary>Reads the sponsor, status, and key terms of the exact revisions on one bounded bill page in its snapshot.</summary>
    public async Task<Dictionary<(string RecordId, int RevisionNumber), BillListFacts>> ReadListFactsAsync(NpgsqlTransaction transaction,
        IReadOnlyList<BillRow> bills, CancellationToken cancellationToken)
    {
        var facts = new Dictionary<(string, int), BillListFacts>();
        if (bills.Count == 0) return facts;
        await using var command = new NpgsqlCommand(ListFactsSql, transaction.Connection, transaction);
        command.Parameters.AddWithValue("ids", bills.Select(bill => bill.RecordId).ToArray());
        command.Parameters.AddWithValue("revisions", bills.Select(bill => bill.RevisionNumber).ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            facts[(reader.GetString(0), reader.GetInt32(1))] = new(ViewColumns.Text(reader, 2), ViewColumns.Text(reader, 3), ViewColumns.Date(reader, 4),
                reader.IsDBNull(5) ? null : ViewColumns.Enum<BillStatusStage>(reader, 5), ReadKeyTerms(reader, 6), ViewColumns.Text(reader, 10));
        return facts;
    }

    /// <summary>Lists the policy areas that bills of one Congress of the active release hold, as a filter lists them, inside the transaction of the caller.</summary>
    public async Task<SearchChoiceData[]> ListPolicyAreasAsync(NpgsqlTransaction transaction, int congress, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(PolicyAreasSql, transaction.Connection, transaction);
        command.Parameters.AddWithValue("congress", congress);
        var areas = new List<SearchChoiceData>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) areas.Add(new(reader.GetString(0), reader.GetString(1)));
        return [.. areas];
    }

    /// <summary>Reads one page of the sponsorships of a bill. Null means that no release is active or that the active release holds no such bill.</summary>
    public async Task<SponsorshipPage?> ListSponsorshipsAsync(SponsorshipRequest request, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(SponsorshipSql(request));
        command.Parameters.AddWithValue("id", request.RecordId);
        command.Parameters.AddWithValue("take", request.Limit + 1);
        if (request.Role is { } role) command.Parameters.AddWithValue("role", role.ToString());
        if (request.Cursor is { } cursor) command.Parameters.AddWithValue("cursor_position", cursor.Position);
        var rows = new List<SponsorshipRow>();
        int? release = null;
        var billExists = false;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                release = reader.GetInt32(0);
                billExists = reader.GetBoolean(1);
                if (!reader.IsDBNull(2)) rows.Add(ReadSponsorship(reader));
            }
        if (release is not { } active || !billExists) return null;
        var page = rows.Take(request.Limit).ToArray();
        var next = rows.Count > request.Limit ? SponsorshipCursor.Of(active, page[^1]).Encode() : null;
        return new(active, page, next);
    }

    /// <summary>Reads one page of the completed history snapshot of a bill in one statement.</summary>
    public async Task<ActionHistoryPage?> ListActionsAsync(ActionHistoryRequest request, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(ActionHistorySql(request));
        command.Parameters.AddWithValue("id", request.RecordId);
        command.Parameters.AddWithValue("take", request.Limit + 1);
        if (request.Cursor is { } cursor) command.Parameters.AddWithValue("cursor_position", cursor.Position);
        var rows = new List<BillActionRow>();
        int? release = null;
        BillHistory? history = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                release = reader.GetInt32(0);
                history ??= ReadHistory(reader, 1, 2);
                if (!reader.IsDBNull(3)) rows.Add(ReadAction(reader));
            }
        if (release is not { } active) return null;
        var page = rows.Take(request.Limit).ToArray();
        var next = rows.Count > request.Limit ? ActionHistoryCursor.Of(active, page[^1]).Encode() : null;
        return new(active, history, page, next);
    }

    /// <summary>Reads every sponsorship of a bill with the name of its official and the party from <c>public_api.bill_sponsorship_parties</c>, the one owner of the
    /// term party on the sponsorship date, in one statement. Null means that no release is active. The count is bounded by the officials that the release tracks.</summary>
    public async Task<BillSponsors?> ListSponsorsAsync(string recordId, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand("""
            SELECT a.release_number, s."position", s.official_id, o.name, p.party, s.role, s.is_original, s.sponsored_on, s.withdrawn_on
              FROM (SELECT release_number FROM public_api.active_release LIMIT 1) a
              LEFT JOIN public_api.bill_sponsorships s ON s.record_id = @id
              LEFT JOIN public_api.officials o ON o.official_id = s.official_id
              LEFT JOIN public_api.bill_sponsorship_parties p
                     ON p.record_id = s.record_id AND p.revision_number = s.revision_number AND p."position" = s."position"
             ORDER BY s."position"
            """);
        command.Parameters.AddWithValue("id", recordId);
        var rows = new List<BillSponsorRow>();
        int? release = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            release = reader.GetInt32(0);
            if (!reader.IsDBNull(1) && !reader.IsDBNull(3)) rows.Add(ReadBillSponsor(reader));
        }
        return release is { } active ? new(active, [.. rows]) : null;
    }

    /// <summary>Reads the configured names of the given issue IDs. Null means that no release is active.</summary>
    public async Task<BillIssueNames?> ListIssueNamesAsync(string[] issueIds, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand("""
            SELECT a.release_number, i.issue_id, i.name
              FROM (SELECT release_number FROM public_api.active_release LIMIT 1) a
              LEFT JOIN public_api.release_issues i ON i.issue_id = ANY(@ids)
             ORDER BY i.name, i.issue_id
            """);
        command.Parameters.AddWithValue("ids", issueIds);
        var issues = new List<BillIssueData>();
        int? release = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            release = reader.GetInt32(0);
            if (!reader.IsDBNull(1)) issues.Add(new(reader.GetString(1), reader.GetString(2)));
        }
        return release is { } active ? new(active, [.. issues]) : null;
    }

    private static string ListSql(BillRequest request) => $"""
        SELECT a.release_number, {Columns}, b.latest_action_date
          FROM (SELECT release_number FROM public_api.active_release LIMIT 1) a
          LEFT JOIN LATERAL (
               SELECT b.*, k.latest_action_date FROM (
                    SELECT b.record_id, b.revision_number, b.latest_action_date {KeysOf(request)}
                     WHERE TRUE{Filters(request)}{AfterCursor(request)}
                     ORDER BY {KeysetPage.Order(Order, request.Backward)} LIMIT @take) k
               {BillsOfKeysSql}) b
            ON TRUE
         ORDER BY {KeysetPage.Order(Order, request.Backward)}
        """;

    private static string NewestCongressSql(BillRequest request) => $"""
        WITH newest AS MATERIALIZED (
             SELECT c.congress, c.bills FROM public_api.release_bill_counts c WHERE c.bills > 0 ORDER BY c.congress DESC LIMIT 1)
        SELECT a.release_number, {Columns}, b.latest_action_date, n.congress, coalesce(n.bills, 0)::bigint AS in_congress
          FROM (SELECT release_number FROM public_api.active_release LIMIT 1) a
          LEFT JOIN newest n ON TRUE
          LEFT JOIN LATERAL (
               SELECT b.*, k.latest_action_date FROM (
                    SELECT b.record_id, b.revision_number, b.latest_action_date {KeysOf(request)}
                     WHERE b.congress = n.congress{Filters(request)}{AfterCursor(request)}
                     ORDER BY {KeysetPage.Order(Order, request.Backward)} LIMIT @take) k
               {BillsOfKeysSql}) b
            ON TRUE
         ORDER BY {KeysetPage.Order(Order, request.Backward)}
        """;

    /// <summary>The FROM clause of the keys <c>b</c> that a list reads. The issue or the sponsor drives the read when the request names one, because those rows are few.
    /// Each other filter is a test on the key columns.</summary>
    private static string KeysOf(BillRequest request) =>
        request.Issue is not null ? IssueKeysSql
        : HasSponsor(request) ? KeysOf($"SELECT DISTINCT s.record_id, s.revision_number FROM public_api.bill_sponsorships s WHERE {SponsorMatch(request)}")
        : "FROM public_api.bill_keys b";

    private static string KeysOf(string rows) => $"""
        FROM ({rows}) d
        CROSS JOIN LATERAL (
             SELECT b.* FROM public_api.bill_keys b
              WHERE b.record_id = d.record_id AND b.revision_number = d.revision_number OFFSET 0) b
        """;

    private static bool HasSponsor(BillRequest request) => request.Sponsor is not null || request.SponsorName is not null;

    /// <summary>The test of one sponsorship <c>s</c> against the sponsor of the request. The JSON list names the sponsor ID and the list page names the sponsor; never both.
    /// The names come from <c>release_officials</c>, the names that <c>officials</c> shows, once for each statement, so no bill rebuilds the official counts of <c>officials</c>.</summary>
    private static string SponsorMatch(BillRequest request) =>
        $"s.role = '{nameof(PublishedSponsorshipRole.Sponsor)}'" +
        (request.Sponsor is null ? "" : " AND s.official_id = @sponsor") +
        (request.SponsorName is null ? "" : $" AND s.official_id IN (SELECT o.official_id FROM public_api.release_officials o WHERE {OfficialStore.NameMatchSql("o")})");

    // The sponsor test runs here only when the issue drives the read. OFFSET 0 keeps it a lookup for each key of the issue.
    private static string Filters(BillRequest request) =>
        (request.Congress is null ? "" : " AND b.congress = @congress") +
        (request.BillType is null ? "" : " AND b.bill_type = @bill_type") +
        (request.BillNumber is null ? "" : " AND b.bill_number = @bill_number") +
        (request.PolicyArea is null ? "" : " AND b.policy_area_id = @policy_area") +
        (request.Issue is null || !HasSponsor(request) ? "" : $"""
             AND EXISTS (SELECT 1 FROM public_api.bill_sponsorships s
                          WHERE s.record_id = b.record_id AND s.revision_number = b.revision_number AND {SponsorMatch(request)} OFFSET 0)
            """) +
        (request.Status is null ? "" : " AND b.status = @status") +
        (request.From is null ? "" : " AND b.latest_action_date >= @from") +
        (request.To is null ? "" : " AND b.latest_action_date <= @to");

    private static string AfterCursor(BillRequest request) => request.Cursor switch
    {
        null => "",
        { LatestActionDate: null } => KeysetPage.Window(AfterNoAction, "b.record_id", "@cursor_id", request.Backward),
        _ => KeysetPage.Window(AfterDated, "b.record_id", "@cursor_id", request.Backward)
    };

    private static void AddParameters(NpgsqlCommand command, BillRequest request)
    {
        var parameters = command.Parameters;
        parameters.AddWithValue("take", request.Limit + 1);
        if (request.Congress is { } congress) parameters.AddWithValue("congress", congress);
        if (request.BillType is not null) parameters.AddWithValue("bill_type", request.BillType);
        if (request.BillNumber is { } billNumber) parameters.AddWithValue("bill_number", billNumber);
        if (request.PolicyArea is not null) parameters.AddWithValue("policy_area", request.PolicyArea);
        if (request.Sponsor is not null) parameters.AddWithValue("sponsor", request.Sponsor);
        if (request.SponsorName is not null) parameters.AddWithValue("name_prefix", request.SponsorName);
        if (request.Status is { } status) parameters.AddWithValue("status", status.ToString());
        if (request.Issue is not null) parameters.AddWithValue("issue", request.Issue);
        if (request.From is { } from) parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.Date) { Value = from });
        if (request.To is { } to) parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.Date) { Value = to });
        if (request.Cursor is not { } cursor) return;
        if (cursor.LatestActionDate is { } date) parameters.Add(new NpgsqlParameter("cursor_date", NpgsqlDbType.Date) { Value = date });
        parameters.AddWithValue("cursor_id", cursor.RecordId);
    }

    private static string SponsorshipSql(SponsorshipRequest request) => $"""
        SELECT a.release_number, EXISTS (SELECT 1 FROM public_api.bills b WHERE b.record_id = @id),
               s."position", s.official_id, s.role, s.is_original, s.sponsored_on, s.withdrawn_on
          FROM (SELECT release_number FROM public_api.active_release LIMIT 1) a
          LEFT JOIN LATERAL (
               SELECT s.* FROM public_api.bill_sponsorships s
                WHERE s.record_id = @id{(request.Role is null ? "" : " AND s.role = @role")}{(request.Cursor is null ? "" : " AND s.\"position\" > @cursor_position")}
                ORDER BY s."position" LIMIT @take) s
            ON TRUE
         ORDER BY s."position"
        """;

    private static ListedBill ReadListed(DbDataReader reader) => new(ReadRow(reader), ViewColumns.Date(reader, LatestActionColumn));

    /// <summary>Takes one page of the rows that a list read, one more than the limit when a next page exists. The cursor holds the sort key of the last row of the page.</summary>
    private static BillPage PageOf(int release, List<ListedBill> rows, BillRequest request)
    {
        var (page, next, previous) = KeysetPage.Cut(rows, request.Limit, request.Cursor?.Encode(), request.Backward,
            row => new BillCursor(release, row.LatestActionDate, row.Bill.RecordId).Encode());
        return new(release, [.. page.Select(row => row.Bill)], next) { PreviousCursor = previous };
    }

    // Column 0 holds the release. The row starts at column 1.
    private static BillRow ReadRow(DbDataReader reader) => new()
    {
        RecordId = reader.GetString(1),
        RevisionNumber = reader.GetInt32(2),
        Congress = reader.GetInt32(3),
        BillType = reader.GetString(4),
        BillNumber = reader.GetInt32(5),
        BillTitle = reader.GetString(6),
        Headline = reader.GetString(7),
        Summary = reader.GetString(8),
        TextOrigin = ViewColumns.Enum<PublishedTextOrigin>(reader, 9),
        Label = ViewColumns.Text(reader, 10),
        PolicyArea = ViewColumns.Text(reader, 11),
        PolicyAreaId = ViewColumns.Text(reader, 12),
        EventDate = reader.GetFieldValue<DateOnly>(13),
        SponsorCount = ViewColumns.Number(reader, 14)!.Value,
        CosponsorCount = ViewColumns.Number(reader, 15)!.Value,
        BillLine = ViewColumns.Text(reader, 16),
        WhyItMatters = ViewColumns.Text(reader, 17),
        TextVersionName = ViewColumns.Text(reader, 18),
        TextVersionDate = ViewColumns.Date(reader, 19),
        TextCharsRead = ViewColumns.Number(reader, 20),
        TextCharsTotal = ViewColumns.Number(reader, 21),
        PublicLaw = ViewColumns.Text(reader, 22)
    };

    private static BillHistory? ReadHistory(DbDataReader reader, int observedAtIndex, int actionCountIndex) => reader.IsDBNull(observedAtIndex) || reader.IsDBNull(actionCountIndex)
        ? null
        : new(new DateTimeOffset(reader.GetInt64(observedAtIndex), TimeSpan.Zero), reader.GetInt32(actionCountIndex));

    // Columns 28 to 30 hold the status. The view holds no row for a bill with no latest action.
    private static BillLatestAction? ReadLatestAction(DbDataReader reader) => reader.IsDBNull(28)
        ? null
        : new(reader.GetString(28), reader.GetFieldValue<DateOnly>(29), new DateTimeOffset(reader.GetInt64(30), TimeSpan.Zero));

    // Column 0 holds the release and column 1 holds whether the bill exists. The row starts at column 2.
    private static SponsorshipRow ReadSponsorship(DbDataReader reader) => new()
    {
        Position = reader.GetInt32(2),
        OfficialId = reader.GetString(3),
        Role = ViewColumns.Enum<PublishedSponsorshipRole>(reader, 4),
        IsOriginal = reader.GetBoolean(5),
        SponsoredOn = reader.GetFieldValue<DateOnly>(6),
        WithdrawnOn = ViewColumns.Date(reader, 7)
    };

    // Column 0 holds the release. The row starts at column 1.
    private static BillSponsorRow ReadBillSponsor(DbDataReader reader) => new(
        reader.GetInt32(1), reader.GetString(2), reader.GetString(3), ViewColumns.Text(reader, 4), ViewColumns.Enum<PublishedSponsorshipRole>(reader, 5),
        reader.GetBoolean(6), reader.GetFieldValue<DateOnly>(7), ViewColumns.Date(reader, 8));

    private static string ActionHistorySql(ActionHistoryRequest request) => $"""
        SELECT a.release_number, b.history_observed_at_utc_ticks, b.history_action_count,
               h."position", h.action_date, h.action_time, h.text, h.type, h.action_code, h.source_system_code, h.source_system_name
          FROM (SELECT release_number FROM public_api.active_release LIMIT 1) a
          JOIN public_api.bills b ON b.record_id = @id
          LEFT JOIN LATERAL (
               SELECT h.* FROM public_api.bill_actions h
                WHERE h.release_number = a.release_number AND h.record_id = b.record_id AND h.revision_number = b.revision_number
                  {(request.Cursor is null ? "" : "AND h.\"position\" > @cursor_position")}
                ORDER BY h."position" LIMIT @take) h ON TRUE
         ORDER BY h."position"
        """;

    private static BillActionRow ReadAction(DbDataReader reader) => new()
    {
        Position = reader.GetInt32(3),
        ActionDate = reader.GetFieldValue<DateOnly>(4),
        ActionTime = reader.IsDBNull(5) ? null : reader.GetFieldValue<TimeOnly>(5),
        Text = reader.GetString(6),
        Type = ViewColumns.Text(reader, 7),
        ActionCode = ViewColumns.Text(reader, 8),
        SourceSystemCode = ViewColumns.Number(reader, 9),
        SourceSystemName = ViewColumns.Text(reader, 10)
    };
}
