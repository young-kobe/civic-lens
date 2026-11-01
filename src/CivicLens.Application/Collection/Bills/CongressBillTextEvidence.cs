namespace CivicLens.Application.Collection.Bills;

public sealed record CongressBillTextEvidence(CongressBillTextVersion Version, CongressBillTextReading Reading,
    string ExtractionId);
