using System.Collections.Immutable;
using CivicLens.Core.Statements;

namespace CivicLens.Application.Statements;

public sealed record StatementDraftResolution(StatementDraftRevision? Revision, ImmutableArray<string> Errors, bool HasCitationErrors);
