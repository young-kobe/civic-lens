namespace CivicLens.Infrastructure.Publication;

/// <summary>The one SQL owner of the term party rule: the party that every term of an official held on a day. A day that no term holds,
/// a blank term party, and terms of two parties on one day give no party. A term holds from <c>start_on</c> to <c>end_on</c>, and both days count.
/// The public views <c>bill_sponsorship_parties</c> and <c>statement_speaker_terms</c> and the issue line-up of the release commit read it.</summary>
internal static class TermParties
{
    /// <summary>The published terms that the public views read.</summary>
    public const string PublicTerms = "public_api.release_official_terms";

    /// <summary>The lateral subquery that gives the column <c>term.party</c>: the party that every term of the official held on the day, or null.
    /// <paramref name="terms"/> is the relation of the terms to read: the public view, or the terms of one release in the release commit.</summary>
    public static string PartyOn(string terms, string officialColumn, string dayColumn) => $"""
        CROSS JOIN LATERAL (
               SELECT CASE WHEN count(DISTINCT held.party) = 1 AND count(held.party) = count(*) THEN min(held.party) END AS party
                 FROM (SELECT CASE WHEN btrim(t.party) <> '' THEN t.party END AS party
                         FROM {terms} t
                        WHERE t.official_id = {officialColumn}
                          AND t.start_on <= {dayColumn} AND (t.end_on IS NULL OR t.end_on >= {dayColumn})) held) term
        """;
}
