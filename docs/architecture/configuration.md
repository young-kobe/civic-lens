# Configuration

## Purpose

Configuration defines the people, sources, coverage, profiles, and tracked issues. Version-controlled files are authoritative. Each validated file becomes an immutable, hash-identified revision that jobs and analysis runs keep.

## Decisions

- Officials, issues, and sources are configuration, never code branches.
- Issues use two layers. The 32 Congress.gov policy areas are the backbone: they link statements to bills, which carry one policy area each. A small configured list of tracked issues drives the gate and the issue pages.
- Coverage means who we monitor a source for. It establishes neither authorship nor officeholding.
- Missing date bounds impose no eligibility limit. They do not establish historical ownership.
- An as-of date selects collection eligibility only. It does not select historical content or attribute evidence.
- A revision ID is the hash of the canonical snapshot. Reordering arrays therefore creates a new revision.
- Validation runs once, where the configuration loads or the revision is built. Later request and coverage methods do not validate again.
- A configuration change creates a new revision. It never rewrites an admitted job.
- Registry data never enters collector requests.
- An institutional account is not an alias of a person.
- Issue definitions are the future classification rubric, so a wording change creates a new revision.
- Offices, terms, and institutional ownership come with the features that use them.

## Invariants

Each line gives the rule, what enforces it, and the test that protects it. Cross-feature invariants are in [architecture](../architecture.md#invariants).

- Only configuration version 2 loads. Enforced by `CollectionConfiguration`. Test: `ConfigurationTests.OnlyConfigurationVersionTwoIsSupported`.
- A revision ID is the hash of its canonical snapshot. Enforced by `ConfigurationSnapshotRules`. Test: `CollectionConfigurationRevisionTests.ConstructorRejectsHashMismatchMalformedJsonAndInvalidConfiguration`.
- Two people cannot share one bioguide ID. Enforced by `CollectionConfiguration`. Test: `ConfigurationTests.TwoPeopleCannotShareOneBioguideIdBecauseItNamesOneMember`.
- Coverage intervals of one person on one source do not overlap. Enforced by `CollectionConfiguration`. Test: `ConfigurationTests.VersionTwoRejectsOverlappingCoverageButAllowsAdjacentIntervalsAndDifferentPeople`.
- A tracked issue links to a known policy area. Enforced by `IssueConfiguration`. Test: `IssueConfigurationRevisionTests.IssueMustLinkToKnownPolicyAreaSoItCanReachBills`.

## Behavior

### People and sources

Version 2 is the only supported collection configuration version. Each source has nonempty `coverage` entries. An entry has a `personId`, an optional `startsOn`, and an optional `endsBefore`. The wire contract rejects the older `personIds` field.

A person has a stable `id` and a current display `name`. Optional dated `names` use the same date fields. A person can also carry a `bioguideId`, the Congress.gov member identifier: one uppercase letter and six digits. Two people cannot share one. Snapshots leave out an absent `bioguideId`, so revision IDs without it do not change.

`config/federal-coverage.json` lists all 539 current members of Congress, delegates included, each with a bioguide ID. It has 1,392 member sources and the `congress-bills` source. Persistent pages are checked every 6 hours, and feeds and listings every hour.

- McConnell has no web sources, because the robots.txt of his site disallows all paths.
- Britt and Kim have no press source, because their press listings refuse our requests.
- The House Drupal `rss.xml` feeds stay unused. A sample showed site-wide content, at most 10 items, and no overlap with the press-release listings.

One source type, the Congress.gov bill list, has no coverage list; see [bills](bills.md#source-configuration).

### Dates and intervals

Dates are calendar dates with inclusive starts and exclusive ends. Application validates registry references and intervals, also on disabled sources.

- Adjacent intervals are allowed.
- Overlapping intervals for the same person and source are rejected.
- Overlapping intervals for the same exact name and person are rejected.
- Different people can share overlapping source coverage, and distinct aliases can overlap.
- IDs and name comparisons are ordinal and case-sensitive.

A shared source is collected once.

### Eligibility

`GetCoveredPersonIds` answers coverage on an explicit date. It includes configured relationships on disabled sources. Enabling a source is a separate execution gate. New collection or admission needs an enabled source with active coverage.

`--as-of yyyy-MM-dd` gives an explicit date. Direct collection sets an omitted date once to the current UTC date. Managed admission keeps an omitted date unresolved until the existing key is checked in its database transaction. Publisher, event, and observation times keep their separate meanings.

Ending coverage or disabling a source stops new admissions for the affected dates. Already admitted jobs still run from their snapshot, including retries. Cancel them explicitly to stop them. Keep stable identities when coverage ends.

### Revisions

`CollectionConfigurationRevision` keeps a validated JSON snapshot, the configuration parsed from it, and its lowercase SHA-256 ID. The snapshot keeps array order, includes default values, and leaves out null optional fields. Construction verifies the hash, strict JSON shape, configuration invariants, and canonical serialization in one parse.

The `Configuration` property returns the parsed object. It is shared and read-only by contract, so callers must not change it. `ReadConfiguration` parses the JSON again and returns a detached copy. Use it only when a caller must change the result. `ConfiguredCollectionSource` accepts a configuration or an existing revision.

A future schema or default change must preserve or migrate this representation, so old jobs stay readable.

### Revisions on managed jobs

New configuration-based managed jobs bind the revision and `coverageAsOf` to the durable job definition. Discovery admission binds the current revision and date to the batch and each child job. This uses the existing atomic job and admission persistence, with no second editable registry. Storage of revision rows is described in [collection](collection.md#autonomous-collection-engine).

- Job settings must match the bound source. A discovery child can use an in-scope article URL, page mode, and cleared validators.
- A stored definition is validated again when read, before execution or replay comparison. An invalid stored binding reports an operational failure.
- Reuse of a revision-bound job or batch key with another revision conflicts. Reuse with an explicitly different date also conflicts.
- Jobs and standalone definitions without provenance stay supported. Replay of such an admission compares its original execution settings. It returns the original record and assigns no later revision.
- Saved receipt replay does not depend on the current configuration.
- Direct `collect` and `collect-import` apply coverage eligibility but keep no revision. Their receipts stay source observations. Use managed jobs when the retained coverage decision matters.

### Admission replay

`ConfiguredCollectionSource` is the detached admission request. It is separate from the saved job definition and the discovery batch input. The configuration-based store overloads look up the idempotency key under the transaction lock before they resolve that request.

- An existing key compares the source settings, the revision, and any explicit date, then returns the saved decision. An omitted date reuses the saved date.
- Discovery replay also compares the attempt, mode, and admission policy.
- An existing key gets no current-date eligibility check.
- A legacy key compares its original execution settings, with no revision or date, even when current coverage has ended. An explicit date is ignored for its replay, because the original date is unknown. This does not establish historical coverage.
- A new key resolves an omitted date from UTC database time. It validates eligibility before any admission write.

Overloads that take a prebuilt definition or request serve callers that already hold a resolved decision. CLI admissions use the configuration-based overloads.

### Tracked issues

Tracked issues are in a separate file, `config/issues.json`, with its own version. `IssueConfiguration` holds the 32 policy areas and the tracked issues. Each issue links to at least one policy area and can carry a dated active range.

The file has a `version` (only 1 is supported), a list of `policyAreas` (`id`, `name`), and a list of `issues`. Each issue has an `id`, a `name`, a `definition`, and at least one `policyAreaIds` entry. It may also have `includes`, `excludes`, `startsOn` (inclusive), and `endsBefore` (exclusive).

- IDs use lowercase words joined by hyphens, with at most 128 characters.
- Policy area IDs and names are unique, and issue IDs are unique.
- Each policy area ID in an issue must exist and may appear once.
- List entries must be nonblank and unique within their list. The date range must be valid.
- The file holds at most 256 issues. The review host accepts a file of at most 1 MB.
- The host reads and validates the file once at process start. An invalid file stops startup.

`IssueConfigurationRevision` mirrors `CollectionConfigurationRevision`. It keeps the canonical JSON and its lowercase SHA-256 ID. Construction verifies the hash, strict shape, and invariants. The review host reads the file named by `CIVIC_LENS_ISSUES_CONFIG` once at start and takes the configured issue IDs from it.

## Storage and contracts

- `ConfigurationSnapshotRules` owns the revision ID, the canonical JSON, and the parse errors for both revision types.
- `ConfigurationJson` holds the JSON options and converters for configuration snapshots.
- Absent optional fields stay out of snapshots, so adding an optional field does not change existing revision IDs.
- Version owners are in [Behavior versions](../architecture.md#behavior-versions).

## Code map

| Concern | Files |
|---|---|
| Date ranges and dated names | `src/CivicLens.Core/Registry/DateRange.cs`, `src/CivicLens.Core/Registry/DatedName.cs` |
| Collection configuration | `src/CivicLens.Application/Collection/CollectionConfiguration.cs`, `src/CivicLens.Application/Collection/PersonConfiguration.cs`, `src/CivicLens.Application/Collection/SourceCoverageConfiguration.cs`, `src/CivicLens.Application/Collection/WatchedSourceConfiguration.cs` |
| Revisions | `src/CivicLens.Application/Collection/CollectionConfigurationRevision.cs`, `src/CivicLens.Application/Configuration/ConfigurationSnapshotRules.cs`, `src/CivicLens.Application/Configuration/ConfigurationJson.cs` |
| Admission request | `src/CivicLens.Application/Collection/ConfiguredCollectionSource.cs` |
| Issues | `src/CivicLens.Application/Issues/IssueConfiguration.cs`, `src/CivicLens.Application/Issues/IssueConfigurationRevision.cs` |
| Files | `config/issues.json`, `config/federal-pilot.json`, `config/example.json` |

## Limits and planned work

- Terms will come from captured Congress.gov member records.
- Agencies and committees will get an institution owner in the schema, not a synthetic person.
- The first tracked-issue list in `config/issues.json` is a proposal for the owner to edit before use.
- Verify the policy area names against the Congress.gov API before use ([R-29](../roadmap.md#next)). The bill gate compares them by exact name.
- Offices, affiliations, other external identifiers, institutional ownership, and historical identity resolution are planned.
- Configuration work estimates and bounded backfill workflows are planned.
- Public visibility is a separate future control.
- Classification will bind to the issue revision later.
- See [roadmap](../roadmap.md).
