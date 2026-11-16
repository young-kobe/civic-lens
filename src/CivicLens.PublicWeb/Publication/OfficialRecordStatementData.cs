using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>One statement in the list of an official. <c>Label</c> and <c>TextOrigin</c> are the label and text origin columns of the view.</summary>
public sealed record OfficialRecordStatementData(string RecordId, DateOnly? StatementDate, string Headline, string? Label, PublishedTextOrigin? TextOrigin);
