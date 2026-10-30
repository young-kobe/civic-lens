namespace CivicLens.Application.Collection.FederalRegister;

/// <summary>One parsed document with its saved extraction and its signer, as preparation hands it to admission.</summary>
public sealed record FederalRegisterEvidence(FederalRegisterDocument Document, string ExtractionId, string ExtractionText, string PersonId);
