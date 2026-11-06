namespace CivicLens.Application.Analysis;

/// <summary>A document change draft that still waits for a person and whose current revision came from a model run with an old version.
/// The next model revision of the draft takes the number after <see cref="RevisionNumber"/>.</summary>
public sealed record DocumentChangeModelDraft(string DraftId, int RevisionNumber);
