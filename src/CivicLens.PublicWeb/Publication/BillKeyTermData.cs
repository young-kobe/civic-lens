namespace CivicLens.PublicWeb.Publication;

/// <summary>One key term of a bill that an AI model chose from the definitions section of the bill text. <c>Definition</c> is an exact quote of that section
/// in plain quote marks, never AI wording. <c>Quote</c> holds the exact cited characters of that quote. <c>Cut</c> is true when the quote ends before the end
/// of its definition paragraph. The AI label comes from the label column of <c>public_api.bill_key_terms</c>, and the bill detail serves it once. The list is
/// empty unless the bill has an AI text summary that cites bill text.</summary>
public sealed record BillKeyTermData(string Term, string Definition, bool Cut, string Quote);
