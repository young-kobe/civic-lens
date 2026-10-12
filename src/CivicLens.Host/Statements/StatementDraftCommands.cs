using CivicLens.Application.Statements;

namespace CivicLens.Host.Statements;

internal sealed record StatementDraftCommands(GetStatementDraftStatus Status, ListStatementDrafts List, GetStatementDraftRun Run,
    RequeueStatementDrafts Requeue);
