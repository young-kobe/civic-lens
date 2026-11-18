# Phase 3. Reader experience

## What a visitor should learn

Lead with one dated development and a sentence explaining its consequence. On the Paine vote, the first screen should identify the memorial, the question of its location, House passage and the next known stage. Let the reader open the sponsors' words, find their member's vote or inspect the full tally. On veterans' benefits, place the quoted claim beside the dated measure, the computed result, and the remaining question. [Pilot cases](claims.md).

## Proposed navigation

Branch-first navigation teaches institutional roles and helps someone locate a known record. Issue-first navigation serves topical browsing, but gaps in issue tagging can hide records. Test both against the recommended hybrid below. Reader questions organize the main destinations. Branches organize the evidence library.

| Left navigation parent | Children or destination |
|---|---|
| Overview | What changed, latest substantive updates, your members |
| Issues | Recently changed issues, issue timelines, all tracked issues |
| Claims and actions | Reviewed assessments, commitments and deadlines, latest evidence |
| People and comparisons | Your members, officials, coalitions, chamber and party comparisons |
| Evidence library | Legislative with bills, votes and amendments as available. Executive with orders, proclamations and notices. Cross-branch statements and document changes. Add Judicial once case records and coverage exist. |
| Methods and coverage | How a finding was checked, data freshness, definitions, corrections and AI evaluation |

Give each parent a labeled icon and a separate chevron button when it has children. Draw a thin line from the parent to the last child, with short horizontal branches. Expose the active path and open its ancestors on a deep link. The parent label opens the section. The chevron expands or collapses it and reports its state to assistive technology. Support keyboard focus, Enter and Space, high contrast and large touch targets. On mobile use the same order in a drawer and return focus to its control when it closes. Use navigation landmarks and nested links. [W3C disclosure navigation with parent links](https://www.w3.org/WAI/ARIA/apg/patterns/disclosure/examples/disclosure-navigation-hybrid/).

Retain the Mosaic palette and typography for the trial. Use a persistent left rail, a central reading column, and a narrower panel for dated evidence and sources. On mobile put evidence directly after the finding. Use labeled action pills such as **Follow this bill**, **Read the statement** and **Compare votes**. Keep long headlines readable and give one primary action visual priority. Start browsing pages with concrete questions drawn from available findings. Search and complete directories remain available to readers who arrive with an identifier.

## What leads each page

| Page | First useful answer | Deeper detail |
|---|---|---|
| Legislative activity | What a proposal would change and which step just occurred | Distinct stable bill and vote pages, amendment text, individual casts and procedure. Nominations have a vote path even without a bill. |
| Official and comparison | A dated public statement beside a relevant vote or sponsorship | Time-bounded term and party, complete eligible base, missing casts and exact sources. |
| Issue | What changed across branches, what is known about implementation, and what is pending | Separate events from measured outcomes, with dated links to both. |
| Executive action | Who must do what and by when | Directive text, statutory basis when identified, deadlines and observed follow-up. |
| Map | Answer a geographic question such as how a reader's delegation split | Retain the full roll call and a non-map equivalent. On an issue with no geographic question, lead with the timeline. |
| Brief or document change | Which cited fact or passage changed since the last update | Exact old and new wording, date uncertainty, and underlying release records. |

## Replace the fixed featured vote

Today Home selects the newest published vote classified as Passage with a bill type and number. The Paine vote qualified for recent votes under Suspension. In the sampled pages H.R. 2102 held the featured slot. [Current selection code](../../src/CivicLens.PublicApi/Home/HomeStore.cs).

Pilot a **What changed** feed. Eligible events include a bill advancing a stage, a new outcome measurement, a reviewed connection, or a correction. A due date gets a dated reminder with an explicit follow-up status. Use the event or evidence date, stable ID as tie breaker and one entry per material change. Label late-arriving evidence and corrections with their publication date. Give each item a visible reason for selection. Show the most recent dated brief in quiet periods. Keep as-of dates and remove withdrawn dependencies. Replay two releases with repeated events, missing evidence, a quiet week and a correction. Check how often one topic or chamber dominates before adding any diversity rule.

## Decision test

Give first-time readers the same three tasks with the branch-first and hybrid prototypes. Alternate their order. Ask them to explain what roll call 325 decided, locate their own member's action, and name what the backlog figures establish. Record correct answers, first click, time, evidence opened and points of confusion. Repeat keyboard-only and narrow-screen tasks. Check names and tallies with a screen reader. Choose the navigation after seeing where people succeed. Capture the resulting requirements in the [public site reference](../architecture/public-site.md) when the design is accepted.
