# Review of PR #501 (head 6c53684e)

Move the issue page to server rendering at /web/issues/{issueId}.

## Verdict

The page is mostly correct, but one P1 must be fixed before merge. It is a misleading badge in the members section.

## How I checked

- I rendered the real server page with a temporary xunit test. It used the endpoint test host and the seed helpers of the page's own endpoint tests.
- I seeded a full case: 12 votes, 7 bills, a line-up, and the scope panel. I seeded an empty case with a very long issue name.
- I loaded the real `view=issue` members fragment through `public.js` with a saved ZIP.
- I captured the page and `docs/design/mosaic-v2/issue.html` at 360 and 1280 px, light and dark. I looked at every capture. No capture has horizontal scroll.
- The Release build, format, and the diagram check pass. These PR test classes pass, 139 tests in all: IssuePage, IssueMemberVotes, ZipMemberCards, SharedVoteRows, HomePage, and Architecture.
- I deleted the temporary test.
- Not done: the Impeccable context launcher was blocked as external code, so its detector did not run. I applied its SKILL.md and its critique and audit references by hand. I used the Playwright that the environment already has, not a new install.

## P1

### P1-1. Single-member rows show "Voted differently" with no comparison

- What the reader sees: each senator has a section. Every row in it says "Voted differently", but the section shows only that senator's vote. Nothing names the other member, so the badge is misleading public data.
- Cause: `src/CivicLens.PublicWeb/Publication/IssueMemberVotes.razor:27-29` (`OfMember`) copies each vote with `vote with { Casts = ... }` and keeps the `MembersVotedTheSame` value of the group. `src/CivicLens.PublicWeb/Publication/SharedVoteRows.razor:32` prints the badge whatever the value of `SingleMember`. The doc comment of that parameter says that a single-member layout has "no comparison", so the component breaks its own rule. The official page can show the same badge if its data ever carries the field.
- Fix: in `SharedVoteRows.razor:32`, show the badge only when `SingleMember` is false. Then the rule keeps one owner. Add an `IssueMemberVotesTests` case: two senators with different votes, and no `.shared-vote-comparison` in either section. No current test can catch this defect.

## P2

### P2-1. The bill rows have no sponsor

- The mockup row shows "Sponsor: {name}" next to "Latest sponsorship on ...". The rendered row shows the headline, the label, the date, and the bill line only. This difference is not in the approval list.
- Fix: add the sponsor to the `PageBillSet` read in `src/CivicLens.PublicApi/Issues/IssueStore.cs`. Put it in `Meta` at `src/CivicLens.PublicWeb/Publication/IssuePage.razor:81`, from the sponsorship that the line-up uses, so the party rule keeps one owner. "Sponsor:" is mockup text that is not marked EXAMPLE.

### P2-2. The reader cannot get to the other bills or votes

- The page says "The 5 newest of 7." (`IssuePage.razor:86`) and "The 10 newest of 12." (`IssuePage.razor:68`), but it has no link. The mockup has "See all 186 bills, newest first."
- The static full list is at the canonical path of this page, so after the cutover the rest of the list has no URL.
- Fix: link to the list route. If no list route exists yet, record this as planned work in `docs/roadmap.md`.

## P3

### P3-1. Split-vote line with k = 1 and n > 1

- The page prints "1 of those 2 votes were split votes." The plural form follows the approved mockup, so I do not change it, but k = 1 reads wrong.
- Code: `IssuePage.razor:18`.
- Open question for the owner, exact text: "Is '1 of those {n} votes was a split vote.' approved?"

### P3-2. Doc comment on the wrong member

- In `src/CivicLens.Publication.Contracts/PublishedWording.cs:12-17`, the new constants sit between the `BillLineSource` summary and `BillLineSource`. That summary now documents `IssueIncludesLabel`.
- Fix: move the three new constants above that summary.

### P3-3. The tabs have no current state

- The mockup sets `aria-current` and an underline on the active tab. The page has neither.
- Fix: mark "Your members" by default in `IssuePage.razor:33`, or follow the hash. The hover style already exists in `public.css`.

### P3-4. The members fragment is not checked against the page

- The fragment is not narrowed to the release-day Congress, so it can show older votes than the page list. Each row names its Congress, so the data is not wrong.
- `memberFragment` in `src/CivicLens.PublicWeb/wwwroot/js/public.js` does not compare `data-release` with the release of the page. After a new release, the members section can come from a different release than the page above it.
- Fix: pass `count_congress`, or note this as planned. Check the release in `initIssueMembers`.

Also, at 360 px a very long issue name fills three h2 headings of about 6 lines each. This is acceptable, but a shorter fixed noun in the h2 reads better. Any such text needs owner approval.

## Shared owners

- Line-up: one `PartyBar`, built by `IssueLineUpBar` with `PartyTones`. The order is Democrat, then Republican, then unknown. The fills are solid, striped, dotted, and open, so color is not the only cue. It is hidden when `lineUp` is null or has no bills. The approved strings "{Party} sponsor" and "Sponsor party not known" are correct. The stored term party is "Democrat", so the legend reads "Democrat sponsor". This follows the approved pattern. The figcaption "Bills tagged {Issue}" has no time frame, but the intro paragraph just above it names the Congress.
- Members section: it uses the single-member rows of `SharedVoteRows` with `HeadingLevel` 3. The headings go h2, h3, h4 correctly. It does not copy `PartyBar`.
- Record row: `PublicRecordRow` is the only row for Home and this page. It shows the label from the view label column. The CRS source note goes inline with the bill line. The `BillLineSource` field of Home went with it.
- Loader: `memberLoader` is the only loader. `followMemberSlot` wraps it for the strip and the issue page, and `initZipLookup` calls it directly. The stale-ZIP guard behaves as before. The issue view checks the ZIP, the issue, the view, and an empty `sinceRelease`. The server rejects `view=issue` without an issue, or with `sinceRelease`.
- Privacy: the shared HTML holds no ZIP. The members slot carries only the issue and the API origin.
- Copies of shared owners: I found none. The includes, excludes, and tags-anchor constants moved to Publication.Contracts, and the static page now uses them.

## PR body and strings

- PR body: every claim matches the code. The body says "Not run: a browser test of initIssueMembers". I ran it by hand and it works. In my setup the header strip showed "The lookup failed", but my stub server caused that, not the PR.
- New public strings not yet approved: none that I found. The vote caveat and the empty-section lines are in the approval list. "No current member is in our records." and "Try again" already exist on main. "Try again" now also shows on Home and Compare lookup failures. That is new behavior with an existing string.
- Open questions: the PR body has no "Open questions" section. If P1-1 needs no new string, P3-1 is the only question to add.

## Mockup differences judged as approved departures

- The count line in prose in a dark hero, in place of the four figure cards. The count line scoped to the release-day Congress is approved.
- "Split vote" as a `Term` popover, in place of the gloss marked EXAMPLE. Approved.
- Per-row AI labels from the view label column, in place of the single AI box of the mockup above the bills. The standing rule says the label comes from the view column.
- The list of the votes of each member, in place of the member-count rows marked "example counts". The PR uses the single-member `SharedVoteRows` that the brief asks for.
- A real `PartyBar`, in place of the line-up block marked EXAMPLE.
- The ZIP prompt text and the includes and excludes panel in a card. Both are approved.
- The page has no "Records as of" time and no vote threshold marks. Both are out of scope by owner decision.

## Impeccable notes

- The page is easy to read and calm. The hierarchy is clear, there is one h1, and the landmarks are correct: a "Site" nav, a breadcrumb, "On this page", and a footer.
- Contrast: the hero caveat is about 11:1. The tabs are about 7:1 in light mode and also pass in dark mode.
- Keyboard and touch: the tabs show a focus ring, and tap targets are at least 44 px.
- Cognitive load: the members section is the main problem. With three members it is the longest block on the page, and the sections of the two senators repeat the same four headlines.
