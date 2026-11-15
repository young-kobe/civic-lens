namespace CivicLens.PublicWeb.Publication;

/// <summary>The line that says what a vote is about. <c>FromBill</c> is true for the bill line, which carries its source note.</summary>
public sealed record VoteAbout(string Text, bool FromBill);
