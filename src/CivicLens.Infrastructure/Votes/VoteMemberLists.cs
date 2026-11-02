using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CivicLens.Infrastructure.Votes;

/// <summary>Owns the identity and the insert of a member list. Revisions with the same ordered members share one list.</summary>
internal static class VoteMemberLists
{
    private const string Version = "vote-member-list-v1";
    private const char FieldSeparator = '\u001F';

    private const string InsertSql = """
        WITH inserted AS (
            INSERT INTO vote_member_lists (list_id, member_count, created_at_utc_ticks)
            VALUES (@list_id, @member_count, @created_at_utc_ticks)
            ON CONFLICT DO NOTHING
            RETURNING list_id)
        INSERT INTO vote_member_list_entries (list_id, position, person_id, bioguide_id, vote_cast, citation_extraction_id, citation_start, citation_length)
        SELECT inserted.list_id, entry.position, entry.person_id, entry.bioguide_id, entry.vote_cast, entry.extraction_id, entry.start, entry.length
        FROM inserted CROSS JOIN unnest(@positions, @person_ids, @bioguide_ids, @casts, @extraction_ids, @starts, @lengths)
            AS entry(position, person_id, bioguide_id, vote_cast, extraction_id, start, length)
        """;

    public static string IdOf(ImmutableArray<MemberVote> members) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', members.Select(CanonicalLine).Prepend(Version)))));

    /// <summary>Inserts the list and its entries when the list is new. Call it in the transaction that holds the vote review lock.</summary>
    public static Task EnsureAsync(CollectionAttemptDbContext db, VoteDraftRevision revision, CancellationToken cancellationToken)
    {
        var members = revision.Members;
        return db.Database.ExecuteSqlRawAsync(InsertSql,
        [
            new NpgsqlParameter("list_id", IdOf(members)),
            new NpgsqlParameter("member_count", (short)members.Length),
            new NpgsqlParameter("created_at_utc_ticks", revision.CreatedAtUtc.UtcTicks),
            new NpgsqlParameter("positions", Enumerable.Range(0, members.Length).Select(position => (short)position).ToArray()),
            new NpgsqlParameter("person_ids", members.Select(member => member.PersonId).ToArray()),
            new NpgsqlParameter("bioguide_ids", members.Select(member => member.BioguideId).ToArray()),
            new NpgsqlParameter("casts", members.Select(member => member.Cast).ToArray()),
            new NpgsqlParameter("extraction_ids", members.Select(member => member.Citation.ExtractionId).ToArray()),
            new NpgsqlParameter("starts", members.Select(member => member.Citation.Start).ToArray()),
            new NpgsqlParameter("lengths", members.Select(member => member.Citation.Length).ToArray())
        ], cancellationToken);
    }

    private static string CanonicalLine(MemberVote member) => string.Create(CultureInfo.InvariantCulture,
        $"{member.PersonId}{FieldSeparator}{member.BioguideId}{FieldSeparator}{member.Cast}{FieldSeparator}{member.Citation.ExtractionId}{FieldSeparator}{member.Citation.Start}{FieldSeparator}{member.Citation.Length}");
}
