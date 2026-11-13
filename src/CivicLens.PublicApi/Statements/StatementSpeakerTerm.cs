using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Statements;

/// <summary>The party and the seat of one speaker on the statement date, from <c>public_api.statement_speaker_terms</c>. Null fields mean that no single term gives them.</summary>
public sealed record StatementSpeakerTerm(string? Party, PublishedOfficeKind? Kind, string? Office, string? State, int? District);
