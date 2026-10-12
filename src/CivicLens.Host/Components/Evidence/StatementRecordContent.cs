namespace CivicLens.Host.Components.Evidence;

public sealed record StatementRecordContent(string Headline, string? Summary, string StatementDate, string? DateEvidence,
    IReadOnlyList<string> Speakers, IReadOnlyList<string> OtherSpeakers, IReadOnlyList<string> Issues,
    IReadOnlyList<string> PolicyAreas, IReadOnlyList<string>? BillNumbers, IReadOnlyList<StatementQuoteContent> Quotations);
