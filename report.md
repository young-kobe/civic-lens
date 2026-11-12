# Review report for PR #504

Move the brief page and withdrawal notice to server rendering. Head 59c6a2ff.

## Summary

I found no P1 issues in PR #504. There are 3 P2 and 7 P3 findings. The brief page is correct and close to the mockup. The withdrawal notice keeps the old static layout. The PR does not reuse it in the Mosaic v2 form.

## What I checked

- I checked out head 59c6a2ff.
- Locked restore, the Release build, `dotnet format --verify-no-changes` and `render-architecture.py --check` pass.
- 736 tests pass and 1 is skipped. The run included the PR's test classes, ReleaseRendererTests, the Brief tests and the Architecture tests.
- A temporary xunit test seeded these cases on the real endpoint host: a model brief with both times, a long person-written brief with no times and long headlines, a statement withdrawal with a correction and HTML in its note, and a brief withdrawal with a short note. I deleted the test and committed nothing.
- I captured the pages and the Mosaic v2 mockups at 360 and 1280 px, light and dark, and looked at each capture. No case had horizontal scroll.
- The text contrast that I measured is 5.7:1 or more in both themes.
- The Impeccable context launcher did not run. I used SKILL.md, critique.md and audit.md, and public-site.md.

## Known gap (not a finding)

The page does not show "Do not rely on it. The page keeps this notice so old links do not break." yet.

## Scope checks

- Record route: the PR reuses the one route. Brief and withdrawal are two more cases in `src/CivicLens.PublicApi/Pages/ServerPageEndpoints.cs:87-98`.
- AiSummary: reused. The brief passes the text origin and the label from the view.
- SourceFold and LinkChip: neither page renders them, and neither page has a copy.
- Views: the PR adds no new view. The migration does `CREATE OR REPLACE` on `public_api.briefs` only.
- Approval time: it uses the latest Approve decision by review state version. This is the same as the static rule, `PublishedRecordRules.LatestApproval`.
- Cited count: it is `Citations.Length`, and `BriefRevision.Validate` enforces the same rule.
- Counts are scoped: the check line says "of the {n} new records that Civic Lens considered for the week". The fact list starts with the week row.
- Times are labeled: they show as "Approved" and "First published", in UTC.
- New public strings: I found none that are not approved.
- Migration timestamp: `20261112120000` is after the newest migration on main. A trial merge with origin/main has no conflicts.

## P1

None.

## P2

### P2-1. The withdrawal page does not follow the mockup layout

File: `src/CivicLens.PublicWeb/Publication/PublishedWithdrawalPage.razor:5-9`

- The page keeps `RecordView` with the small `.record-headline` heading, about 28 px.
- The approved sentence "Civic Lens withdrew this {kind} on {time}." is a plain lede. There is no banner with `role="status"` like in the mockup.
- The other server record pages use `detail-head`, `detail-eyebrow` and `detail-headline`.

Fix: put the sentence in the shared `Notice` component with a status tone, so it gets `role="status"`. Use the `detail-head` markup for the heading. Keep the existing strings.

### P2-2. The withdrawal trail breaks the trail rule

File: `src/CivicLens.PublicWeb/Publication/PublishedWithdrawalPage.razor:5` (the section is `PublicSection.AllRecords`)

- The trail reads "Home / Votes and bills / Withdrawn statement: cccccccccccc".
- The rule in public-site.md says that a record trail names the section of its kind. The mockup has "Home / Statements / …".

Fix: get the section crumb from `Withdrawal.Kind` through `PublicSections`, as the record trail does. Do not add a `ReportedRecord`. The matrix row 15 keeps the fixed page report link.

### P2-3. No test covers a withdrawn vote, but the PR deletes the limit that covered it

File: `tests/CivicLens.Tests/PublicApi/Pages/RecordPageEndpointsTests.cs:203` (`WithdrawableKinds`)

The PR deletes the limit "A withdrawn vote answers 404 on the `/web` vote preview" from public-site.md. `WithdrawableKinds` leaves out `PublishedRecordKind.Vote`, so no test proves that a withdrawn vote now gets the notice.

Fix: add `Vote` to the theory data.

## P3

### P3-1. The brief shows its week three times

File: `src/CivicLens.PublicWeb/Publication/BriefDetailPage.razor:10`

The week is in the eyebrow range, the `detail-lede` ("Starts Monday 5 Oct 2026, New York time") and the "Week" fact row. The mockup shows it once.

Fix: remove the lede at `BriefDetailPage.razor:10` and keep the fact row.

### P3-2. The roadmap is out of date

File: `docs/roadmap.md:34`

The roadmap still lists "the brief coverage counts" as remaining, but this PR implements them.

Fix: remove the item.

### P3-3. The public-site.md matrix row for corrections and withdrawals is wrong

File: `docs/architecture/public-site.md:242`

- It says "The gated `/web/{kind}/{id}` routes for statements, executive actions, document changes, and briefs". The real route is the one route `/web/records/{id}.html`, and it covers votes too.
- It also plans "the same notice on the routes of the other kinds", which disagrees with the one-route design.

Fix: name the one route and all kinds, and remove that planned clause.

### P3-4. public-site.md now has an empty "Limits:" heading

File: `docs/architecture/public-site.md:290`

Fix: write "None" or remove the heading.

### P3-5. The cited-record list markup has two owners

Files: `src/CivicLens.Host/Publication/PublishedBriefPage.razor:14-18` and `src/CivicLens.PublicWeb/Publication/BriefDetailPage.razor:27-33`

The `<li>` with the choice label, the link and `CitedRecordNote` is copied in both files.

Fix: move it into one PublicWeb component that takes a link builder, as `PublishedBriefText` takes the paragraphs.

### P3-6. The PR body has two false claims

Location: the PR #504 body.

- It says that the PR "reuses … `SourceFold`, and `LinkChip`". Neither renders on these pages. Fix: say "reuses AiSummary; it holds no copy of SourceFold or LinkChip".
- It says "No browser check". The standing rule needs screenshots at 360 and 1280 px, light and dark. Fix: attach them.

### P3-7. Exact terms with no explanation

File: `src/CivicLens.PublicWeb/Publication/PublishedBriefView.cs:33` ("Cutoff release" fact) and `PublishedBriefView.cs:56` (`Sample` text)

"Cutoff release" and "releases up to release {n}" have no term popover. Grade-12 readers do not know what a release is. These strings come from the static page and are not new.

Fix: add a `Term` for "release" from `Glossary` when one exists.

## Mockup differences that I judged approved

- The brief has no figure tiles. L2 says to drop weak figures, and the roadmap says "without weak figures".
- "How this brief was made" holds the fact list and not the method text. The owner approved this title.
- The check line holds the sample caveat and the counts, not the mockup's check text. R5 merges caveats into the check line, and the owner approved it.
- Each cited row shows its ID, kind and revision, not an event date. This is the approved list content, and `brief_citations` has no event date.
- The row cards with the R1, R2 and R3 labels let the citation anchors work. `:target` gives a visible highlight.
- "Earlier week" and "All weekly briefs" are not on the page. The mockup marks "Earlier week" as an example, and the trail links to Weekly briefs.
- The withdrawal page has no "Who decided" line, because the data holds no decider (approved). The "Do not rely on it" sentence is the known gap. The labels "Note" and "This record was withdrawn" are the existing static strings that the page shares.
- The header navigation, the freshness line and the "What you can do" card are site-wide or planned items, not part of this PR.
