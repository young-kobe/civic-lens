namespace CivicLens.Infrastructure.Review.Persistence;

/// <summary>A draft of a kind that the worker revises. It names the newer update that the worker refused while the record was rejected,
/// because the newest revision holds old facts. A person approval stops while it is set.</summary>
internal interface IRefusedDraftRow
{
    string? RefusedCandidateId { get; set; }
}
