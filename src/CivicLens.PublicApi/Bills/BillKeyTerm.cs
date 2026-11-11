namespace CivicLens.PublicApi.Bills;

/// <summary>One key term of a bill that an AI model chose from the definitions section of the bill text. <c>Definition</c> is an exact quote of that section,
/// never AI wording. The bill row carries the AI label. The list is empty unless the bill has an AI text summary that cites bill text.</summary>
public sealed record BillKeyTerm(string Term, string Definition);
