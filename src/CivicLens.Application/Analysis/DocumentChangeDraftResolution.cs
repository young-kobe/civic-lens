using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Application.Analysis;

public sealed record DocumentChangeDraftResolution(DocumentChangeDraftRevision? Revision, ImmutableArray<string> Errors,
    bool HasCitationErrors);
