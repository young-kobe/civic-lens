using System.Collections.Immutable;

namespace CivicLens.Application.Statements;

internal sealed record StatementDraftOutput(string Headline, string Summary, StatementDraftOutputDate? StatementDate,
    ImmutableArray<StatementDraftOutputSpeaker> OtherSpeakers, ImmutableArray<StatementDraftOutputQuotation> Quotations, double Confidence,
    ImmutableArray<StatementDraftOutputSelection> SpeakerOfficialIds = default);

internal sealed record StatementDraftOutputDate(string Date, string Quote, double Confidence);

internal sealed record StatementDraftOutputSelection(string Id, double Confidence);

internal sealed record StatementDraftOutputSpeaker(string Name, double Confidence);

internal sealed record StatementDraftOutputQuotation(string Quote, double Confidence);
