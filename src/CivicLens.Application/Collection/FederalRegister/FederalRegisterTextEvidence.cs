namespace CivicLens.Application.Collection.FederalRegister;

/// <summary>The saved body text of one original document, as preparation hands it to admission.</summary>
public sealed record FederalRegisterTextEvidence(string DocumentNumber, DateOnly PublishedOn, FederalRegisterTextReading Reading,
    string ExtractionId);
