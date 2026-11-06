namespace CivicLens.Application.Publication;

/// <param name="VoteRevisionsExamined">The published vote revisions with a member list whose vote columns were unwritten or null at the start. A revision that the release gives null columns counts again in each run.</param>
/// <param name="VoteRevisionsWritten">The revisions that this run wrote, whether to values or to null.</param>
/// <param name="VoteRevisionsWithoutKeptFile">The revisions that no kept folder holds as a record file. They stay unwritten.</param>
/// <param name="VoteRevisionsWithOtherParties">The revisions whose shared member list already holds other parties. They stay unwritten.</param>
/// <param name="OfficialPhotoPathsFilled">The official rows that got a photo path from the officials file of their release.</param>
/// <param name="EntryCongressesFilled">The entry rows that got a Congress.</param>
public sealed record StoredColumnsBackfillResult(int VoteRevisionsExamined, int VoteRevisionsWritten, int VoteRevisionsWithoutKeptFile,
    int VoteRevisionsWithOtherParties, int OfficialPhotoPathsFilled, int EntryCongressesFilled);
