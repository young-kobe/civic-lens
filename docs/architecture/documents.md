# Documents

## Purpose

The documents feature turns verified captures into immutable text extractions, exact citations, histories, and comparisons. It finds text differences only. It never decides significance or approval.

## Decisions

- Text is not part of extraction identity. Conflicting output for the same inputs is an error, so stored evidence is never overwritten.
- A parser, normalization, or profile upgrade creates a new extraction. Old text and citations stay valid.
- Content profiles use a small selector syntax. Its syntax is deliberately bounded.
- A profile that selects zero or several elements fails. No fallback hides a configuration error.
- One private HTML method serves captures and HTML fragments. One owner holds the HTML rules and limits.
- History and comparison reject overflow. They never return a partial result.
- A stored comparison is never reinterpreted. A new algorithm or settings version keeps the old code for old rows.
- Neither an extraction, a citation, nor a comparison is a human approval.

## Invariants

Each line gives the rule, what enforces it, and the test that protects it. Cross-feature invariants are in [architecture](../architecture.md#invariants).

- An extraction ID depends on the attempt and the versions, never on the text. Enforced by `DocumentExtraction`. Test: `DocumentExtractionTests.IdentityIsFramedAndIndependentOfTextWhileTextHashTracksContent`.
- One extraction identity keeps one text and one provenance. Enforced by `PostgresDocumentExtractionStore`: an insert that finds the row compares the committed winner and throws on a mismatch. Test: `PostgresDocumentExtractionStoreTests.RejectsChangedProvenanceAndDetectsStoredTextTampering`.
- A read verifies the text hash and the identity. Enforced by `PostgresDocumentExtractionStore`. Test: `PostgresDocumentExtractionStoreTests.RejectsChangedProvenanceAndDetectsStoredTextTampering`.
- Versions that differ give an Incompatible comparison, not a diff. Enforced by `DocumentComparison`. Test: `DocumentComparisonTests.RequiresEqualProfileRevisionAndProcessingVersions`.
- The comparison store saves only a result that it computed from retained extractions, and only when the status is Complete. Enforced by `PostgresDocumentComparisonStore.CompareAndSaveAsync`, the only save path. Test: `PostgresDocumentExtractionStoreTests.AnIncompleteComparisonSavesAndQueuesNothing`.
- Concurrent saves of one identity keep one row, and only the inserting transaction runs the follow-on writes (candidate registration, draft analysis queue row). Enforced by `INSERT ... ON CONFLICT DO NOTHING` in both stores. Test for one row: `PostgresDocumentExtractionStoreTests.ComparisonReplayBindsRetainedEvidenceAndDetectsTampering`. No test for the follow-on writes: the loser returns before commit, and the follow-on inserts are idempotent.
- A processing checkpoint accepts only a complete, verified comparison of its own extraction. Enforced by `PostgresEvidenceProcessingStore`. Test: `PostgresDocumentExtractionStoreTests.AProcessingCheckpointRejectsAStoredComparisonThatIsNotComplete`.

## Behavior

### Extraction

`ExtractDocument` reads an imported captured attempt and calls `IDocumentTextExtractor`. It builds domain evidence and saves it through `IDocumentExtractionStore`. It never collects, infers deletion, or treats a 304 as a new capture. Every explicit extraction verifies the file again, also during replay.

`CaptureDocumentTextExtractor` reads `<sha256>.gz` from the supplied artifact root. It verifies the storage-decoded byte length and hash before it decodes HTTP encodings. It supports `text/plain` and `text/html`. It decodes identity, gzip, deflate, and Brotli in reverse order. Character decoding is strict, and cancellation is supported. Unsupported types or encodings, missing or corrupt files, and exceeded bounds fail. No partial text is saved.

`ExtractHtmlFragmentAsync` converts an HTML string that is not a capture, such as a Congress.gov summary. It uses the same private method as the capture path for parsing, depth checks, and body text. It also validates the text.

Limits:

| Item | Limit |
|---|---|
| Storage file and encoded entity | 10 MB |
| Each HTTP decoding stage | 10 MB |
| HTML tokens and elements | 100,000 |
| Depth | 64 |
| Extracted text | 2,000,000 UTF-16 units |

HTML parsing recovers optional paragraph closures and mismatched end tags. It checks lexical depth and the depth of the resulting tree. Self-closing SVG and MathML elements do not add lexical depth. The DOM depth check still applies, also beneath SVG foreign content.

Plain text uses the BOM, then the HTTP charset, then UTF-8. HTML uses the BOM, the HTTP charset, a meta declaration in the first 1,024 bytes, then Windows-1252. HTML encoding labels use the HTML mappings.

HTML body text excludes `script`, `style`, `template`, and `noscript`. It collapses whitespace and puts a newline at block boundaries. This is static text extraction, not browser visibility or article selection. CSS, hidden elements, and navigation can remain. Plain text keeps its decoded whitespace. The current versions are `capture-text-v2` and `body-text-v1`.

### Content profiles

Configuration version 2 can define `documentProfiles`. A profile has an ID, one content selector, and excluded selectors. A source names its profile with `documentProfileId`. Several sources can share one profile.

A selector is one ASCII tag name, `#id`, or `.class`. It has at most 128 characters, including its prefix. Identifiers match `[A-Za-z_][A-Za-z0-9_-]*`. A profile has at most 16 distinct exclusions. Compound selectors, combinators, attributes, pseudo-classes, and selector lists are not supported.

Configured extraction snapshots the validated configuration before it waits for storage. It finds the profile of the captured source ID and keeps the exact profile with the extraction. Current coverage does not block reprocessing of old evidence. The configuration is a processing choice, not a statement about past ownership or coverage. Profile settings never enter collector requests. Whole-body commands still work without a profile.

A profile selects exactly one HTML element in the body. The body itself can be that element. A missing, ambiguous, ignored, or root-excluding selection fails visibly. Matching uses HTML-namespace elements and case-insensitive tag names. IDs and classes compare ordinally. Class tokens split on HTML ASCII whitespace.

Exclusions apply only inside the selected region. They keep separators between the remaining text and never change the raw capture. Profiles apply only to HTML, so a plain-text profile request fails. A wrong profile can omit relevant text, so the selected text and raw evidence stay inspectable. A changed profile does not show a source edit.

### Citations

A citation holds an extraction ID, a UTF-16 start, a positive length, and the exact quoted text. A boundary cannot split a surrogate pair. A citation locates text in one immutable extraction. It is not a raw HTML byte offset or a PDF page. The CLI has commands for whole-body and configured extraction, inspection, and citations. `SourceSpan` is the stored span (extraction ID, start, length, no quote) shared by document changes, statements and actions. `DocumentTextSpan` is the checked span with the quote.

### History

A tracked document is a source ID plus an exact requested URL. Redirect destinations do not merge histories. `GetDocumentHistory` orders all retained observations by observation time, then by ordinal attempt ID.

Each parser, normalization, and profile revision is an independent stream. Consecutive identical text is grouped, and every attempt stays inspectable. A to B to A gives three transitions. A failed check, an unresolved 304, or a capture without an extraction in that stream breaks adjacency. That break does not imply a source edit. A resolved 304 uses the extractions of its prior capture and never creates new text. Explicit reprocessing can add a stream to old observations. History is a derived view, not an editorial decision.

`GetDocumentHistory` reads through `IDocumentHistoryStore`. The Postgres history store reads one repeatable-read snapshot. It batch-loads observations, captures, and extraction rows, and indexes them by attempt. It validates each extraction with the same rule as a single read. A hash index on the requested URL serves equality lookups for all outcomes. The query keeps the exact source and URL predicates, so a hash collision cannot merge two documents. Only accepted bounded histories are sorted in memory. Application builds transition IDs once and tracks only the active adjacent streams. Discovery candidate payloads are not in this view; discovery inspection shows them.

History limits are 1,000 observations, 10,000 extraction revisions, and 10 million UTF-16 text units. The text budget counts repeated 304 references. The database preflight counts each Unicode scalar as two UTF-16 units, so supplementary characters cannot pass the budget. Overflow is an error, not a partial timeline.

### Comparison

`DocumentComparison` in Core owns compatibility, identity, and a deterministic line-level longest-common-subsequence comparison. Word detail keeps whitespace. Both extractions must share the source ID, exact requested URL, parser, normalization, and profile revision. The result is `complete`, `incompatible`, or `limitExceeded`. A complete result with no hunks means the text is unchanged.

Changed ranges, word edits, and context keep exact UTF-16 positions on both sides. A zero-length range marks an insertion or deletion point. A positive range can go to the citation handler. A comparison finds text differences, not significance.

Comparison limits:

| Item | Limit |
|---|---|
| Lines per extraction | 2,000 |
| Line matrix cells | 1,000,000 |
| Changed UTF-16 units per hunk | 8,192 combined |
| Context | 1 line on each side, within 8,192 units per side |
| Word tokens per side per hunk | 512 |
| Word matrix cells per hunk | 250,000 |
| Word matrix cells overall | 1,000,000 |

A result over any limit is explicit and has no partial hunks. Context and changed text are never truncated. Cancellation stops the work and saves no comparison.

`CompareDocuments` passes two extraction IDs to `IDocumentComparisonStore.CompareAndSaveAsync`. The store reads both extractions, computes the comparison once, and saves it only when it is Complete. An incompatible or limit exceeded result is returned and not saved. The CLI creates comparisons and shows history and results. The worker also prepares compatible comparisons automatically.

### Evidence pages

The pages under `/Documents` show a saved comparison, saved text, a cited passage, and saved versions. Links in Review and Sources open them. There is no document catalog until document search exists. These pages support review, but they are not the editorial entry point. The pages call the Application document handlers and the extraction store boundary.

A saved comparison is always complete. Positive changed ranges link to exact citations. Zero-length ranges stay insertion or deletion points. Encoded text keeps carriage returns, leading newlines, and C1 code points through HTML parsing. The comparison page can start an eligible private review draft through the shared Application handler.

A malformed link returns 400, and missing evidence returns 404. A storage or reconstruction failure returns a sanitized 503 with no provider diagnostics. History overflow shows a limit notice and no partial timeline. Source IDs and URLs show as retained identifiers, not as official attribution. No page calls a text difference substantive or human-approved.

## Storage and contracts

- The generated [documents schema](schema/documents.md) lists every table, column, key, check, index, and trigger.
- Text identity is the SHA-256 of strict UTF-8 text.
- Whole-body extraction identity is the SHA-256 of three framed UTF-8 strings: attempt ID, parser version, normalization version. Each frame has a four-byte big-endian byte length.
- A profile extraction adds the profile revision ID as a fourth framed string. Three-field identities do not change.
- Profile revision identity hashes the framed ID and selector, a four-byte big-endian exclusion count, and each framed exclusion in order. IDs and exclusion order are significant.
- `PostgresDocumentExtractionStore` uses the operational DbContext and additive migrations. The save verifies provenance first. It then inserts with `ON CONFLICT DO NOTHING` in one ReadCommitted transaction and takes no advisory lock. If the row exists, the save compares the committed winner and throws on a mismatch. Only the inserting transaction registers statement candidates.
- A foreign key keeps the source attempt. Reads verify extraction identity and the text hash.
- The nullable `profile_json` column holds the profile snapshot. Old rows are whole-body extractions with unchanged IDs. A changed or removed stored profile fails identity verification.
- Absent profile fields are null and are left out of configuration snapshots. Older canonical snapshots therefore stay readable.
- Captures stay as files. Extracted text lives in Postgres. No public serving path reads this store.
- `document_comparisons` has foreign keys to both extractions. It stores the algorithm and settings versions and the serialized result.
- A save takes no advisory lock. It inserts with `ON CONFLICT DO NOTHING` in one ReadCommitted transaction. A replay that finds the row checks the retained row against the computed result and throws on a mismatch. The store computes the result from retained extractions, so fabricated text cannot reuse an extraction identity.
- Reads that act on evidence revalidate the extractions and compute the result again to find conflicts. The New changes list reads only the stored result; see [review](review.md#storage-and-contracts).
- The comparison save also queues AI drafting in the same transaction; see [analysis](analysis.md#behavior).
- `db migrate` installs the comparison table. Version owners are in [Behavior versions](../architecture.md#behavior-versions).

## Code map

| Concern | Files |
|---|---|
| Domain values and comparison | `src/CivicLens.Core/Documents/DocumentExtraction.cs`, `src/CivicLens.Core/Documents/DocumentContentProfile.cs`, `src/CivicLens.Core/Documents/DocumentTextSpan.cs`, `src/CivicLens.Core/Documents/SourceSpan.cs`, `src/CivicLens.Core/Documents/DocumentComparison.cs`, `src/CivicLens.Core/Documents/DocumentLines.cs` |
| Use cases and ports | `src/CivicLens.Application/Documents/ExtractDocument.cs`, `src/CivicLens.Application/Documents/GetDocumentHistory.cs`, `src/CivicLens.Application/Documents/CompareDocuments.cs`, `src/CivicLens.Application/Documents/GetDocumentCitation.cs`, `src/CivicLens.Application/Documents/IDocumentTextExtractor.cs` |
| Profile mapping | `src/CivicLens.Application/Documents/DocumentProfileConfiguration.cs` |
| Text extraction | `src/CivicLens.Infrastructure/Documents/CaptureDocumentTextExtractor.cs` |
| Postgres stores | `src/CivicLens.Infrastructure/Documents/PostgresDocumentExtractionStore.cs`, `src/CivicLens.Infrastructure/Documents/PostgresDocumentHistoryStore.cs`, `src/CivicLens.Infrastructure/Documents/PostgresDocumentComparisonStore.cs` |
| Evidence pages and CLI | `src/CivicLens.Host/Documents/ComparisonPage.razor`, `src/CivicLens.Host/Documents/HistoryPage.razor`, `src/CivicLens.Host/Documents/CitationPage.razor`, `src/CivicLens.Host/Documents/ExtractionPage.razor`, `src/CivicLens.Host/Documents/DocumentCommand.cs` |
| Parser parity test | `tests/CivicLens.Tests/Infrastructure/Documents/HtmlParseParityTests.cs` |

## Limits and planned work

- Automatic navigation filtering is planned.
- A page without a content profile blocks with `contentProfileUnavailable`. On 2026-10-16, 12 of 30 sampled House feeds blocked this way. In `config/federal-coverage.json`, 282 of 1,392 page, HTML, and feed sources have no profile. See [D-12](../roadmap.md#pending-decisions).
- Future cleanup ([R-31](../roadmap.md#later)): a saved comparison keeps hunk and context text that its exact offsets can rebuild from the two extractions. Remove this redundant copy.
- Keep document history available for investigation.

Document-change acceptance:

- [ ] Inspect and publish a real substantive edit with both versions and valid citations.
- [ ] Navigation churn and parser upgrades cannot pass as source changes.
- [ ] A reader can tell observation time from an established event or edit time, and sees relevant evidence gaps.
- [ ] A reviewer assesses the proposed public wording beside its sources. A change to approved content or evidence needs review of the new version.
- [ ] Approval alone does not publish. Public output stays usable during an outage of the operational application.
- Large histories need a future pagination design.
- Substantive-change judgments belong to the planned classifier; see [roadmap](../roadmap.md).
