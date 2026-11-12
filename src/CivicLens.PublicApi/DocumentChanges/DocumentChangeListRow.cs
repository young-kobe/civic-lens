namespace CivicLens.PublicApi.DocumentChanges;

/// <summary>One row of the list that the view <c>public_api.document_changes</c> gives. <c>PlaceCount</c> is the number of changed places, which the view stores as the hunk count.</summary>
public sealed record DocumentChangeListRow(string RecordId, string Headline, string? Label, string Institution, DateOnly? ChangeDate, int? PlaceCount);
