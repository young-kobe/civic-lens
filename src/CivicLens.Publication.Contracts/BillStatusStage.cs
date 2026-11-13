using System.Text.Json.Serialization;

namespace CivicLens.Publication.Contracts;

/// <summary>The coarse stage of a bill, which the public bills list filters on. <see cref="BillStatusMeaning"/> owns the rule that picks it.
/// The query text is camel case, as for <see cref="PublishedExecutiveActionKind"/>.</summary>
[JsonConverter(typeof(PublishedEnumJsonConverter))]
public enum BillStatusStage
{
    InCommittee,
    PassedHouse,
    PassedSenate,
    BecameLaw,
    Other
}
