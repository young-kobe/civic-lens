using CivicLens.Publication.Contracts;
using Npgsql;

namespace CivicLens.PublicApi.PartyAgreement;

/// <summary>Reads the stored alignments of active vote revisions through public_api.party_agreement.</summary>
public sealed class PartyAgreementStore(NpgsqlDataSource views)
{
    private const string Sql = """
        SELECT a.release_number, g.release_number, g.congress, g.kind, g.agreed, g.split, g.mixed
          FROM (SELECT release_number FROM public_api.active_release LIMIT 1) a
          LEFT JOIN public_api.party_agreement g ON g.release_number = a.release_number
           AND (g.congress IS NULL OR (g.chamber = @chamber AND g.congress = @congress))
        """;

    public async Task<PartyAgreementAnswer?> FindAsync(PartyAgreementRequest request, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(Sql);
        command.Parameters.AddWithValue("chamber", request.Chamber.ToString());
        command.Parameters.AddWithValue("congress", request.Congress);
        var kinds = new SortedDictionary<PublishedVoteKind, PartyAgreementKindAnswer>();
        int? release = null;
        var hasUnknownCongress = false;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.IsDBNull(0)) continue;
                release = reader.GetInt32(0);
                if (reader.IsDBNull(1)) continue;
                if (reader.IsDBNull(2))
                {
                    hasUnknownCongress = true;
                    continue;
                }
                var kind = Enum.Parse<PublishedVoteKind>(reader.GetString(3));
                var agreed = checked((int)reader.GetInt64(4));
                var split = checked((int)reader.GetInt64(5));
                var mixed = checked((int)reader.GetInt64(6));
                if (agreed + split + mixed == 0) continue;
                kinds.Add(kind, new(kind, agreed, split, mixed, agreed + split + mixed));
            }
        }

        if (release is not { } active) return null;
        if (hasUnknownCongress) return new(active, null);
        var items = kinds.Values.ToArray();
        var totalAgreed = items.Sum(kind => kind.Agreed);
        var totalSplit = items.Sum(kind => kind.Split);
        var totalMixed = items.Sum(kind => kind.Mixed);
        var nominations = items.Where(kind => kind.Kind == PublishedVoteKind.Nomination).Sum(kind => kind.Total);
        return new(active, new(request.Chamber, request.Congress, items, totalAgreed, totalSplit, totalMixed,
            totalAgreed + totalSplit + totalMixed, nominations));
    }
}
