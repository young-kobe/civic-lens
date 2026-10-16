# Review

## Purpose

The review feature is the private editorial workspace. People draft, revise, and decide on proposed public records beside their evidence. Every decision binds to an exact immutable revision. This page covers document changes and the decision rules that statements and actions share. Statement-specific review is in [statements](statements.md#statement-review). Action-specific review is in [actions](actions.md#action-review). Brief-specific review is in [briefs](briefs.md#brief-review).

## Decisions

- One authorized operator may both draft and approve a record. A separate approver is not required, and self-approval is allowed.
- The owner can draft, review, publish, and manage editorial access. The additional reviewer can draft and review, but cannot publish or manage access.
- Each person signs in with a separate Auth0 identity, and each decision names its actor. Sign-in alone grants no editorial access.
- Authorization is enforced at mutation boundaries, not only by hiding UI controls.
- Server configuration maps exact subject IDs to roles. Client role claims confer no permissions.
- Access changes need a server configuration update and a restart. There is no management UI.
- Approval binds to the exact public wording and evidence versions reviewed. An edit to either needs a new review. Earlier approvals stay as history and do not authorize the new revision.
- Approval and publication are separate actions.
- Writes use expected versions, not editing locks or last-write-wins. The UI never silently merges editorial wording.
- Decisions are immutable. They cannot be edited or deleted.
- A saved revision never resolves a concern by itself. Only an explicit approval with a resolution note does.
- Shared Application handlers own these rules for the UI, the CLI, and future MCP callers.
- Document changes, statements, and actions share one decision policy, `ReviewPolicy`.
- A review cycle starts at the latest approval. Reads load the current revision, the last approved revision, and the decisions since that approval. A cycle has no revision cap. The open-concern cap bounds its decisions.
- One step appends a revision and computes `review_status` again. Person saves and analysis appends both use it.
- `review_status` and `has_changes` are stored columns that C# computes. SQL filters read them, so SQL keeps no copy of the policy.
- Autosave and live preview are not part of the first workflow.

## Invariants

Each line gives the rule, what enforces it, and the test that protects it. Cross-feature invariants are in [architecture](../architecture.md#invariants).

- A model author cannot decide. Enforced by `ReviewPolicy`. Test: `ReviewPolicyTests.TheAiDrafterCannotRecordAnyDecisionEvenOnItsOwnDraft`.
- A decision binds to its exact revision. Enforced by composite foreign keys from the decision tables to the revision tables. No test.
- One decision exists for each record and review state version. Enforced by unique indexes on both decision tables. Test: `PostgresStatementReviewStoreTests.DecisionReplayReturnsTheOriginalDecisionAndAStaleStateConflicts`.
- Expected versions are checked under the review lock. Enforced by `ReviewWriteRules`. Test: `PostgresDocumentChangeReviewStoreTests.CreateReplayIsStableAndConcurrentSavesAppendOnlyOneCurrentRevision`.
- A record has at most 64 open concerns, so a review cycle holds at most 65 decisions: its opening approval and the concerns after it. Only an approval resolves a concern, and the next approval opens a new cycle. The cycle read takes `ReviewPolicy.MaximumDecisionsPerCycle` rows for each record. Enforced by `ReviewPolicy`. Tests: `ReviewPolicyTests.ACycleHoldsItsApprovalAndAtMostTheOpenConcernCapBecauseOnlyTheNextApprovalResolvesAConcern` and `PostgresDocumentChangeReviewStoreTests.ReadsLoadOneReviewCycleSoARecordWithMoreDecisionsInTotalThanACycleHoldsStillReadsAndAcceptsDecisions`.
- A batched read finds the cycle start of each record on its own. Enforced by `ReviewRecords`. Test: `PostgresDocumentChangeReviewStoreTests.AListReadsEachRecordFromItsOwnCycleStartSoStatusesAndConcernsStayApartInOneBatch`.
- A read replays every decision of the current cycle against the policy. Enforced by `ReviewDecisionHistory`. Test: `PostgresDocumentChangeReviewStoreTests.ReadsReplayEveryDecisionSinceTheLatestApprovalSoATamperedDecisionInTheCycleFailsTheRead`.
- A reused idempotency key with another payload fails. Enforced by `ReviewIdempotency`. Test: `PostgresDocumentChangeReviewStoreTests.DecisionReplayReturnsOriginalDecisionAndRejectsPayloadReuse`.
- The stored status equals the policy status. Enforced by `ReviewPolicy`, `ReviewRecords.AppendRevision`, and the status check constraints. Tests: `PostgresDocumentChangeReviewStoreTests.StoredStatusEqualsThePolicyStatusAfterEveryKindOfWrite` and `PostgresBillStreamTests.AWorkerRevisionAfterAnApprovalRecomputesTheStoredStatusSoTheRecordNeedsReviewAgain`.
- An approval resolves every open concern. Enforced by `ReviewPolicy`. Test: `ReviewPolicyTests.RequestChangesCarriesAcrossRevisionAndApprovalMustResolveExactConcern`.
- A withdrawal needs a current approval. Enforced by `ReviewPolicy`. Test: `ReviewPolicyTests.WithdrawalRequiresCurrentApprovalAndCreatesAConcernForReapproval`.

## Behavior

### What reviewers check

The workspace centers the proposed public record beside its supporting evidence. Document history and comparisons support the investigation. Reviewers assess wording, quotation fidelity, attribution, dates, context, citations, and interpretation.

- For a document change, reviewers separate a substantive source edit from navigation churn or a processing change.
- For a relationship, reviewers verify the claimed connection.
- For a brief, reviewers verify each assertion against the cited revision and read the coverage description. A brief shows a flag when a cited record changed.

An explanation of significance is an editorial judgment that needs review. A text difference never makes it automatic. A comparison alone does not show substantive significance.

### Drafts

A document-change draft starts from an explicitly selected, complete saved comparison with an actual text change. There are three paths:

- The worker writes an AI draft of each complete text change; see [analysis](analysis.md).
- An operator explicitly creates a draft from a saved comparison.
- A reviewer starts a draft by hand.

A change has at most one draft. Starting a draft for a change that already has one opens the existing draft.

The public record holds:

- a headline and a plain-language summary
- optional evidence-supported significance
- limits and uncertainty
- the source institution and supported official links
- configured issue categories
- bindings to the exact comparison, the before and after versions, and the cited passages

The earlier and later observation dates stay distinct from the actual change date. The record includes a change date only when evidence supports it.

### Revisions

Each explicit save keeps an immutable revision of the wording, attribution, issue selection, and evidence bindings. Each record has one current revision. Incomplete drafts can be saved. Approval of a document change needs a headline, a summary, an institution, at least one valid citation, and explicit resolution of all outstanding concerns. An actual change date needs a selected exact citation.

Editing an approved revision creates a new unapproved revision. The historical approval and any published record do not change. The UI warns before a person leaves unsaved edits.

Review shows a revision beside the supporting before and after passages. Technical identifiers stay in the evidence details. The approved HTML mockup is the interaction and layout reference.

Initial citations select positive ranges from the first 32 comparison hunks. The editor offers all hunks for explicit selection within the citation limit.

### Review decisions

A saved revision starts awaiting review. A decision targets an exact revision and keeps the authenticated actor.

| Decision | Effect and requirements |
|---|---|
| Approve | Marks the revision suitable for publication after checking wording, attribution, dates, context, citation fidelity, and substantive significance. One authorized approval suffices. |
| Request changes | Blocks publication until explicitly resolved. Needs a note that describes the needed corrections. |
| Withdraw approval | Invalidates the effective approval and blocks future publication. Needs a reason. |

Either reviewer can raise a blocking concern. A later approval must acknowledge the outstanding concerns and include a resolution note. It records the exact outstanding decision IDs it resolves. The original decisions keep their original revision bindings. Unresolved concerns carry forward as publication blockers when a new revision is saved.

### Status states

The check constraints on `document_change_drafts`, `statement_drafts`, and `action_drafts` list the stored statuses. Statements add NotDrafted. `ReviewPolicy.GetCurrentStatus` computes it after each write: the latest decision on the current revision sets it, and without one it is awaiting review. `ReviewPolicy.GetInitialStatus` gives the status of a new draft row, which has no decisions.

| From | To | Write |
|---|---|---|
| NotDrafted | AwaitingReview | The first revision, by a person or by analysis |
| Any status | AwaitingReview or AwaitingReviewWithConcerns | A saved or appended revision, with or without open concerns |
| Any drafted status | Approved | Approve, which must resolve every open concern |
| Any drafted status | ChangesRequested | Request changes |
| Approved | ApprovalWithdrawn | Withdraw approval |

### Review cycles

A review cycle starts at the latest approval of a record. A record with no approval has one cycle that starts at its first decision. An approval resolves every open concern, so the decisions before it never change the status or the open concerns.

- Reads load the current revision, the last approved revision, and the decisions of the cycle. The AI draft outcome list loads revision 1 through `ReviewRecords.ReadFirstRevisionsAsync` for the model draft. The statement read loads it too, but uses only the run of revision 1. No read loads other revisions or older decisions.
- A read checks the approval at the start of the cycle against its revision, and replays every later decision against the policy.
- The history panel shows the current and the last approved revisions, and the decisions of the cycle. `ReviewHistory.LoadedRevisions` builds that revision list, and each review aggregate carries it as `Revisions`.
- A cycle has no revision cap. The decisions of a cycle are bounded by the open-concern cap; see [Invariants](#invariants).

### Concurrency and replay

- A draft save checks the expected current revision atomically.
- A stale save returns a conflict. It keeps the unsaved text of the operator for comparison with the latest revision.
- A review decision checks the expected current revision and the review-state version atomically. A changed revision or an intervening decision needs a refresh before the person decides.
- An idempotent retry of the same successful request returns the original outcome. It creates no duplicate revision or decision.
- Keys are scoped to the authenticated actor and operation and bound to the request payload. Reuse with a conflicting payload is rejected.
- Authorization is checked before a replayed or a new outcome returns.

### Lists and paging

The draft list, the new-change list, and the recent collection checks use keyset paging. A request holds one opaque cursor with a direction and a (time, id) key. Each page lists the newest rows first. It returns a newer cursor and an older cursor, and a cursor is null at that end.

| List | Order |
|---|---|
| Drafts | Creation time, then draft ID |
| New changes | Observation time of the later version, then comparison ID |
| Checks | Creation time, then job ID |

A new change is a complete comparison with at least one text hunk and no draft. The draft list can filter on status in SQL. Approved means that the newest decision on the current revision approves it. Needs action means every other status. The overview counts reuse the same status filter in `ReviewRecords.Drafts`, so each count equals the total of its list.

The Review home page lists new changes, drafts, source health, and recent activity. The owner also sees the latest root job of each configured source; see [collection](collection.md#sources-page). The recent activity feed reads the newest events of each kind from the review tables. For the owner only, it also reads the job and processing tables. Application merges the events by time.

### Workspace forms

The editor shows the saved account and its exact selected passages as the approval target. It keeps them apart from editable or recovered unsaved wording. Reviewers judge substantive change, wording, attribution, dates, interpretation, and evidence limits. Automatic citation and revision checks establish integrity, not semantic correctness.

The workspace keeps conflicting edits. It disables decisions for unsaved, failed, or conflicting forms. Every mutation uses an antiforgery token. The reader preview updates on the server render after an explicit save.

### Evidence verification

The New changes list is the one read that does not verify full evidence. One query reads each stored comparison result, source, requested URL, and observation time into `NewDocumentChange`. That type holds the passage count and the first passage. The query loads no extraction text and does not compute the comparison again, so a page of 100 rows stays cheap. A list row is only a pointer.

Saving a comparison, the single review read, starting a draft, approval, and publication all verify the full evidence. A read verifies each comparison and its two extractions once and reuses them for every revision.

### Authentication

The Host uses `Microsoft.AspNetCore.Authentication.OpenIdConnect` 10.0.12 for the Auth0 authorization-code flow with PKCE. The CLI acts as the configured owner subject. The authenticated `review` command binds to loopback and expects an HTTPS reverse proxy; see [host](host.md).

## Storage and contracts

- The generated [review schema](schema/review.md) lists every table, column, key, check, index, and trigger.
- Core/Review owns decision semantics. Application/Review owns authenticated commands and configured selection validation. Infrastructure/Review implements the store boundary with Postgres.
- `ReviewPolicy` holds the status rule, the open-concern rule, and the decision checks. It works on a record ID, the current revision number, the review state version, and the decisions. It knows no record type.
- Review writes take the review keys from `PostgresAdvisoryLock`. The keys and their order are in [advisory locks](../architecture.md#advisory-locks).
- `ReviewRecords` is the shared store core for each record type. Each store holds one instance over its draft, revision, and decision rows, its lock key, and its record ID property. It owns the create, save, and decide flow, the append step, the status filters, the draft page, and the unpublished approved list. Its bounded batch reads parse and validate one review cycle for each record into a `ReviewHistory`. Its batched revision 1 read parses and validates the shape of each revision 1. A store gives only its feature rules as delegates. They are the create body, the save preparation, the revision row builder, the decision checks, and the decision row builder. Actions and briefs also give a delegate that records the revision time on the draft row. Each store can give a callback for a withdrawn approval, which requeues the briefs that cite the record; see [briefs](briefs.md#staleness). The rows implement `IReviewDraftRow`, `IReviewRevisionRow`, and `IReviewDecisionRow`, and the revisions implement `IReviewRevision`. `IReviewRevision` is a deliberate departure from the concrete-types rule and the first interface in Core. Core cannot see Infrastructure, and the shared core needs it as its generic constraint.
- `ReviewRecords.AppendRevision` is the one step that appends a revision and computes `review_status` again. The person save and the action and brief analysis appends call it. Document-change and statement analysis drafts only create revision 1, so they use `ReviewPolicy.GetInitialStatus`.
- `ReviewIdempotency` reads and writes replay receipts and checks the actor, key, and payload hash. `ReviewDecisionHistory` parses and replays the stored decisions of one review cycle. `IReviewDecisionRow` exposes the decision kind, so the cycle read finds the latest approval in SQL. `ReviewKeyset` builds the keyset page filter. `ReviewJson` holds the stored JSON options.
- Four tables hold record pointers, immutable revision JSON, immutable decision JSON, and actor, operation, and request-key replay receipts.
- Draft and decision expected versions are checked inside the locked transaction.
- Each draft row stores `review_status`, which `ReviewPolicy` computes in C#. Create, save, the action analysis append, and every decision write it under the lock. A check constraint lists the five status names. `ReviewStatus` adds `NotDrafted` for a record with no revision, which a document change never has.
- The migration `StoreReviewStatusAndHasChanges` filled `review_status` of existing document-change drafts from their stored decision JSON, and `has_changes` from the stored comparison result.
- Each comparison row stores `has_changes`, which is `DocumentComparison.HasChanges()`: a complete comparison with at least one hunk.
- Partial indexes on `has_changes` and on the Approved and not-Approved statuses serve the filters, the New changes list, and the overview counts.
- The list of unpublished approved drafts reads the active release records. It stays a read query on top of `review_status`.
- Revisions keep the author and the UTC save time. Decisions reference the exact revision with a composite foreign key.
- Reads validate stored identities, historical transitions, and exact retained evidence.
- Bounds: 64 open concerns, which also bound the decisions of a cycle (see [Invariants](#invariants)), 64 citations for each revision, and bounded field sizes. Writes fail before they exceed a bound.
- The public release contract includes review and publication dates and correction context. It excludes author identity, reviewer email, Auth0 identifiers, and private notes; see [publication](publication.md).

## Code map

| Concern | Files |
|---|---|
| Decision policy and status | `src/CivicLens.Core/Review/ReviewPolicy.cs`, `src/CivicLens.Core/Review/ReviewStatus.cs`, `src/CivicLens.Core/Review/ReviewDecision.cs`, `src/CivicLens.Core/Review/AiProposedDate.cs` |
| Document-change revision | `src/CivicLens.Core/Review/DocumentChangeDraftRevision.cs`, `src/CivicLens.Core/Review/DocumentChangeReview.cs` |
| Commands | `src/CivicLens.Application/Review/CreateDocumentChangeDraft.cs`, `src/CivicLens.Application/Review/SaveDocumentChangeDraft.cs`, `src/CivicLens.Application/Review/DecideDocumentChangeReview.cs` |
| Reads | `src/CivicLens.Application/Review/GetDocumentChangeReview.cs`, `src/CivicLens.Application/Review/ListDocumentChangeReviews.cs`, `src/CivicLens.Application/Review/ListEligibleDocumentComparisons.cs`, `src/CivicLens.Application/Review/GetReviewOverview.cs`, `src/CivicLens.Application/Activity/GetRecentActivity.cs` |
| Validation and authorization | `src/CivicLens.Application/Review/ReviewValidation.cs` |
| Paging | `src/CivicLens.Application/Paging/PageCursor.cs`, `src/CivicLens.Application/Paging/KeysetSlice.cs`, `src/CivicLens.Application/Paging/PageLimit.cs` |
| Shared store core | `src/CivicLens.Infrastructure/Review/ReviewRecords.cs`, `src/CivicLens.Infrastructure/Review/ReviewHistory.cs`, `src/CivicLens.Core/Review/IReviewRevision.cs`, `src/CivicLens.Infrastructure/Review/Persistence/IReviewDraftRow.cs`, `src/CivicLens.Infrastructure/Review/Persistence/IReviewRevisionRow.cs`, `src/CivicLens.Infrastructure/Review/Persistence/IReviewDecisionRow.cs` |
| Postgres store and helpers | `src/CivicLens.Infrastructure/Review/PostgresDocumentChangeReviewStore.cs`, `src/CivicLens.Infrastructure/Review/ReviewIdempotency.cs`, `src/CivicLens.Infrastructure/Review/ReviewDecisionHistory.cs`, `src/CivicLens.Infrastructure/Review/ReviewKeyset.cs`, `src/CivicLens.Infrastructure/Review/ReviewJson.cs`, `src/CivicLens.Infrastructure/Review/ReviewQueries.cs` |
| Pages | `src/CivicLens.Host/Review/ReviewHome.razor`, `src/CivicLens.Host/Review/DraftEditor.razor`, `src/CivicLens.Host/Review/ReviewDecisionPanel.razor`, `src/CivicLens.Host/Review/ReviewHistoryPanel.razor`, `src/CivicLens.Host/Review/ReviewRecordPage.cs` |
| Brief review | `src/CivicLens.Host/Review/BriefList.razor`, `src/CivicLens.Host/Review/BriefEditor.razor`, `src/CivicLens.Infrastructure/Briefs/PostgresBriefReviewStore.cs`; see [briefs](briefs.md#code-map) |
| Workspace settings | `src/CivicLens.Host/Review/ReviewWorkspace.cs`, `src/CivicLens.Host/Review/ReviewWorkspaceSettings.cs` |

## Limits and planned work

- Live Auth0 tenant verification is pending. Both reviewers will reach the shared production workspace on Hetzner through their browsers; see [host](host.md#limits-and-planned-work).
- Reads show the current review cycle only. A page of older cycles is future work.
- A read trusts the approval at the start of a cycle and does not check it against older concerns again. The decide step checked it under the lock when it was written.
- Local MCP will inspect jobs, explain processing, inspect source health, compare analysis runs, and run explicitly budgeted evaluations.
- MCP review tools will list and read cases, propose corrections, and record explicit decisions. Mutations will use expected versions and idempotency keys. Initial local tooling is read-only.
- AI suggestions, model predictions, and human decisions stay distinct records.
- Planned reviewer experience: a source-to-review path that a nontechnical reviewer understands, dynamic search across retained evidence and review records, and integrated visual comparison.
- Global search and expanded reviewer source visibility are pending decisions. Add an evidence library page once document search exists.
- Review volume is large. Bulk approval exists for action revisions that change only sponsorships; see [actions](actions.md#action-review). Wider bulk tools are planned here. They will show the automatic citation and classification checks beside each draft, and sort the riskiest drafts first.
- A reviewer approves a weekly brief on its own. A brief cites records that are already published, and each cited record keeps its own decision bound to its exact revision. Approval needs every cited revision to be approved and in the active release. Model output never approves or publishes. See [briefs](briefs.md#limits-and-planned-work).
- See [roadmap](../roadmap.md) for order.
