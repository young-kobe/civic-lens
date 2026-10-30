# Roadmap

This page holds pending work only. Current behavior and agreed designs are in the feature docs; start at the [architecture lookup table](architecture.md#lookup-table).

## MVP definition

The MVP delivers the full product: officials' statements, authoritative actions, substantive document changes, and evidence-backed weekly briefs. A release with document changes only is a milestone, not the MVP. Coverage may start small, but all four record kinds must work end to end. Each one collects evidence, keeps exact inspectable provenance, gets human review, and publishes in a static release. AI drafts every record kind. Code approves action and vote records after it checks their citations (D-15, accepted on 2026-10-19). People edit and approve the other kinds, and the owner publishes. Review must handle a large volume of statements, relationships, briefs, and document changes. The dev box reaches the features, correctness, and citizen and reviewer UI of the first deploy before the first cloud deployment. Public MCP, the shared language insight, and the document-change criterion are checked after that deployment.

Exit criteria for the first deploy:

- [ ] Statements, actions, document changes, and weekly briefs each run from collection to a published static release.
- [ ] The public release stays usable during an outage of the operational application.
- [ ] Approval binds to the exact evidence version. Model output never counts as approval, and code approves only official fact records and statements from the own office of their official. People review the other kinds.
- [ ] Statements and actions support reviewed relationships and timelines.
- [ ] A weekly brief publishes with verified citation links, an explicit sample and cutoff, and versioned editorial decisions.
- [ ] Readers search and filter all published records from the public pages.
- [x] Readers can report a problem with a published record, and each report reaches a person in the review queue.
- [x] A person can publish a correction or a withdrawal of any record kind. Corrections use the confirm step of the Review page. Withdrawal uses the CLI.
- [x] House and Senate votes both publish.
- [x] The talk and action profile publishes with a method note.
- [ ] The acceptance checklists in the linked feature docs pass, except the public MCP acceptance checklist.

After the first deploy:

- [ ] Public MCP reads the same releases as the website.
- [ ] The shared language insight publishes with a method note.
- [ ] Document changes separate substantive edits from navigation churn and parser upgrades. Separate extraction streams isolate parser upgrades. Rules from measured change history filter churn, and a model tier is added only when the measurement shows a need.

## Work queue

Each item gives its outcome, what it depends on, and where the design lives. Present the design choices before building R-22 only.

The first deploy is a complete product within the product direction. Its path is R-24. R-25, R-38, R-21, and R-18 come after R-24.

### Accepted plans

The owner accepted these designs in October 2026. Each item holds its choices until the code and the feature doc replace them. Build each PR from main.

**Public site**

- R-44: Finish the public UI gaps from the release 32 check. The PRs are: vote result labels, then count units and two line bill titles, then Congress tabs in the header with the footer panel deleted, then All records filter tabs that link to search, then one date form ("27 Mar 2026"), then the profile header layout. Build the date PR alone, because it touches most files. New vote kinds and treaty votes wait for a contract choice. Depends on: none. [Publication](architecture/publication.md#limits-and-planned-work)
- R-45: Publish on change. An approval sends `pg_notify`. The worker groups approvals for about 1 minute and then publishes. The hourly timer stays as a fallback. The static release flow stays. Fix two scale problems first: keep releases by age (48 hours) plus count, and stop each `publication_releases` row storing the full `records_json`. Then cut the page `max-age` from 300 to 60 seconds and the search poll from 2 minutes to 30 seconds. Depends on: none. [Publication](architecture/publication.md#limits-and-planned-work)
- R-46: Sortable tables and vote insights. Add header sort buttons with a phone select, default orders, and a filter row. Paged tables sort through new search API sort keys. Vote pages get badges and a panel of members who broke with their party. Issue pages get a summary of votes and bills. Depends on: R-44. [Search](architecture/search.md#limits-and-planned-work)
- R-47: Insight boards. One contract PR adds vote fields to vote entries, a per-official summary roll-up, and a cosponsor field on action entries. Then build boards for party breakers, loyalists, and missed votes, as static pages for each chamber and party. Then add list badges and home cards for the closest vote, the most breaks, and the newest executive actions. Then add a bill status line, sponsored bills that moved, and bipartisan cosponsorship. Depends on: R-46. [Publication](architecture/publication.md#limits-and-planned-work)
- R-48: What an official said and how they voted. The profile lists pairs, not totals: the quote with its date, then the cast with its question, date, and result. It shows no Yea or Nay totals and no talk against action quadrant, because a bare total lacks context. Depends on: R-47. [Publication](architecture/publication.md#limits-and-planned-work)
- R-49: Public glossary. One code list owns each term, its meaning, and why it matters. A `Term` component shows the definition with the browser popover, on the first use of a term on each page. A Glossary page lists every term from the same list. The owner approved revision 4 of the wording, and any change needs approval again. Depends on: none. [Publication](architecture/publication.md#limits-and-planned-work)

**Review and approval**

- R-50: Finish publishing every approval. Move the correction note to the approve step, then delete the owner publish code. Depends on: none. [Publication](architecture/publication.md#limits-and-planned-work)
- R-51: Cut the review pages. Make one "Needs a person" queue that says why each record needs a person. Delete the triage files, the relationship and brief list pages, and the home panels that the queue replaces. Add an owner "Take off site" action in the editor, using the existing withdrawal code. Send a reader report correction straight to the editor with its note filled in. Delete the gate label pages after D-13. Depends on: R-50. [Review](architecture/review.md#limits-and-planned-work)
- R-52: Approve factual links by code. The types are "statement about a vote" and "describes own bill". The types "states position on" and "claims credit for" stay with a person. The public wording "a reviewer approved each link" must change with it. Depends on: R-51. [Review](architecture/review.md#limits-and-planned-work)
- R-53: AI brief check. A person still approves each brief. A new model task recommends approval or flags where the brief needs more attention. Its design is still to agree. Depends on: R-51. [Briefs](architecture/briefs.md#limits-and-planned-work)
- R-54: Finish bill issue narrowing. Add an `actions draft` redraft command. Narrow vote issues through the shared `BillIssueChoice` owner, with a votes redraft. Then give the model the Congress.gov legislative subjects when a bill has them. A bill with no fitting issue publishes with no issue. Depends on: none. [Actions](architecture/actions.md#limits-and-planned-work)

**Coverage**

- R-55: Stop losing feed items. Items past `maxJobs` are dropped for good today, and every member feed uses `maxJobs` 2. Survey a drain of deferred candidates, then build it. Depends on: none. [Collection](architecture/collection.md#limits-and-planned-work)
- R-56: Publish source coverage. Add a coverage field for each official and a "sources we could read" panel, so uneven coverage is visible. Depends on: none. [Publication](architecture/publication.md#limits-and-planned-work)
- R-57: Finish the executive and judicial sources, after a check of what shipped. The plan is Cabinet officials from agency pages where the official speaks, then the 9 justices, speeches and remarks only. Opinions wait for a case record kind and a PDF reader. Executive orders as a record kind shipped with the Federal Register work. Depends on: none. [Executive actions](architecture/executive-actions.md#limits-and-planned-work)
- R-58: More data sources, each from an official API or bulk file, with a terms and robots check. The order is committees, NOMINATE scores, FEC, LDA, and Census. Financial disclosures come last. Depends on: R-47. [Roster](architecture/roster.md#limits-and-planned-work)

**Operations**

- R-59: Deploy from GitHub Actions. First, the host and worker refuse to start on a missing or unknown `CIVIC_LENS_*` variable. Then a release job, gated by an owner click, reaches a forced command on the host through the tunnel. That command accepts only a full SHA on main, then updates and checks the live site. Depends on: R-44. [Host](architecture/host.md#limits-and-planned-work)
- R-60: Small open items. Add worker health to the alert script. Fix the flaky tests. Check the Fischer source URL. Add Senate press email lists, if a terms check allows them. Obfuscate the email address on the About page. Add donations. Depends on: none. [Host](architecture/host.md#limits-and-planned-work)

### Next

- R-17: Congressional Record articles as a statement connector, Senate only at first. Each granule and speaker gives one candidate, and the extraction holds only the turns of that speaker. Code filters by class, title, covered member, and about 100 words. The public link is the GovInfo details page. A new quotation kind "Congressional Record text" is a contract change. Backfill to 2025-01-03. The PRs are collection to a ledger table with no model spend, then candidates, then the backfill, then removal of the 62 dead senate.gov listings and the 29 dead sources that fail a 7 day health check. Depends on: none. [Actions](architecture/actions.md#limits-and-planned-work)
- R-39: An `analyses redraft --stale` owner command requeues finished drafts whose last run used an older task, prompt, or schema version. It covers document changes, statements, actions, votes, and briefs. Depends on: none stated. [Analysis](architecture/analysis.md#limits-and-planned-work)
- R-29: Check each policy area name against the Congress.gov API. Depends on: none stated. [Configuration](architecture/configuration.md#limits-and-planned-work)
- R-30: Fix the remaining format and parser limits that the federal coverage found. Depends on: none stated. [Collection](architecture/collection.md#limits-and-planned-work)

### Later

- R-25: Public MCP over published releases, and source health. Depends on: R-24. [Publication](architecture/publication.md#limits-and-planned-work)
- R-38: The shared language insight. Code finds the same or nearly the same sentences in releases from different offices, and publishes a method note. A person reviews each finding before publication. Depends on: R-24. [Statements](architecture/statements.md#limits-and-planned-work)
- R-21: Process view of collection and AI stages from retained state. Depends on: R-24. [Host](architecture/host.md#limits-and-planned-work)
- R-18: An engine-health view, built with R-21. The spending total is in R-24. Depends on: R-24. [Collection](architecture/collection.md#limits-and-planned-work)
- R-13: Measure document change, then gate it. Check the House press index pages and about 300 press release URLs daily for 2 to 4 weeks. Count byte, text, and line changes for each source, and label about 100 changes by hand. Add rules for the churn found. Add a model tier only if the changes that pass the rules are too many for people to review. Depends on: the first cloud deployment (R-24). [Analysis](architecture/analysis.md#limits-and-planned-work)
- R-22: Ordered row locks in the job store, after the MVP. Depends on: lock waits measured on a bills-only collection run. [Collection](architecture/collection.md#limits-and-planned-work)
- R-24: The long-term cloud move to a Hetzner CPX21 with containers, Terraform, CI, and live Auth0 verification. A Jetson runs temporary production with Docker Compose, and it serves releases through Caddy and a Cloudflare tunnel. Terraform state never goes to R2. The open host checklist items are its exit checks. Also stop the worker during a job and show that the deployed container recovers it. The search service runs as its own container on the same box. The deploy must set no provider-specific `Logging` levels for the search service, because such a rule overrides its request log filter. R-24 also holds a monthly restore test, worker service management, and the documented spending total. Withdrawal stays CLI only for the first deploy, so the runbook holds a tested `releases publish --withdraw` command that runs on the cloud host. The coverage file is 775 KB of the 1 MB limit of the shared configuration loader. Raise the limit before R-24 only if the file would pass 1 MB before the deploy. Depends on: the exit criteria of the first deploy. [Host](architecture/host.md#limits-and-planned-work)
- R-26: Read-only local MCP inspection. Depends on: none stated. [Review](architecture/review.md#limits-and-planned-work)
- R-27: Automatic discovery of official sources, and check cadence from measured change rates. It needs runtime writes, so people, sources, and coverage then move to tables. Depends on: none stated. [Collection](architecture/collection.md#limits-and-planned-work)
- R-28: Datasets and fine-tuning from labels and review history. Depends on: none stated. [Analysis](architecture/analysis.md#limits-and-planned-work)
- R-31: Remove the redundant hunk and context copy in saved comparisons. Depends on: none stated. [Documents](architecture/documents.md#limits-and-planned-work)

## Pending decisions

- D-03: Global search and expanded reviewer source visibility. [Review](architecture/review.md#limits-and-planned-work)
- D-04: Bounded storage: store each distinct extraction text once, keyed by its hash. Keep raw capture bytes forever only when a gated change, draft, or release uses them or they are the latest version. Delete other bytes after a set period and keep the observation record. [Collection](architecture/collection.md#limits-and-planned-work)
- D-05: Classification ladder, only if the R-13 measurement shows that a model tier is needed: the uncertainty signal for each tier, where the escalation policy and thresholds live, and the stronger model for the reasoning tier. [Analysis](architecture/analysis.md#limits-and-planned-work)
- D-11: Drafting the past votes of a member who is added to tracking later. [Votes](architecture/votes.md#limits-and-planned-work)
- D-12: Content profiles for the 282 of 1,394 page, HTML, and feed sources that have none. [Documents](architecture/documents.md#limits-and-planned-work)
- D-13: A classifier benchmark for the statement issue gate, to run as soon as possible. The gate label store, the held-out split, the owner labeling page, and the label export are built ([Statements](architecture/statements.md#gate-labels)). Still to build: a Sonnet pre-label for releases outside the held-out set, and about 100 labels by hand, starting from the 48 measured ones. Then compare Haiku 5.5, Clef-flash, Clef-omni, Clef, a fine-tuned Clef, Strands Decider 2B, a fine-tuned ModernBERT, and a feature baseline. The issue list is configuration. A decision model takes the options in each request, so it needs no retraining when the list changes. A model that runs inside the worker (ONNX Runtime) needs a reviewed boundary change first. The fine-tuning needs the dataset rules of [R-28](#later) agreed first.
- D-16: Party count for an independent who left Congress. The roster reads only current members, so such a member is left out of "Votes with their party". The options are to read `legislators-historical.json`, to keep the roster terms of a member who leaves, or to store party and caucus on each cast. [Roster](architecture/roster.md#limits-and-planned-work)

## Product direction

The audience is regular citizens who want transparency in U.S. national politics. State and local coverage are possible future extensions. Readers start with reviewed accounts and can inspect the evidence behind them. The editorial flow and the restrained monochrome identity are agreed.

Design for scale. Each record carries rich detail, and coverage of officials, sources, and issues is broad. Automate every step that does not need human judgment.

Derived insights are product goals. They make Civic Lens more than a connector to source APIs. Code computes each metric and publishes a method note. A person reviews any judgment before publication, and each insight shows its coverage limits.

1. Changed record: detect deleted press releases, rewritten quotations, and positions removed from issue pages. Show both versions and their timing next to votes and events. This needs deletion detection and the document-change gate.
2. Shared language: find the same or nearly the same sentences in releases from different offices. Show coordinated messaging, how it spreads, and who uses it first.

## Deferred

- News coverage in briefs: deferred until we measure how much of the real record a set of news feeds covers. Add coverage links and a gap signal only if that recall is high. Store feed entries only, never full articles.
- Congress.gov member records and a waiting window before sponsorship revisions: see [actions](architecture/actions.md#limits-and-planned-work) and [action decisions](architecture/actions.md#decisions).
- Self-hosted encoder models: see [analysis](architecture/analysis.md#limits-and-planned-work).
- Browser rendering, OCR, and cross-origin robots redirects: see [collection](architecture/collection.md#limits-and-planned-work) and [robots](architecture/collection.md#robots-and-pacing).
- Shared line-format rules for renderers: see [bills](architecture/bills.md#limits-and-planned-work).
