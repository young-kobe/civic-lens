using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Core.Briefs;

namespace CivicLens.Application.Briefs;

public sealed record SaveBriefDraftRequest(string RecordId, int ExpectedRevisionNumber, string Headline,
    ImmutableArray<BriefParagraph> Paragraphs, SuggestionChoice Suggestion, string IdempotencyKey);
