namespace CivicLens.PublicApi.Bills;

/// <summary>One key term of a bill that an AI model chose from the definitions section of the bill text. <c>Definition</c> is an exact quote of that section,
/// never AI wording. <c>Cut</c> is true when the quote ends before the end of its definition paragraph. The AI label comes from the label column of
/// <c>public_api.bill_key_terms</c>, and the bill detail serves it once. The list is empty unless the bill has an AI text summary that cites bill text.</summary>
public sealed record BillKeyTerm(string Term, string Definition, bool Cut);
