namespace CivicLens.Application.Publication;

/// <param name="VoteRevisionsExamined">The published vote revisions with a member list whose columns were not written at the start.</param>
/// <param name="VoteRevisionsWritten">The revisions that this run wrote, whether to values or to null.</param>
/// <param name="OpenVoteRevisionsWithoutKeptFile">The revisions in the active release range that no kept folder holds as a record file. They stay unwritten. They fail the command.</param>
/// <param name="ClosedVoteRevisionsWithoutKeptFile">The revisions that left the active release and that no kept folder holds. They stay unwritten. The command reports them and does not fail.</param>
/// <param name="OfficialPhotoPathsFilled">The official rows that got a photo path from the officials file of their release.</param>
/// <param name="EntryCongressesFilled">The entry rows that got a Congress.</param>
public sealed record StoredColumnsBackfillResult(int VoteRevisionsExamined, int VoteRevisionsWritten, int OpenVoteRevisionsWithoutKeptFile,
    int ClosedVoteRevisionsWithoutKeptFile, int OfficialPhotoPathsFilled, int EntryCongressesFilled);
