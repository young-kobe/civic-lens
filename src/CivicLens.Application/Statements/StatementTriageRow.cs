using System.Collections.Immutable;
using CivicLens.Application.Review;

namespace CivicLens.Application.Statements;

public sealed record StatementTriageRow(StatementReviewListItem Review, ApprovalCheck Check, ImmutableArray<string> Quotes);
