# Roadmap

This page holds pending work. A checked step is merged to main, and a merged step reaches the live site at the next deploy. Delete an item when all its steps are checked. Current behavior and agreed designs are in the feature docs; start at the [architecture lookup table](architecture.md#lookup-table).

## MVP definition

The MVP delivers the full product: officials' statements, authoritative actions, substantive document changes, and evidence-backed weekly briefs. A release with document changes only is a milestone, not the MVP. Coverage may start small, but all four record kinds must work end to end. Each one collects evidence, keeps exact inspectable provenance, gets human review, and publishes in a static release. AI drafts every record kind. Code approves action and vote records after it checks their citations (D-15). People edit and approve the other kinds, and the worker publishes. Review must handle a large volume of statements, relationships, briefs, and document changes. The dev box reaches the features, correctness, and citizen and reviewer UI of the first deploy before the first cloud deployment. Public MCP, the shared language insight, and the document-change criterion are checked after that deployment.

Exit criteria for the first deploy:

- [ ] Statements, actions, document changes, and weekly briefs each run from collection to a published static release.
- [ ] The public release stays usable during an outage of the operational application.
- [ ] Approval binds to the exact evidence version. Model output never counts as approval, and code approves only official fact records and statements from the own office of their official. People review the other kinds.
- [ ] Statements and actions support reviewed relationships and timelines.
- [ ] A weekly brief publishes with verified citation links, an explicit sample and cutoff, and versioned editorial decisions.
- [ ] Readers search and filter all published records from the public pages.
- [x] Readers can report a problem with a published record, and each report reaches a person in the review queue.
- [x] A person can publish a correction or a withdrawal of any record kind. Corrections use the confirm step of the Review page. Withdrawal uses the CLI or Take off site on the record editor.
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

The owner accepted these designs. Each item holds its choices until the code and the feature doc replace them. Build each PR from main.

**Public site**

- R-47: Insight boards. One contract PR adds vote fields to vote entries, a per-official summary roll-up, and a cosponsor field on action entries. Then build boards for party breakers, loyalists, and missed votes, as static pages for each chamber and party. Then add list badges and home cards for the closest vote, the most breaks, and the newest executive actions. Then add a bill status line, sponsored bills that moved, and bipartisan cosponsorship. Depends on: none stated. [Publication](architecture/publication.md#limits-and-planned-work)
  - [x] Contract PR, with one operator rewrite of carried entries and no reset, not yet run
  - [x] Boards
  - [x] List badges and home cards
  - [x] Bill status line
  - [ ] The remaining items
  - [x] Show the prior Congress on the vote boards until a new Congress has votes. One `releases publish <key> --rewrite-officials` fills `previous` in the summary rows.
- R-48: What an official said and how they voted. The profile lists pairs, not totals: the quote with its date, then the cast with its question, date, and result. It shows no Yea or Nay totals and no talk against action quadrant, because a bare total lacks context. Depends on: R-47. [Publication](architecture/publication.md#limits-and-planned-work)
  - [ ] Said and voted pairs on the profile
- R-64: What officials did on an issue. Today a bill or vote links to an issue with no direction, so no official position can be derived. Add a row for what a bill does on an issue, with citations, and a row for what a Yea does on each vote, such as pass, amend, table, or proceed. Code then lists each official's casts with that evidence. The page shows no score and no "pro" or "anti" label. Effect on an issue starts with facts only: bill status, vote result, and whether the bill became law. Start with one tracked issue and measure accuracy and review load. The schema design goes to the owner first. Depends on: R-48, and the action redraft. [Publication](architecture/publication.md#limits-and-planned-work)
- R-69: Representation audit. Show how an official acts for their constituents, with the evidence side by side and its error named. Civic Lens never publishes a composite representation score or grade, its own or a scorecard's. Each feature has a method note. The research is on branch `survey-representation-data`. Depends on: none stated. [Publication](architecture/publication.md#limits-and-planned-work)
  - [ ] How it passed: each bill and nomination shows roll call, voice vote, or unanimous consent, so readers see when no member's position is on record
  - [ ] Bill fate: the profile shows how far each sponsored or cosponsored bill got and when the official joined it, beside the chamber base rate, with no rank
  - [ ] District context: Census district facts with their margins of error beside votes on matching issues. The owner chooses where district facts are stored before this PR
  - [ ] Ideology and district: the NOMINATE score beside the district presidential margin, with a reviewed method note
  - [ ] Constituent opinion: survey estimates with error bars, only for an issue with a published estimate whose terms allow reuse, after a person reviews the method note

**Review and approval**

- R-51: Cut the review pages. Make one "Needs a person" queue that says why each record needs a person. Delete the relationship and brief list pages, and the home panels that the queue replaces. Add an owner "Take off site" action in the editor, using the existing withdrawal code. Send a reader report straight to the editor, with the reader's words on top. Delete the gate label pages after D-13. Depends on: none. [Review](architecture/review.md#limits-and-planned-work)
  - [x] "Needs a person" queue, and delete list pages and home panels
  - [x] "Take off site" action
  - [x] Report to editor
  - [ ] Delete gate label pages, after D-13
- R-65: One page for each review job. A reader report opens the record editor with the reader's words on top. The person dismisses it, or edits, types the public text, and clicks "Save and approve". After a decision, the page moves to the next item. A failed approval keeps the typed text. Every approval check stays. Depends on: R-51 take off site. [Review](architecture/review.md#limits-and-planned-work)
  - [x] Save and approve in one transaction, and open the edit form by default
  - [x] Reader reports in the editor, closed by the approval
  - [ ] Move to the next item after a decision
- R-52: Approve factual links by code. The types are "statement about a vote" and "describes own bill". The types "states position on" and "claims credit for" stay with a person. The public wording "a reviewer approved each link" must change with it. Depends on: R-51. [Review](architecture/review.md#limits-and-planned-work)
  - [ ] Code approval of factual links, and new public wording
- R-54: Finish bill issue narrowing. The model gets the Congress.gov legislative subjects when a bill has them, and the redraft command exists. Still open: run the subjects backfill and the redraft command on the live site. Then check on a sample that a bill with no fitting issue publishes with no issue. Depends on: none. [Actions](architecture/actions.md#limits-and-planned-work)
  - [x] Vote issue narrowing
  - [x] `analyses redraft --stale`
  - [x] Read Congress.gov subjects in drafts
  - [ ] Run the subjects backfill and the redraft command
  - [x] Write action text in code when no summary text reaches the model, and move summary bills to the new prompt
  - [ ] Draft the action backlog, and spot check the issue tags after it

- R-66: Gate model output on confidence. Models give a confidence score. A low score sends the record to the one page review, where a person fixes it or tells the model what to fix. Set the threshold from measured data and a person-labeled set. Depends on: R-65. [Review](architecture/review.md#limits-and-planned-work)
  - [ ] Agree the design
  - [ ] Build the gate and the model fix
- R-67: Measure each prompt. Each prompt version gets a live, budgeted, opt-in measure on fixed bills, and the numbers go on the PR. Use the cheapest setting that holds accuracy. The order is vote, statement draft and gate, document change, relationship, then brief. Depends on: none. [Analysis](architecture/analysis.md#limits-and-planned-work)
  - [x] Fix the vote prompt wording and measure it
  - [x] Build the first opt-in measure, `actions text measure`, for the bill text prompt (#373)
  - [ ] Agree one opt-in eval command. Later prompt measures extend `actions text measure` or replace it with one command
  - [ ] Measure the other prompts, one PR for each

**Coverage**

- R-55: Stop losing feed items. Items past `maxJobs` are dropped for good today, and every member feed uses `maxJobs` 2. Survey a drain of deferred candidates, then build it. Depends on: none. [Collection](architecture/collection.md#limits-and-planned-work)
  - [x] Admit new URLs before rechecks, with a starvation test first
  - [ ] Survey and build a drain of deferred candidates
- R-56: Publish source coverage. Add a coverage field for each official and a "sources we could read" panel, so uneven coverage is visible. Depends on: none. [Publication](architecture/publication.md#limits-and-planned-work)
- R-57: Finish the executive and judicial sources, after a check of what shipped. The plan is Cabinet officials from agency pages where the official speaks, then the 9 justices, speeches and remarks only. Opinions wait for a case record kind and a PDF reader. Executive orders as a record kind shipped with the Federal Register work. Depends on: none. [Executive actions](architecture/executive-actions.md#limits-and-planned-work)
- R-58: More data sources, each from an official API or bulk file, with a terms and robots check as the first step of its PR. The order is value for effort: Congress.gov amendments, House NOMINATE scores, Census ACS district profiles, presidential vote share by district, FEC, LDA, and committees. Financial disclosures come last. FEC money shows only as totals, with no donor names, because federal law limits reuse of donor names. Depends on: R-47. [Roster](architecture/roster.md#limits-and-planned-work)

- R-61: Finish executive action records with issue tags. The body text and the AI summary moved to R-68. Issue tags need a design first, because these documents have no policy area. Depends on: none. [Executive actions](architecture/executive-actions.md#limits-and-planned-work)
- R-62: Statements from official social accounts, such as Bluesky and YouTube. This needs a list of verified accounts and a new design. X and Meta stay out, because of cost and access limits. Depends on: R-17. [Collection](architecture/collection.md#limits-and-planned-work)

**Data and schemas**

- R-63: Move our own data out of JSON. Our data and our schemas are a first class feature, so the database must enforce their rules. Decisions, revisions, and other data we define move to columns and child tables. Stored JSON stays only for text that must stay verbatim: the hashed configuration revision, the byte-compared document comparison, and raw model and collector input and output. Before a kind drops its JSON, a check rebuilds each revision from the new rows and compares it to the stored JSON. Public release files stay JSON, because they are the published product and not our storage. Each link between records becomes a real row with a foreign key, so that later insights can follow it. The revision steps start after R-51 PR 3 and PR 4. Depends on: none. [Architecture](architecture.md)
  - [x] Survey every JSON column
  - [x] Agree the direction
  - [x] Stop parsing JSON where a column already holds the value (#261)
  - [x] Move review decisions to columns, and read them there (#266, #272)
  - [x] Drop the decision JSON, after a read-only check of production rows
  - [x] Add tick bounds and missing foreign keys, after a read-only check of production rows
  - [x] Agree the table design from one pilot on vote revisions, with measured row counts
  - [x] Vote pilot 1: write both, so every vote revision writes columns and child rows beside its JSON
  - [x] Vote pilot 2: backfill old vote revisions and verify them with a C# command that compares each revision to its JSON
  - [x] Vote pilot 3: move the vote readers to the columns
  - [x] Vote pilot 4: drop the JSON column and the `votes revisions` commands, and make the core columns `NOT NULL`
  - [ ] Move the other revision kinds, one PR for each. Executive action, document change, relationship, statement, brief, and action revisions are done in code through step 3. The production backfills and the JSON drops stay open
  - [ ] Move release entries to a table: one immutable row for each record revision, with a foreign key to that revision. `publication_record_states` gives the release membership. The code of `releases entries backfill <limit>` and `releases entries verify` is done. Open: the production runs, the foreign key from `publication_record_states`, and the move of the publish path off the manifest.
  - [ ] Write `release.json` from the tables, and stop reading the previous manifest in a publish
- R-27: Officials, sources, and discovery in tables. People data lives in tables, not in the configuration file. Sources and coverage move to tables too, and the owner commands and the worker write them. Check cadence then follows measured change rates, and discovery finds official sources. Plans: the git branches `survey-officials-in-tables` and `survey-source-discovery`. Depends on: none stated. [Roster](architecture/roster.md#officials-in-tables), [Configuration](architecture/configuration.md#limits-and-planned-work)
  - [ ] Officials in tables, PRs 3 to 8: minting members, bindings, pinned readers, live readers, coverage from `officeOf`, justices and gaps
  - [ ] Sources and coverage in tables
  - [ ] Check cadence from measured change rates
  - [ ] Discovery of official sources

**Operations**

- R-59: Deploy from GitHub Actions. First, the host and worker refuse to start on a missing or unknown `CIVIC_LENS_*` variable. Then a release job, gated by an owner click, reaches a forced command on the host through the tunnel. That command accepts only a full SHA on main, then updates and checks the live site. Depends on: none. [Host](architecture/host.md#limits-and-planned-work)
- R-60: Small open items. Fix the flaky tests. Check the Fischer source URL. Add Senate press email lists, if a terms check allows them. Obfuscate the email address on the About page. Add donations. Depends on: none. [Host](architecture/host.md#limits-and-planned-work)

### Next

- R-17: Congressional Record articles as a statement connector, Senate only at first. Each granule and speaker gives one candidate, and the extraction holds only the turns of that speaker. Code filters by class, title, covered member, and about 100 words. The public link is the GovInfo details page. A new quotation kind "Congressional Record text" is a contract change. Backfill to 2025-01-03. The PRs are collection to a ledger table with no model spend, then candidates, then the backfill, then removal of the 62 dead senate.gov listings and the 29 dead sources that fail a 7 day health check. Depends on: none. [Actions](architecture/actions.md#limits-and-planned-work)
- R-29: Check each policy area name against the Congress.gov API. Depends on: none stated. [Configuration](architecture/configuration.md#limits-and-planned-work)
- R-30: Fix the remaining format and parser limits that the federal coverage found. Depends on: none stated. [Collection](architecture/collection.md#limits-and-planned-work)

### Later

- R-25: Public MCP over published releases, and source health. Depends on: R-24. [Publication](architecture/publication.md#limits-and-planned-work)
- R-38: The shared language insight. Code finds the same or nearly the same sentences in releases from different offices, and publishes a method note. A person reviews each finding before publication. Depends on: R-24. [Statements](architecture/statements.md#limits-and-planned-work)
- R-21: Process view of collection and AI stages from retained state. Depends on: R-24. [Host](architecture/host.md#limits-and-planned-work)
- R-18: An engine-health view, built with R-21. The spending total is in R-24. Depends on: R-24. [Collection](architecture/collection.md#limits-and-planned-work)
- R-13: Measure document change, then gate it. Check the House press index pages and about 300 press release URLs daily for 2 to 4 weeks. Count byte, text, and line changes for each source, and label about 100 changes by hand. Add rules for the churn found. Add a model tier only if the changes that pass the rules are too many for people to review. Depends on: the first cloud deployment (R-24). [Analysis](architecture/analysis.md#limits-and-planned-work)
- R-22: Ordered row locks in the job store, after the MVP. Depends on: lock waits measured on a bills-only collection run. [Collection](architecture/collection.md#limits-and-planned-work)
- R-68: Serve public data from tables. The R-63 tables are the only source of truth, and no JSON file carries data to the public path. `PublicApi` reads a `public_api` schema of views, each listing its columns, through a role that can read nothing else. A new insight is a view, one endpoint, and one shared component. The owner chose on 2026-11-06 that `PublicApi` renders every public page from these views, and that Cloudflare caches each page for each release. This replaces the static pages. The browser calls the API for paging, filters, and the reader's own members. Depends on: the vote, action, and release entry steps of R-63. [Publication](architecture/publication.md)
  - [ ] Agree the view design, active release rule, and role limits, with a test that fails when the role can read any other table
  - [x] Add the `public_api` schema, the role `civic_lens_public`, the active release view, the vote views, and the bill views ([public API views](architecture/publication.md#public-api-views))
  - [x] Serve the members of a vote and the bills through `PublicApi` ([search API](architecture/search.md#api))
  - [x] Store the vote member parties, the vote alignment and party majorities, the official photo path, and the entry Congress as columns that C# writes at publication, with `releases columns backfill` ([storage](architecture/publication.md#storage-and-contracts)). The views expose these values; party aggregates remain to add
  - [x] Serve the latest action of a bill, the related records of a record, the facts of a vote, and executive actions through `PublicApi` ([search API](architecture/search.md#api)). Source quotes and source links of a record are not served yet
  - [ ] Measure traffic, query load, and worker effect on the shared database
  - [x] Move the public components to a Razor class library, `CivicLens.PublicWeb`, that references `CivicLens.Publication.Contracts` only. Host and `PublicApi` reference it. The static pages stay byte for byte the same
  - [ ] Add the views and endpoints that the pages need: officials and their summary figures, party boards, issue counts, the home highlights, bill status, related records, record bodies, and filters by official and issue
    - [x] View PR 1: expose the #403 stored columns through the views: `vote_members.party` and `caucus`, `votes.alignment`, `member_votes.side` and `party_majority_cast`, `officials.photo_path`
    - [ ] View PR 2, bills: add `bill_actions`, `bills.public_law`, `bills.crs_summary_text` with its version, and `votes.bill_record_id`
    - [ ] View PR 3: add #400 `issue_votes` and `issue_sponsors`, and the issue party line-up from them and from PR 1
    - [ ] View PR 4, statements: add `statements`, `statement_speakers`, `statement_quotations`, and `record_links` with a `reason` column
    - [ ] View PR 5: add `document_changes` with a changed-region child view, `record_corrections`, and `withdrawals`
    - [ ] View PR 6: add `briefs` paragraphs and citations, `brief_paragraphs` and `brief_citations`
    - [ ] View PR 7, executive actions: add #402 `executive_actions` and `executive_action_notices`, then `executive_action_links` for amends and revoked-by
    - [ ] View PR 8: add `official_party_lines` and `party_agreement`, and the party-break and party-line boards from them
  - [ ] Render pages in `PublicApi` with static server rendering from the views. Cloudflare caches each page for each release: the ETag names the release, build, and normalized public base URL, and `stale-if-error` keeps a copy for 24 hours. Personal parts are HTML fragments after a ZIP, and the ZIP stays on the device
  - [ ] Move the routes one at a time, in the order of the reader flows: home and members, the members strip, votes, bills, official and President, issues, other kinds, then lists, boards, search, and the fixed pages
    - [ ] Shell: split nav (Votes, Bills, Executive actions, Statements, Issues), current-section mark, breadcrumbs on inner pages, header search on every page, footer report link that names the record, and the yellow, ink, and record-type tokens of the [public site](architecture/public-site.md#decisions) theme
    - [ ] Home: featured vote picked by the fixed rule "the latest passage vote on a bill", no repeat in the recent votes list, two House and two Senate votes, one AI label and a scoped split caption for the list, issue counts with their Congress, executive actions grouped by document type with newest title and count, "This week" lead line only when a brief exists, ZIP answer scrolls into view with focus and a busy state
    - [ ] Members: member cards with attendance and party-line counts and a portrait, no bare Yes and No totals, an issue filter on member votes, a "your members" header chip with Forget, and the compare page
    - [ ] Votes: answer first with one "Check the source" fold, no facts list, no timeline panel, one party split rule in bars and fold, chamber and base legends with a majority mark, bill cards with labeled bill lines, status badges, and bill links, "and N more" breakers link, and a folded member table with filters
    - [ ] Vocabulary: one plain name for each vote kind and term, a `Term` popover on first use, approved text for every glossary slot, and a letter index on the glossary page
    - [ ] Bills: status stepper with a "how a bill becomes law" legend, party split of cosponsors, the CRS text in a fold under the AI summary, no cost and date slot, and the official title where no bill line exists
    - [ ] Official and President: one figure for party votes, a vote strip that marks only breaks and missed votes as links, a count row of jump links on the President page, sections hidden when empty with linked-statement messages in the jump row, President speeches hidden until sourced, and bill rows with introduced dates and status badges
    - [ ] Issues: one count line, votes with my members' casts, and the party line-up with the "about the question, not the issue" caveat
    - [ ] Records: relationship reason, quote, and fact in the record row, with the relationship URL kept; statement date rule, event-named row dates and one bill-number format, caveats beside the AI summary check line; no timeline panel on statements and executive actions; brief pages keep text, citations, and records without weak figures
    - [ ] Lists and boards: per-kind list pages with filters and API paging, "All records" and policy area pages out of the nav with old URLs kept, officials list with seat, party, term end, and one labeled figure; board rows label delegates and their bases, and agreement cards appear once per chamber outside party tabs
    - [ ] Search: official first, exact bill number pin, snippets from the summary or bill line, filters after results, and the empty state at the top
    - [ ] Numbers: name the base and time frame of every number and date, and show the newest vote day of each chamber in the freshness line
    - [ ] Add one grade 12 test over page text and AI text, and a "Get this data" link on each table to its API route
  - [ ] After the vote pages move, add an electoral map: one component on its own page and on the vote and home pages. The first layer colors each House district and each half of a state by its member's cast on one roll call, with a pattern as well as a color. A tile view gives each seat the same size. The legend names what each color counts, and a table holds the same data. The server draws the map as SVG from the views, and the Census district boundaries are a static file. Later layers: party breaks, seats up for election, issue activity, attendance, and district lines by Congress. The map shows recorded votes only, never predictions or ratings. Whether it uses party colors is an open owner choice
  - [ ] AI summaries are part of the redesign (owner, 2026-11-06). Each one is a view column with its text origin and label. Each prompt gets a budgeted measure on fixed records before it runs ([R-67](#later)), and code checks each summary against its source. Each summary lands with the page step that shows it:
    - [ ] Home and members: code tables for what a Yes vote means and what happens next, by vote kind and result, and one grade 12 test over page and AI text
    - [ ] Votes: "what this vote decides". The vote prompt gets the bill line, the Congressional Research Service summary, and the plain vote words. Procedure votes name the bill only
    - [x] Bills: the bill line from a model with a checked quote ([bill line](architecture/actions.md#invariants)). Owner next: measure it with `actions line measure`, then run `analyses redraft --stale actions` after deploy
    - [ ] Bills: publish the bill text summary after the owner reads the measured sample, turns the setting on, and backfills older bills
    - [ ] Bills: up to 5 key terms, each defined only by a quote from the bill's definitions section
    - [ ] Executive actions: collect the document body text, then an AI summary that says what the document states, never its motive
    - [ ] Later, after amendment data ([R-58](#later)): the purpose of each amendment
  - [ ] After 2 days with every page on the server and no rollback, delete the static page code and the rewrite options. Publish then writes data only
  - [ ] Write `release.json` only as a public download on a schedule, or remove it
- R-24: The long-term cloud move to a Hetzner CPX21 with containers, Terraform, CI, and live Auth0 verification. A Jetson runs temporary production with Docker Compose, and it serves releases through Caddy and a Cloudflare tunnel. Terraform state never goes to R2. The open host checklist items are its exit checks. Also stop the worker during a job and show that the deployed container recovers it. The search service runs as its own container on the same box. The deploy must set no provider-specific `Logging` levels for the search service, because such a rule overrides its request log filter. R-24 also holds a monthly restore test, worker service management, and the documented spending total. The owner withdraws with Take off site on the record editor, and the CLI stays. The runbook still holds a tested `releases publish --withdraw` command that runs on the cloud host. The coverage file is 814 KB of the 1 MB limit of the shared configuration loader. Raise the limit before R-24 only if the file would pass 1 MB before the deploy. Depends on: the exit criteria of the first deploy. [Host](architecture/host.md#limits-and-planned-work)
- R-26: Read-only local MCP inspection. Depends on: none stated. [Review](architecture/review.md#limits-and-planned-work)
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
- D-17: Whether the talk and action counts of a President include executive actions. Today they do not. [Executive actions](architecture/executive-actions.md#limits-and-planned-work)

## Product direction

The audience is regular citizens who want transparency in U.S. national politics. State and local coverage are possible future extensions. Readers start with reviewed accounts and can inspect the evidence behind them. The editorial flow and the restrained monochrome identity are agreed.

Data is the product. The product is only as good as its data, so data and database schemas are a first class feature. Design them as deliberately as any public page. The goal is rich links between people, records, bills, votes, and issues, so that we can derive insights no single source shows. This may become a graph.

Design for scale. Each record carries rich detail, and coverage of officials, sources, and issues is broad. Automate every step that does not need human judgment.

Each page answers more questions than it opens (owner, 2026-11-06). Where a bill, a vote, an action, or a term is ambiguous or technical, the page gives an AI summary in plain words. The reader flow decides where a summary goes. Each summary carries its AI label, code checks it against its source, and it reads at grade 12 or below with the exact terms explained.

Derived insights are product goals. They make Civic Lens more than a connector to source APIs. Code computes each metric and publishes a method note. A person reviews any judgment before publication, and each insight shows its coverage limits.

1. Changed record: detect deleted press releases, rewritten quotations, and positions removed from issue pages. Show both versions and their timing next to votes and events. This needs deletion detection and the document-change gate.
2. Shared language: find the same or nearly the same sentences in releases from different offices. Show coordinated messaging, how it spreads, and who uses it first.

## Deferred

- News coverage in briefs: deferred until we measure how much of the real record a set of news feeds covers. Add coverage links and a gap signal only if that recall is high. Store feed entries only, never full articles.
- Congress.gov member records and a waiting window before sponsorship revisions: see [actions](architecture/actions.md#limits-and-planned-work) and [action decisions](architecture/actions.md#decisions).
- Self-hosted encoder models: see [analysis](architecture/analysis.md#limits-and-planned-work).
- Browser rendering, OCR, and cross-origin robots redirects: see [collection](architecture/collection.md#limits-and-planned-work) and [robots](architecture/collection.md#robots-and-pacing).
- Shared line-format rules for renderers: see [bills](architecture/bills.md#limits-and-planned-work).
