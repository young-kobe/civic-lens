namespace CivicLens.Application.ExecutiveActions;

public sealed class CorrectionOfCorrectionException(string documentNumber, string correctedNumber)
    : ArgumentException($"Federal Register document {documentNumber} corrects {correctedNumber}, which is itself a correction. Civic Lens records a correction only for an original document.");
