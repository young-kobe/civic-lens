using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>Owns the rule that two entries of one revision agree. The eight optional fields are new to the contract, so an older entry may lack them.</summary>
internal static class ReleaseEntryComparison
{
    public static bool SameOutsideNewFields(PublishedRecordEntry first, PublishedRecordEntry second) =>
        Serialize(WithoutNewFields(first)) == Serialize(WithoutNewFields(second));

    private static PublishedRecordEntry WithoutNewFields(PublishedRecordEntry entry) =>
        entry with { YeaCount = null, NayCount = null, Alignment = null, Chamber = null, VoteKind = null, Congress = null, CosponsorIds = null, BillLine = null };

    private static string Serialize(PublishedRecordEntry entry) => JsonSerializer.Serialize(entry, PublicationProtocol.JsonOptions);
}
