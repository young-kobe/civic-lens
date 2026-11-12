namespace CivicLens.PublicWeb.Publication;

/// <summary>One statement in the list of an official. <c>Label</c> is the label column of the view.</summary>
public sealed record OfficialRecordStatementData(string RecordId, DateOnly? StatementDate, string Headline, string? Label);
