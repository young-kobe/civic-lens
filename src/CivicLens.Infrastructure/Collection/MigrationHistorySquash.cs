using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace CivicLens.Infrastructure.Collection;

/// <summary>
/// Moves a database from the pre-squash migration history to the one baseline migration, before EF applies migrations.
/// The baseline holds the same schema as the old chain, so the step changes only the history rows and runs no schema change.
/// A missing database or a database with no history runs the baseline. A database that already holds the baseline is left as it is.
/// Any other history fails and changes nothing, because only the exact old chain is known to equal the baseline.
/// </summary>
public static class MigrationHistorySquash
{
    public const string BaselineId = "20261116204947_InitialSchema";

    /// <summary>Every migration ID of the chain that the baseline replaced, in order. The last one is the newest pre-squash migration.</summary>
    public static IReadOnlyList<string> PreSquashIds { get; } =
    [
        "20261026010648_InitialSchema",
        "20261026185206_CollectionJobPriority",
        "20261026203851_ReviewRecordLastRevised",
        "20261027201707_RosterTermCaucus",
        "20261028134523_OfficialPhotos",
        "20261029041718_WorkerHeartbeat",
        "20261029195656_VoteviewSenateVotes",
        "20261029210316_ExecutiveActions",
        "20261029221112_CollectorSlotPerPacingKey",
        "20261030001114_DropRosterLisId",
        "20261030182714_PublicationRecordStates",
        "20261030195231_BillSubjects",
        "20261031001908_ReviewHeldReason",
        "20261031041526_ReviewDecisionColumns",
        "20261031175813_DropPublicationRecordsJson",
        "20261031183503_CodeWrittenActions",
        "20261031203820_ActionTextKeptReason",
        "20261031212047_BillLatestAction",
        "20261101165811_BillTextVersions",
        "20261101195619_DropDecisionJson",
        "20261101213151_TickBoundsAndKeys",
        "20261102184731_VoteRevisionColumns",
        "20261102200000_PublishOnApprovalNotify",
        "20261102222348_BriefCheck",
        "20261103130424_RejectDecision",
        "20261103165049_ActionRevisionColumns",
        "20261103165431_PublicApiViews",
        "20261103165437_PublishedRecords",
        "20261103190221_ZctaDistricts",
        "20261103195249_ExecutiveActionRevisionColumns",
        "20261103195455_CodeVersionColumns",
        "20261103204606_PublicationWithdrawals",
        "20261103220605_JobAttemptColumns",
        "20261103234036_PublicApiVoteFacts",
        "20261104004000_PublishedEntries",
        "20261104010500_DocumentChangeRevisionColumns",
        "20261104011000_ActionRevisionDerivedColumns",
        "20261104012000_AttemptElapsed",
        "20261104013000_PublicApiZipDistricts",
        "20261104014100_RelationshipRevisionColumns",
        "20261104014500_StatementRevisionColumns",
        "20261104014600_BriefRevisionColumns",
        "20261104014700_PublicApiReleaseTime",
        "20261104135239_PublicApiBillViews",
        "20261104154215_ModelRunInput",
        "20261105005544_VoteQuestionResultColumns",
        "20261105012740_ActionRevisionTextColumns",
        "20261105141838_ReleaseNames",
        "20261105153622_ZipDistrictCongress",
        "20261105161253_DropVoteRevisionJson",
        "20261105171102_PeopleTables",
        "20261105174739_BillActionHistory",
        "20261105185300_BillLineColumn",
        "20261105225851_PublicApiMemberVotes",
        "20261105230132_DocumentChangeRunRevisions",
        "20261106100000_PublicApiOfficials",
        "20261106120000_PublicApiMemberVoteIssues",
        "20261106130000_PublicApiBillAndRecordPages",
        "20261106140000_PublicApiIssuesHome",
        "20261106144359_StoredPublicColumns",
        "20261106160000_PublicApiBillTextColumns",
        "20261106165552_ReleaseBillStatuses",
        "20261106184439_PublishedStatementApprover",
        "20261106233326_PublicApiPartyViews",
        "20261107020000_PublicApiVoteBillRecord",
        "20261107030000_ReleaseBillHistories",
        "20261107040000_PublicApiBriefContent",
        "20261107060000_PublicApiRecordLinkReason",
        "20261107080000_PublicApiBillLookup",
        "20261107160000_PublicApiWithdrawals",
        "20261107170000_PublicApiStatementLists",
        "20261107180000_PublicReadIndexes",
        "20261107210000_PublicApiOfficialPartyLines",
        "20261107220000_PublicApiCrsSummary",
        "20261107230000_PublicApiHomeIssueCongress",
        "20261108020000_PublicApiStatementDetail",
        "20261108053000_BillHistoryImports",
        "20261108053100_BillHistoryProjectionVersion",
        "20261108110000_DocumentComparisonDetails",
        "20261108130000_ExecutiveActionDispositionNotes",
        "20261108135000_PublicApiActiveReleaseIdentity",
        "20261108140000_PublicApiPartyAgreement",
        "20261109152248_PublicApiDocumentChangeDetail",
        "20261109182200_ReleaseHomeIssueCounts",
        "20261109203000_VoteMemberListTotals",
        "20261109213500_PublicApiVoteFirstPublications",
        "20261110031647_PublishedCorrectionHistories",
        "20261110052400_PublicApiCorrectionHistories",
        "20261110160000_VotePartyTallies",
        "20261110180000_PublicApiMemberVoteCompleteness",
        "20261110185000_PublicApiVotePartyTallies",
        "20261110190000_ReleaseBillCounts",
        "20261110210000_PublicApiVoteEvidence",
        "20261110230000_ExecutiveActionDispositionTargets",
        "20261111000000_ReleaseIssuePartyLineups",
        "20261111010000_PublicApiExecutiveActionTargets",
        "20261111020000_PublicApiReleaseBillCounts",
        "20261111030000_PublicApiVoteMemberDistricts",
        "20261111100000_PublicApiMemberVoteBreaksParty",
        "20261111100100_PublicApiStatementApprover",
        "20261111194004_ReleaseVoteBills",
        "20261111205421_ActionRevisionKeyTerms",
        "20261111213033_ExecutiveActionTexts",
        "20261112040000_DropPublicApiOfficialBillCounts",
        "20261112050000_PublicApiSearchSources",
        "20261112050100_PublicApiSearchEntries",
        "20261112051000_PersonApprovedStatementLabel",
        "20261112060000_PublishedEntryApprovalTime",
        "20261112120000_PublicApiBriefCoverage",
        "20261112150000_PublicApiBillSponsorPartiesAndTimes",
        "20261113020000_RelationshipApprover",
        "20261113060000_ExecutiveActionSummary",
        "20261113060100_PublicApiExecutiveActionSummary",
        "20261113110000_ReleaseBillStatusStage",
        "20261113120000_PublicApiStatementSpeakerTerms",
        "20261113202000_PublicApiExecutiveActionEvidence",
        "20261113210000_PublicApiBillTextQuotes",
        "20261114003000_PressReleaseStatementLabel",
        "20261114012000_PublicApiRelationships",
        "20261114033000_PublicApiMemberVoteTextOrigin",
        "20261114150000_PublicApiRelationshipFactTextOrigin",
        "20261114210000_PublicApiOfficialVoteParties",
        "20261114230000_CodeWrittenVotes",
        "20261114230000_PublicApiBillKeys",
        "20261114230000_VoteMemberListEntriesCoveringIndex",
        "20261114233000_ActionRevisionIntroducedOn",
        "20261114233000_ChamberVoteCodeLabel",
        "20261114233000_ShortStatementSourceText",
        "20261115000000_HideCutBillLines",
        "20261115004418_CorrectionTextRewrite",
        "20261115010148_OfficialSortNames",
        "20261115120000_PublicApiBillKeysLatestAction",
        "20261115183637_ChamberRecordHeadlineLabel",
        "20261116171634_ReleaseOfficialRecordCounts",
    ];

    private const string HistoryExistsSql = """SELECT to_regclass('"__EFMigrationsHistory"') IS NOT NULL AS "Value" """;
    private const string LockHistorySql = """LOCK TABLE "__EFMigrationsHistory" IN ACCESS EXCLUSIVE MODE""";
    private const string ReadHistorySql = """SELECT "MigrationId" AS "Value" FROM "__EFMigrationsHistory" ORDER BY 1""";
    private const string DeleteHistorySql = """DELETE FROM "__EFMigrationsHistory" """;
    private const string InsertBaselineSql = """INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES ({0}, {1})""";

    internal static async Task ApplyAsync(DbContext db, CancellationToken cancellationToken)
    {
        // A database that does not exist yet has no history. EF creates it and runs the baseline.
        if (!await db.GetService<IRelationalDatabaseCreator>().ExistsAsync(cancellationToken))
            return;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await db.Database.SqlQueryRaw<bool>(HistoryExistsSql).SingleAsync(cancellationToken))
            return;

        // A second migrate waits here, then reads the baseline row and leaves it.
        await db.Database.ExecuteSqlRawAsync(LockHistorySql, cancellationToken);
        var applied = await db.Database.SqlQueryRaw<string>(ReadHistorySql).ToArrayAsync(cancellationToken);
        if (applied.Length == 0 || applied.Contains(BaselineId, StringComparer.Ordinal))
            return;
        if (!applied.SequenceEqual(PreSquashIds, StringComparer.Ordinal))
            throw new UnknownMigrationHistoryException(UnknownHistoryMessage(applied));

        await db.Database.ExecuteSqlRawAsync(DeleteHistorySql, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(InsertBaselineSql, [BaselineId, ProductInfo.GetVersion()], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static string UnknownHistoryMessage(string[] applied) =>
        $"The migration history holds {applied.Length} migrations, and the newest is {applied[^1]}. " +
        $"Only the exact pre-squash history of {PreSquashIds.Count} migrations, ending with {PreSquashIds[^1]}, can move to the baseline {BaselineId}. " +
        "Deploy the last pre-squash version, run db migrate, then deploy this version again. Nothing was changed.";
}
