# Council role 4: Reader and product adversary

Read briefs/council-common.md on this branch first (git show origin/cloud-briefs:briefs/council-common.md). Follow it exactly. This file names your angle.

Role name: Reader and product.

Read: docs/post-r68/README.md, reader.md, delivery.md, claims.md (reader-facing parts), docs/architecture/public-site.md, and the mockups in docs/post-r68/experiments/review-graph/ and docs/post-r68/experiments/evidence-trials/reader-study.html. You may serve them from /tmp copies with python3 -m http.server and capture them at 360 and 1280 px in light and dark with Playwright (npm i playwright@1.49.1 in /tmp; npx playwright install --with-deps chromium). Look at each capture yourself. Skip captures if the install fails, and say so. You may load a few pages of the live public site at https://civic-lens.info to see the shipped product, no more than 20 requests, 3 seconds apart.

The product goal: a citizen follows a public claim through a decision to a measured outcome. Public text reads at about grade 12, says what each thing is and why it matters, and labels every number with who and what it counts and its time frame. AI text carries a label.

Attack these:
- Reader value. For each phase, name the reader question it answers. Flag work that answers no reader question, or answers one readers do not ask.
- The quote-to-vote link. A link from a statement to a vote can mislead even when each part is true, for example a vote on a procedural motion or on a large bill with many provisions. Check how the reader view shows the limits of each link and the "insufficient evidence" state.
- Navigation and homepage. Check that the proposed left navigation and changing homepage fit 360 px, keyboard use, and screen readers. Check whether a changing homepage can look like editorial selection, and how the plan keeps the selection rule neutral and visible.
- Comprehension study. Check whether the planned reader test measures what the decision needs. Propose a cheap way to recruit first-time readers and a task script.
- Missing features. Propose reader features the plan misses. Examples to consider (keep only those with evidence of value): "what changed since my last visit", a member's promise-versus-record page, plain alerts by member or issue, a printable or shareable evidence card, an "ask about this record" path that cites only published evidence, a correction log, and local impact by ZIP. For each, give the reader question, the data that exists today, and the risk.

Give each finding the reader harm in one sentence.
