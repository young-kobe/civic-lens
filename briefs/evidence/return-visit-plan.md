# Return visit features: design plan (2026-11-14)

Drafted by a planning agent from origin/main and the production UX review. Owner decisions are pending; nothing here is approved until the owner answers.

## Findings
- The brief pipeline cannot cite votes or executive actions: BriefCitationKind is { DocumentChange, Statement, Action } (src/CivicLens.Core/Briefs/BriefCitationKind.cs), one column pair and composite foreign key per kind. A person approves every brief. Production holds 1 brief revision and no published brief. The mockup brief.html cites executive actions, which the code cannot do.
- Shared pages must not use the wall clock: PageCache promises that a page of one release never changes (release ETag). The private ZIP fragment is private, no-store, so it may use the clock.
- One owner for party breaks: public_api.member_votes.breaks_party owns the break rule and covers Senate votes. VotePageView.Breakers uses party tallies, which Senate pages lack (P1-8). Words come from VoteBreakers.AgainstMost.
- voted_on and signed_on are Washington dates; a Monday-start America/New_York week (BriefWeek) needs no conversion.
- Features 1 and 2 need no new public_api view.

## "This week" options
- W1 clock week in New York: breaks the ETag rule on shared pages; private fragment only.
- W2 the Monday to Sunday week that holds the newest published roll call of each chamber (the freshness line anchor: HomeStore, ActiveReleaseReader.FindFreshnessAsync). Fixed by the release; during a recess shows the last week with votes under exact dates.
- W3 first publication (record_first_publications, vote_first_publications): never misses a late record, but a vote can show in a later week than it happened.
Recommendation: W2 for features 1 and 2; closed BriefWeek bounds by event date for feature 3. Headings print dates ("House votes, 9 to 13 Nov 2026"). A small week helper in Publication.Contracts (BillStatusMeaning precedent).

## Feature 1: "This week: how your 3 members voted"
- Top of the Home ZIP results, above member cards (also fixes P2-11). One SharedVoteRows list-mode row per vote with each member's cast chip, chamber PartyBar, split badge; a cast against the party gets the VoteBreakers.AgainstMost words; one caption names the scope ("Your 3 members, 12 roll calls, 9 to 13 Nov 2026, 2 votes against most of their party"); optional "New" mark after the device marker.
- Data: add breaks_party to the shared vote read (MemberVoteStore, SharedMemberVoteCast); a server-only week filter (precedent MemberVoteRequest.Congress); anchor read in the same RepeatableRead snapshot as ZipDistrictStore.FindCoreAsync.
- Privacy: only in the default view of /v1/zip-districts/{zip}/members (private, no-store). The ZIP never goes into a page URL or feed.
- Effort M. Risks: duplicate rows with the existing recent-votes list; empty week needs an empty line.

## Feature 2: "Broke ranks this week"
- Home section next to the break board. Option A by vote: newest split votes of the anchored week (up to 4), vote row, PartyBar, split badge, up to 5 linked breakers with party and seat, "and N more" to the vote page. Option B by member weekly tally ("2 of 7 split votes"): brings back the small-base problem (P1-5).
- Data: member_votes by breaks_party, votes.alignment split, voted_on; names and seats from release_officials and release_official_terms; joins the HomeAnswer snapshot so a 304 skips it; EXPLAIN at production size (#563 precedent).
- Public, shared page cache with release ETag. Effort S to M. Depends on the P1-5 seat fix. Risks: breaks_party and VotePageView.Breakers can disagree (pick one owner); ties show no breakers.

## Feature 3: weekly brief, RSS, email
- (a) Revive the brief pipeline: two new citation kinds (migration, foreign keys, staleness, publication), weekly person approval (the bottleneck today), a new hard grade check. Effort L.
- (b) Code-only digest page weeks/{monday}.html (no query; sitemap lists it), server-rendered per closed week: roll calls per chamber with passage results, new laws, executive actions by signed_on, the feature 2 component. All code text with the No AI chip; every number names who, what and the week. New laws may need public_api.bills.became_law_on, else bill_actions through the stepper owner. Late Senate votes and Federal Register documents are added on arrival with a note. Effort M.
- (c) Both: (b) first; later one optional AI paragraph from a person-approved brief, limited to statements and document changes (facts the code digest lacks); no new citation kinds.
- RSS: /weekly.xml, RSS 2.0, newest 12 closed weeks, guid = page URL, description = code summary; served like the sitemap (ServerPageResponder.RespondFile, release ETag, shared PageCache, 304); autodiscovery link in PublicPage. No per-ZIP feed. Per-official feeds could come later.
- Email out of scope now: PublicApi reads only public_api views with Npgsql; a subscriber list is personal data needing write storage, consent, unsubscribe, a mail provider: a new boundary.
- Home links the digest from the existing "This week:" lead line (DR5).

## PR order
1. (S) breaks_party on shared casts, break mark in SharedVoteRows, week helper.
2. (M) Feature 1.
3. (S to M) Feature 2, after P1-5.
4. (M) Digest page, its list (briefs.html or new path), sitemap, home lead line.
5. (S) RSS.
6. Later (L, optional) the brief paragraph.

## Decisions for the owner
1. Week definition: W1, W2 or W3. Recommend W2 for Home; BriefWeek bounds by event date for the digest.
2. Feature 1 versus the existing Home recent-votes list: keep both or replace on Home (Compare keeps its list). Recommend replace.
3. "New since your last visit" marks: add (reuse vote_first_publications) or count notice only. Recommend add.
4. Feature 2 shape: A by vote or B by member. Recommend A.
5. Break rule owner: breaks_party everywhere or keep the vote page rule. Recommend breaks_party; move the vote page in a separate PR.
6. Feature 3 path: (a), (b) or (c). Recommend (b) now, (c) only with a weekly review commitment.
7. Digest dates: event date with late additions or first publication. Recommend event date with a printed note.
8. New laws date: new became_law_on column or read bill_actions. Recommend the column if EXPLAIN shows a slow scan.
9. Digest location: reuse briefs.html and nav "Weekly briefs", or new weeks.html. Recommend reuse briefs.html.
10. Email: now or later. Recommend later, as a separate boundary decision.
