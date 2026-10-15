# Publication

## Purpose

The publication feature turns approved revisions into immutable, cumulative static releases. It activates a release atomically. A separate static file server reads only published files.

## Decisions

- Only the owner publishes or activates a release.
- A record is published at its current revision, and only when that revision is approved and has no unresolved concerns.
- A newer draft never replaces published content automatically.
- A release is cumulative. It holds the base release records plus explicit additions or replacements.
- Carried records are hard links to the immutable files of the base release. Disk therefore grows with published records, not with the number of releases.
- Each publish rebuilds only `release.json`, the index pages, and the assets. It reads the previous manifest once, under the 16 MB read cap. `PublicationProtocol.MaximumRecordsPerRelease` is the one owner of the record limit, which is 20,000. The database has no count check, because C# owns the rule. At the longest allowed headline, one entry takes about 440 bytes, so 20,000 entries take about 8.8 MB.
- Postgres records which release is active. The `current` link is only a copy for the static server, so a failed link swap never drops committed records.
- Every publication write takes all three review locks in one fixed order, which prevents a deadlock. The order is in [advisory locks](../architecture.md#advisory-locks).
- A release has no status column. `is_active` marks the one active release: a commit sets it on the new row and clears the old one, and rollback moves it.
- Rollback is a CLI command only. The workspace has no rollback or activate control.
- All releases are kept until a retention policy is agreed.
- Host renders the shared Razor components to static HTML, so publication needs no new project.
- Each manifest entry carries search filters, so search needs no record file reads. Each published record type owns the mapping in its `ToEntry` method. A filter that the kind has no fact for stays empty; publish never guesses it.
- Unreviewed proposals, internal reviewer notes, operational job details, and mutation controls stay private.
- Public serving reads release contracts only. It never reads operational storage or private notes.

## Invariants

Each line gives the rule, what enforces it, and the test that protects it. Cross-feature invariants are in [architecture](../architecture.md#invariants).

- Only an approved record with no open concern publishes. Enforced by `PublishedRecordRules`. Test: `PublishReleaseTests.RejectsDraftsThatAreNotCleanlyApproved`.
- A release holds at most `PublicationProtocol.MaximumRecordsPerRelease` records, and the manifest stays under the read cap at that count. Enforced by `PublishRelease` and `PublicationRelease.Validate`. Test: `PublishReleaseTests.RejectsMoreRecordsThanTheCapBeforeAnythingIsBuilt` and `PublishReleaseTests.AReleaseAtTheRecordLimitWithLongestHeadlinesStaysWellUnderTheManifestReadCap`.
- Manifest filters hold at most `PublishedRecordEntry.MaximumFilterValues` distinct, nonblank values, each at most `PublishedRecordEntry.MaximumFilterValueLength` characters. Enforced by `PublishedRecordEntry.Validate`, which `PublicationRelease.Validate` calls. Test: `PublicationReleaseTests.FiltersAreBoundedAndDistinctSoOneEntryCannotSwellTheManifest`.
- An entry's filters come from the revision that the release publishes. Enforced by `PublishedDocumentChange.ToEntry` and `PublishedStatement.ToEntry`. Test: `PublishStatementTests.EachKindFillsItsSearchFiltersFromTheRevisionItPublishes` and `PublishStatementTests.ARepublishedStatementReplacesItsCarriedEntryInsteadOfDuplicatingIt`.
- A commit holds the exact revision and review state version that the build read. Enforced by `PostgresPublicationStore`. Test: `PostgresPublicationStoreTests.ADecisionAfterTheBuildBlocksTheCommitBecauseApprovalMayHaveChanged`.
- One release is active. Enforced by `IX_publication_releases_is_active`. Test: `PostgresPublicationStoreTests.EachCommitBecomesTheActiveReleaseSoAFailedLinkSwapCannotHideIt`.
- Release numbers have no gaps. Enforced by `PostgresPublicationStore`. Test: `PostgresPublicationStoreTests.ReleaseNumbersMustFollowTheLatestCommittedOneWithoutGaps`.
- A replay with another payload fails. Enforced by `PostgresPublicationStore`. Test: `PostgresPublicationStoreTests.ReplayReturnsTheOriginalOutcomeAndRejectsADifferentPayloadForTheSameKey`.
- A published release serves without the database. Enforced by the static release files. Test: `PublishedReleaseOutlivesDatabaseTests.PublishedReleaseServesFromFilesAloneAfterTheDatabaseIsGone`.

## Behavior

### Public reader experience

A record explains what happened, who was involved, and when. Where evidence supports it, it explains why the event matters. It states what the evidence can and cannot establish. Readers move from the account into retained passages and exact citations. They do not need to read collection jobs or extraction metadata first.

A document-change record shows both versions, and observation history where needed to explain gaps. A current source link supplements the retained evidence; it does not replace it. Public records show their review and version context and published corrections.

### Release content

- Each document-change record carries the full extracted text of both versions, their source URLs, and their observation times. Changed regions are offsets into those texts.
- A citation holds an extraction ID, UTF-16 offsets, and the quoted text. The contract rejects a quote that does not match its offsets.
- Records show the approval date and the first-publication date. The release index shows the release date.
- A release holds at most 20,000 records.
- The index lists 50 records on each page, newest first. The pages are `index.html`, then `page-2.html` and so on, with relative previous and next links.
- Officials appear by their configured name. Issues appear by their configured identifier.
- Only the release manifest shows the schema version.

A release holds two kinds of record. Each manifest entry has a `kind`: `documentChange` or `statement`, a lowercase camel string. Both kinds share the `records/` folder, the record data path, and the record page path. A manifest entry is unique by kind and ID.

| Kind | Record ID |
|---|---|
| `documentChange` | The draft ID: 32 lowercase hex characters |
| `statement` | The candidate ID: 64 characters |

A statement record carries one source version with the full extracted text, the source URL, and the observation time. It also carries:

- speakers by configured name, and other speakers
- issues, policy areas, and bill numbers
- the optional statement date with its evidence
- 1 to 32 quotations, each with its kind: `directQuotation` or `releaseText`

Every quotation must equal the source text at its offsets.

The approval time of a record is the latest approval of its current revision, for both kinds. One Application rule owns that time.

### Publish

One publish request names 1 to 64 records, each as a kind and an ID. Publication loads all selected drafts and their verified evidence in one repeatable-read transaction. That read uses seven queries for any selection of 1 to 64 drafts. The publisher builds records from this evidence and does not read it again.

1. Application builds the release contract, and Host renders the static HTML.
2. The owner action builds the release in a staging directory, verifies it, and moves it to a uniquely named directory under `releases/`.
3. Under all three review locks, the commit checks each added record's revision and review-state version again, in the draft table of its kind. It also checks the next release number and the active release it built on.
4. In the same transaction, it records a sequentially numbered release and marks it as active.
5. After the commit, it takes all three review locks again, reads the active release, and atomically replaces the relative `current` symbolic link.

Publish, replay, and activate all end with step 5. An older command therefore cannot move the link back after a newer one.

The review state is checked at commit, so a concurrent blocking decision cannot be ignored. A commit that fails on a conflict or invalid input deletes its directory. If that cleanup fails, or an unknown failure occurs, the directory stays. Only committed releases can be activated.

Each publication builds on the recorded active release, not on the link. A replayed publish request and the activate command both point the link at the recorded active release. This repairs a link that is out of date. Publish requests reuse the review replay receipts.

### Rollback

Rollback records an earlier release as active and then points the link at it. The next publication builds on the rolled-back release, so it does not restore the records that were rolled back. A publication built before a rollback fails on commit.

### Owner publish section

The Review home page shows a publish section to the owner only, and the handlers also enforce the owner role. The ready-to-publish read lists approved document changes and statements that the active release does not hold at their current revision. A record is a replacement when the active release holds an older revision. Otherwise it is new.

SQL selects a bounded candidate set for each kind from the review and active-release tables. Application then applies the one publishability rule in `PublishedRecordRules`, which the publish handler also uses. Each kind gets up to half of the publish limit, and the other kind fills any unused share.

The owner selects records with checkboxes. A summary step lists each headline, its kind, and the next release number. Only the confirm form calls the publish handler, with one idempotency key for each page render. The page lists recent releases read-only.

### Corrections and withdrawal

Withdrawing approval does not change published artifacts. The UI flags affected published records for an explicit correction or withdrawal release. A correction needs a new revision, an approval, a public correction note, and a new release.

### Rendering

The release builder renders public components to static HTML through `PublicPage`, `PublishedIndexPage`, and `PublishedRecordPage`. A release copies `assets/css/theme.css`, `assets/css/app.css`, `assets/js/theme.js`, `assets/js/app.js`, and the fonts. Pages also work without JavaScript. Shared components are described in [host](host.md#presentation).

## Storage and contracts

- The generated [publication schema](schema/publication.md) lists every table, column, key, check, index, and trigger.
- Each release row lists its records as kind, record ID, and revision. A row written before kinds existed has no kind and counts as a document change.
- The ready lists read the active row and match by kind, so a statement ID cannot hide a document change.
- Schema version 2 adds the entry kind. Readers accept versions 1 to 3. A version 1 entry has no kind and means a document change. A version 1 release on disk can therefore still be the base of the next release.
- Schema version 3 adds the entry filters. An entry from version 1 or 2 has empty filters, and publish does not backfill them.

| Filter | Document change | Statement | Planned action |
|---|---|---|---|
| `officials` (ID and configured name) | Officials | Speakers | Sponsor officials |
| `issueIds` | Issue IDs | Issue IDs | Issue IDs |
| `policyAreaIds` | Empty | Policy area IDs | Policy area |
| `sourceHost` | Host of the after version URL | Host of the source URL | Host of the bill source URL |
| `eventDate` | Evidenced change date, or empty | Evidenced statement date, or empty | Latest action date |

- New manifests use version 3. `src/CivicLens.Publication.Contracts/PublicationProtocol.cs` owns the version; see [Behavior versions](../architecture.md#behavior-versions).
- A separate static file server serves the release root read-only. It has no access to operational storage.

## Code map

| Concern | Files |
|---|---|
| Release contract | `src/CivicLens.Publication.Contracts/PublicationRelease.cs`, `src/CivicLens.Publication.Contracts/PublishedRecordEntry.cs`, `src/CivicLens.Publication.Contracts/PublishedDocumentChange.cs`, `src/CivicLens.Publication.Contracts/PublishedStatement.cs`, `src/CivicLens.Publication.Contracts/PublishedEvidenceRules.cs` |
| Publish, list, activate | `src/CivicLens.Application/Publication/PublishRelease.cs`, `src/CivicLens.Application/Publication/ListPublishable.cs`, `src/CivicLens.Application/Publication/ActivatePublicationRelease.cs`, `src/CivicLens.Application/Publication/ListPublicationReleases.cs` |
| Record rules and builders | `src/CivicLens.Application/Publication/PublishedRecordRules.cs`, `src/CivicLens.Application/Publication/PublishedRecordBuilder.cs`, `src/CivicLens.Application/Publication/PublishedStatementBuilder.cs` |
| Postgres and files | `src/CivicLens.Infrastructure/Publication/PostgresPublicationStore.cs`, `src/CivicLens.Infrastructure/Publication/FileReleaseStaging.cs`, `src/CivicLens.Infrastructure/Publication/FileReleaseDirectory.cs`, `src/CivicLens.Infrastructure/Publication/ReleasePaths.cs` |
| Rendering and CLI | `src/CivicLens.Host/Publication/ReleaseRenderer.cs`, `src/CivicLens.Host/Publication/PublicPage.razor`, `src/CivicLens.Host/Publication/PublishedIndexPage.razor`, `src/CivicLens.Host/Publication/PublishedRecordPage.razor`, `src/CivicLens.Host/Publication/ReleaseCommand.cs` |
| Owner publish section | `src/CivicLens.Host/Review/ReviewHome.Publication.cs` |

Release and rollback commands are in [operations](../operations.md#publish-a-static-release). Backup, retention, and monitoring rules are in [Limits and planned work](#limits-and-planned-work).

## Limits and planned work

- Correction and withdrawal releases come later.
- How the static file server runs locally is not decided.
- Issue, official, and briefing pages are planned. Readers will start with a weekly briefing and recent reviewed developments. Issue pages will connect related records across officials and institutions. Official pages will show dated statements, actions, and reviewed relationships.
- Public search and filtering, timelines, expandable evidence, and shareable records are planned against published releases.
- Public search is an MVP requirement. Readers search all published records from the public pages. Search reads the published release only, so it works while the operational application is down.
- Search filters: record kind, official, issue, policy area, date range, and source. Filters are easy to combine and easy to clear. The page works at phone width.
- Results show the kind, headline, officials, issues, and dates of each record, and link to the record page.
- Planned, not built: the public pages search the manifest entry filters client side. Whether they read `release.json` or a separate compact search file is not decided.
- Later, after the first MVP deploy: a public search service with its own Postgres database, fed from releases. It gives full-text search over the whole processed corpus.
- Revisit the record limit at about 20,000 records or a manifest over 4 MB. Then split the manifest by kind.
- The manifest read cap holds at 20,000 records for headlines that are plain ASCII text. A 256-character headline of non-ASCII characters takes about 1.7 KB in the escaped manifest, and a manifest full of those would pass the cap.
- A future live public Blazor deployment must read published releases only and stay separate from the operational application.
- A future mobile client would read public release contracts through an API, not operational storage.
- Public MCP will expose `search_records`, `get_record`, `get_official_timeline`, and `compare_document_versions`. It returns published data with IDs, citations, dates, review status, coverage caveats, and the release ID.
- Public MCP cannot start collection, inference, or edits. It bounds and pages responses, limits requests, and accepts record IDs, not arbitrary fetch URLs. It treats source passages as untrusted attributed content.
- Releases will also carry a bounded search index, built from the same explicit approved selection. The website and public MCP will share one release, and clients can pin it across calls.
- Public MCP needs request compute, but never the local database or AI inference.
- Local Postgres and capture files need coordinated backups and a tested restore. Production backup rules are in [host](host.md#limits-and-planned-work).
- Retention must keep the evidence that published releases reference. Disposable captures follow an explicit policy, which is not agreed yet.
- Monitoring must cover freshness, skipped and deferred work, source failures, storage, and spend.
- See [roadmap](../roadmap.md) for order.

Public MCP and operations acceptance:

- [ ] Public tools never access operational data, change records, crawl, or call models.
- [ ] The website and MCP use the same release.
- [ ] Show restore, rollback, release-pinned reads, pagination, bounded traffic behavior, and visible coverage gaps.
