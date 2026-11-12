namespace CivicLens.Application.Collection.FederalRegister;

/// <summary>The saved body text of one original document, as preparation hands it to admission. <see cref="BodyUrl"/> is the job URL.</summary>
public sealed record FederalRegisterTextEvidence(string DocumentNumber, string BodyUrl, FederalRegisterTextReading Reading, string ExtractionId);
