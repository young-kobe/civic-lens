using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>
/// One statement that a reviewer or code linked to the vote. <c>Speakers</c> are the officials of the statement, in the order of the record.
/// <c>Label</c> and <c>TextOrigin</c> describe the headline, as on the official page. <c>LinkApprover</c> says who checked the link, and is null until the entries backfill fills it.
/// </summary>
public sealed record VoteStatementData(string RecordId, DateOnly? StatementDate, string Headline, string? Label, PublishedTextOrigin TextOrigin,
    PublishedApprover? LinkApprover, VoteStatementSpeaker[] Speakers);
