# Agentic ecosystem — design proposal

> Draft (pre-Postgres-rewrite, 2026-04); machinery references are stale.

Status: draft for human decision. No code changes in this branch.

## 1. Executive summary

The honest answer to "should Civic Lens build an agentic ecosystem?" is **mostly no, in a small targeted yes**. The candidate list, evaluated against what Civic Lens already has, breaks down as:

- **Skip** custom code-audit, security-audit, and bug-ticket agents. CI already runs `pip-audit` + `npm audit --audit-level=high` on every PR (`.github/workflows/ci.yml`); Dependabot, CodeQL, and Sentry cover the rest of that surface for $0/mo and zero LLM tokens. Building custom agents here is paying for what GitHub gives away.
- **Build** one **pipeline anomaly agent** as the first and probably only custom agent in this proposal. It is the one job no off-the-shelf tool does — it understands `docs`, `ai_outputs`, `narratives`, the scoping rule that x_post + reddit_* are SOCIAL_PLATFORMS, and the existing `civic-lens-alert@` SMTP path. Estimated ~$2–4/mo of Gemini Flash on top of the current ~$10–15/mo budget. Fits as a 10th stage in `job_runner.run_full_pipeline()` plus a small detector module.
- **Reference, don't duplicate**, the fact-check agent — `docs/proposals/fact-check-agent.md` is its own proposal and is out of scope for this doc.
- **Defer** the bug-ticket agent until we have a real signal source (Sentry or journald error stream) to feed it. Without that input, an LLM agent is just a noisy summarizer.

Recommended infra: **plain cron + script, hung off the existing systemd timer fabric**. We already have `civic-lens-analyze.timer`, `civic-lens-alert@.service`, SMTP wiring, and the `_run_stage` budget+isolation pattern. Adding a workflow engine (Inngest, Temporal, LangGraph) buys nothing on a single Hetzner box and adds an external dependency to the deploy story.

Smallest useful version, shippable in ~2 weeks: a `PipelineAnomalyDetector` class that reads recent rows from `docs` + `ai_outputs`, computes a handful of distribution checks, summarises anomalies via Gemini, and routes the verdict through `analysis/src/common/alerts.py::send_alert`. No new infra, no new dependency, no new deploy surface.

## 2. Per-agent evaluation

### 2.1 Code audit agent

> Periodic review of codebase for quality issues, anti-patterns, dead code.

**Recommendation: do not build a custom agent.** Mostly solved by free tools; the part that isn't is genuinely hard for an LLM to do well.

Existing tooling already in this repo:
- Go: `go vet` runs on every PR and pre-deploy gate.
- Python: tests run; no linter is configured. `ruff` would close most of the gap for free.
- TypeScript: `tsc --noEmit` runs on every PR.

What the candidate agent could add:
- "This module looks like dead code" — partly served by the pre-existing `docs/todos/dead-code-cleanup.md` file. A periodic Claude review on a `dead-code-sweep` schedule **would** catch human-readable cleanup that grep can't (e.g., "this aggregator is never wired up by `save_snapshots`"). But the same outcome is reachable with a quarterly manual `/ultrareview` and a few hours of a human reading the audit trail.

The value-per-dollar is low because the codebase is small (~10k Python LOC, ~3k Go LOC, ~5k TS LOC) and the audit-trail discipline already enforces forward-looking documentation. An always-on agent would mostly produce false positives ("you should consolidate these 3 helpers") that the maintainer will dismiss. **Skip.**

If a code-audit agent is desired anyway, the simplest credible implementation is a **GitHub Actions job that runs `claude` on PRs touching >200 LOC**, posting suggestions as a PR comment. Gated on diff size keeps cost trivial (~$0.20–0.50 per fired review, maybe 5–10 fires/month → $1–5/mo). No long-running infra.

### 2.2 Security audit agent

> Scan for vulnerabilities, exposed secrets, unsafe dependencies.

**Recommendation: do not build.** Already covered.

- `pip-audit --strict` runs on every PR (`ci.yml` line 26–29).
- `npm audit --audit-level=high` runs on every PR (`ci.yml` line 68–72).
- The deploy gate re-runs both (`deploy.yml` lines 41–55) so a direct merge to main can't ship a regression.
- The `04_19_2026_security.md` audit and `deploy/` configs (firewall, fail2ban, SSH hardening, Caddy + Cloudflare) cover the runtime side.

What's missing from the existing setup:
- **GitHub Dependabot** and **GitHub secret-scanning** are not currently enabled (no `.github/dependabot.yml`). Both are free and zero-maintenance. Turning these on closes the gap without writing any agent code.
- **CodeQL** for Python/Go/JS is free for public repos. One workflow file, no LLM tokens.

An LLM agent adds nothing on top of these except higher false-positive rate and token spend. **Skip.** The action item for this slot is "enable Dependabot, secret-scanning, and CodeQL" — not "build an agent."

### 2.3 Bug-fix agent that opens tickets

> Detects issues (failing tests, runtime errors, anomalies) and files structured tickets for human review.

**Recommendation: defer until there is a signal source to consume.** Build only in service of a specific failure stream.

The premise of this agent is that there's noise in some failure stream that a human shouldn't have to read directly. Right now Civic Lens has:
- Test failures: surfaced by `ci.yml` PR status. Fine. Already structured.
- Runtime errors: no structured collection. Errors land in journald on the Hetzner box. The `civic-lens-alert@` template emails 50 lines of journal context on systemd unit failure (per `2026-04-23-analyze-pipeline-resilience-and-failure-alerts.md`). That's "the operator gets emailed once when something explodes" — not a stream that needs filtering.
- Anomalies: this is what §2.5 is about; folding it under "bug ticket agent" muddles the design.

For the bug-ticket agent to be worth building, one of these has to happen first:
1. We adopt **Sentry** (or self-host Glitchtip) so unhandled exceptions become a structured stream with frequency/grouping. Then a Gemini agent could batch the top errors weekly and open structured GitHub issues.
2. The journald stream gets noisy enough that a human can't triage it without help.

Until then, the existing `OnFailure=civic-lens-alert@%n.service` wiring is the right level of automation: failure → email with journal tail → human investigates. **Defer.** Estimated cost when built: ~$1–3/mo on Gemini Flash batching weekly digests; ~zero infra cost if it runs as a GitHub Actions cron consuming Sentry's API.

### 2.4 Fact-check agent

**Recommendation: see `docs/proposals/fact-check-agent.md`.** Not duplicated here; this proposal does not gate on its outcome.

One coordination note: if the fact-check agent ships, the **pipeline anomaly agent (§2.5) should treat fact-check coverage and confidence distributions as additional inputs**. That's a one-line extension once both exist; nothing to design upfront.

### 2.5 Pipeline anomaly agent

> Detects when ingestion volume drops, scoring distributions shift, or downstream tables go empty.

**Recommendation: build this. It is the only candidate in the brainstorm with no off-the-shelf substitute.**

The reasoning:
- Civic Lens's correctness invariants (`docs/INVARIANTS.md`) include things like *every doc traceable to raw_hash*, *AI outputs carry confidence*, *no proxy presented without framing*. These are domain-specific properties no generic monitoring service knows about.
- The existing operator-pain story is real: per `2026-04-23-analyze-pipeline-resilience-and-failure-alerts.md`, prod served stale data for 2 days because snapshots silently didn't run. The pipeline's resilience layer fixed *that* class of bug, but doesn't catch the other class — pipeline finished green, but `narrative_docs` is suddenly empty, or sentiment distributions inverted overnight, or X ingestion silently fell to zero because the bearer token expired.
- The data is right there in `data/civic_lens.db` and `data/cache/*.json`. Aggregators are pure SQL → JSON. A detector is the same shape, plus an LLM call to write the human-readable verdict.

#### Trigger and runtime

- **Trigger**: tail end of `job_runner.run_full_pipeline()`, after `save_snapshots()`, in the same systemd-driven 4×/day cadence. Reuse the existing `_run_stage` wrapper for failure isolation and budget guarding.
- **Runtime**: same Python venv as the rest of the analysis layer. No new container, no new process, no new timer.
- **Tools/APIs needed**:
  - Read SQLite: row counts and distribution snapshots over recent windows.
  - Read prior snapshot: store the last anomaly-detector summary as `data/cache/anomaly_state.json` so the detector can compare today vs. yesterday/last-week.
  - LLM (Gemini Flash via existing factory) for the *verdict-writing* step only. Detection itself is deterministic SQL.
  - SMTP via `analysis/src/common/alerts.py::send_alert` for routing alerts to the operator.
- **Expected output**: a structured JSON record (status: `ok` | `degraded` | `alarm`, list of triggered checks, brief LLM-written explanation) saved to `data/cache/anomaly_state.json` and surfaced via a new `/api/health/pipeline` endpoint. Alarms also email.
- **Human review loop**: operator-only — the email links to the cache file and to the relevant SQL the detector ran. Verdicts can be acked by editing a `data/anomaly_acks.json` allow-list (e.g., "X ingestion is zero because the bearer token rotation is in flight"). No PR/issue creation in v1.

#### Checks to implement (concrete, not speculative)

These are SQL-shaped; no LLM needed for the detection step itself:

1. **Per-source ingestion volume**: rows in `docs` partitioned by `source_type` over the last 24h vs. the trailing 7d median. Alert when any source drops >75% or goes to zero. (Catches the X bearer-token-expired class of bug.)
2. **Per-stage AI coverage**: ratio of docs ingested in the last 24h that have a row in `ai_outputs` for each task_type. Alert when coverage < 80% for a stage that ran. (Catches silent partials that the existing `partial` email already covers, but with stage-specific context.)
3. **Confidence-distribution drift**: mean confidence per task_type, last 24h vs. trailing 30d. Alert on >2σ deviation. (Catches "the prompt regressed" class of bug.)
4. **Narrative coverage**: count of `narratives` with `narrative_docs` rows in the last 7d. Alert when this falls below the trailing-30d median by >50%. (Catches clustering thresholds drifting out of useful range.)
5. **Cache freshness**: every key in `data/cache/` was written within the last 12h. Alert otherwise.

The LLM step takes the structured triggered-checks list and writes a 4–6 sentence summary an operator can read on a phone. This is exactly the shape of work LLMs do well: structured input → human-readable output. It does **not** make the alert/no-alert decision; that is rule-based, auditable, and reproducible.

#### Cost estimate

Real numbers from this repo:
- Gemini 2.0 Flash via `aistudio.google.com`. Current monthly spend ~$10–15 (per the propaganda cost-optimization audit). Loader batch capped at 200/stage × 4 fires/day = 800 docs/day; LLM calls per fire ≈ 200 × 4 stages = ~800 calls.
- The anomaly agent adds **one LLM call per pipeline fire** — 4 calls/day × ~30 days × ~$0.0005/call = **~$0.06/mo**. The detection SQL adds milliseconds per fire on a DB this size. Storage: a few KB of JSON state.
- Total marginal cost: rounds to **<$1/mo**. Well below noise.

Realistic ceiling, including pessimistic prompt growth and an extra "weekly summary digest" email: **~$2–4/mo.**

#### Risk assessment

- **False positives**: the most likely failure mode. Mitigated by (a) deterministic detection rules with explicit thresholds (no "LLM decides"), (b) the `anomaly_acks.json` allow-list so operators can suppress known-good states, (c) initial `OnCalendar` setting matches the existing 4×/day analyze cadence, so any noise pattern shows up fast and is easy to tune.
- **False negatives**: the detector misses a real anomaly. Acceptable for v1 — the existing `civic-lens-alert@` path catches systemd-level failures already. The anomaly agent is *additive* coverage, not the only safety net.
- **Runaway loops / cost blowout**: not possible by construction. The detector runs once per pipeline fire, makes one bounded LLM call (verdict-writing on a fixed-size structured input), and exits. There is no loop.
- **Misbehaving alerts blocking the operator**: SMTP rate-limited at the Gmail-app-password level (~500/day); a buggy detector firing every fire produces 4 emails/day, which is annoying but not destructive. Add a simple `last_alert_hash` dedup so identical states don't re-page within 24h.

## 3. Pipeline-internal agentic-fit assessment

> Are there places in the existing analysis or ingest pipeline where agentic behavior would meaningfully outperform the current deterministic flow?

Honest answer: **mostly no, and the places where it might help are not worth the disruption right now.** The pipeline is deterministic by design — that's what makes it auditable per `INVARIANTS.md`. Replacing parts of it with planning/tool-use loops trades that auditability for flexibility we don't currently need.

Specific places worth naming:

1. **Citation extraction** (`engine/citation_extractor.py`) — already deterministic. Adding planning here would only hurt. Leave alone.
2. **Bot detection** — hybrid (heuristic + LLM). Already does the right thing: deterministic pre-exclusions (`verified_type=government`), heuristic features, single-call LLM, with confidence + indicators surfaced. An "agentic" version would add tool-use to look up account history, but that's a feature add, not a paradigm shift; if we want it, write the lookup as a deterministic enrichment step (already done — see `_enrich_x_metadata`) and feed it to the existing classifier.
3. **Claim extraction → narrative clustering** — the rough place where agentic behavior *could* help is **claim canonicalization**: deciding when two claim phrasings are the same claim. Today this is Jaccard or cosine on embeddings. A planning agent could ask "is the entity 'Trump' the same as 'the president' in this context?" and resolve case-by-case. But this is the slipperiest possible slope toward fabrication: an LLM "deciding" two claims are the same is exactly the kind of step the invariants forbid presenting without framing. **Don't.**
4. **Retries inside LLM calls** — already handled at the SDK + factory level (`llm/gemini.py` retries on 429s). The `_run_stage` wrapper handles per-stage failure isolation and budget. There is no "retry with planning" gap.
5. **Ingestion error recovery** — the Go crawler's frontier state machine (`QUEUED → INFLIGHT → DONE|FAILED`, `INFLIGHT` reset on startup) is already crash-resumable. An agent layered on top would solve nothing this design doesn't already solve.

The one place I'd revisit later, after the anomaly agent is in production, is **adaptive batch sizing**: an agent that watches per-stage latency and dynamically sets `loader_batch_size` so the budget guard doesn't have to skip stages. But this is an optimization on a system that already degrades gracefully — not urgent, and easily replaced by a feedback-loop heuristic (no LLM needed).

**Summary**: deterministic code wins for everything in the current pipeline. The anomaly agent works *around* the pipeline, not inside it.

## 4. Infrastructure options comparison

The relevant facts about the current stack:
- One Hetzner VPS, deployed via SSH from GitHub Actions on push-to-main (`deploy.yml`).
- Systemd timers run scheduled work: `civic-lens-analyze.timer` (4×/day), `civic-lens-crawl.timer`, `civic-lens-x.timer`, `civic-lens-backup.timer`.
- SMTP alerting wired via `civic-lens-alert@.service` (template) + `analysis/src/common/alerts.py::send_alert` for runner-side partials.
- Python venv at `analysis/.venv`, single SQLite DB, content-addressed raw storage on disk.

### Option A — GitHub Actions + Claude Code in CI

**Pros**: free for our usage tier; perfect for PR-triggered audits or scheduled jobs that don't need DB access; no new infra to maintain; secrets already there for the deploy key. Best fit for a *code-audit-on-PR* agent if we ever want one.

**Cons**: no access to `data/civic_lens.db` (lives on the VPS); not a fit for runtime/data-driven agents. Workflow Action minutes are gated; long-running agents would be expensive.

**Fit**: **good** for code/security/PR-review style agents that operate on the repo only. **Bad** for the pipeline anomaly agent, which needs the live DB.

### Option B — Inngest, Temporal, or similar workflow engine

**Pros**: durable execution, retries, fan-out, observability dashboards. Right answer if we had a multi-tenant cloud system with long-lived multi-step agent workflows.

**Cons**: another external service in the deploy story. Inngest is free up to a tier, Temporal needs self-hosting or Temporal Cloud — both add a dependency we don't have today. The state we'd want to make durable (pipeline fires, alerts) is already durable in SQLite + journald + email. No multi-step agent in this proposal needs >5 minutes of execution.

**Fit**: **overkill**. This is the wrong shape of tool for a one-VPS, single-tenant, batch-oriented system. Re-evaluate only if Civic Lens grows into a multi-tenant or real-time service.

### Option C — LangGraph / custom agent framework

**Pros**: maximum flexibility for graph-shaped agent workflows. Useful when an agent needs to plan over many tool calls.

**Cons**: large maintenance surface for a project where there is exactly one candidate agent (anomaly) and that agent is fundamentally a *deterministic detector + one LLM verdict call* — no graph, no planning. Adopting LangGraph here is paying a tax for capability we don't use, and it pulls in a fast-moving dependency that the operator (single-maintainer project) will end up upgrading instead of building features.

**Fit**: **don't adopt yet.** Revisit only if a future agent needs >3 chained tool calls per invocation.

### Option D — Plain cron (systemd timer) + script

**Pros**: zero new infra. Systemd timers, SMTP alerts, the venv, the DB, and the per-stage failure-isolation pattern in `_run_stage` are *already in production* and have already been hardened by an incident. Adding a `PipelineAnomalyDetector` as the 11th stage of `run_full_pipeline()` is a ~200 LOC change with no new deploy step, no new dependency, no new service.

**Cons**: not generalizable to a many-agents future. If we end up with 5+ agents, this approach starts to look ad-hoc. But §2 concludes we should have ~1 agent, so generalization isn't a real cost yet.

**Fit**: **best for the agent we are actually building.** Recommend.

### Recommendation

**Build the anomaly agent on Option D (systemd + script).** Reserve **Option A (GitHub Actions + Claude)** for any future PR-triggered code review agent — that's a separate decision, not part of this rollout. **Skip B and C** until the agent count justifies them.

## 5. Recommended phased rollout

Aim: ship one agent in ~2 weeks, learn from it, then decide whether anything else is worth doing.

### Phase 0 — Free wins, no agent code (Day 1, ~1 hour)

These close the security-audit slot without writing any agent:

1. Enable GitHub Dependabot (commit `.github/dependabot.yml` with weekly updates for pip + npm + go modules).
2. Enable GitHub secret-scanning + push protection in repo settings.
3. Enable CodeQL via the default workflow template for Python, Go, and JavaScript.

Cost: $0, time: ~1 hour. Recommendation: do this regardless of whether anything in §2 ships.

### Phase 1 — Pipeline anomaly agent v1 (Week 1–2)

Smallest useful version:
- New module `analysis/src/engine/anomaly_detector.py` implementing the 5 deterministic checks from §2.5.
- New stage in `job_runner.run_full_pipeline()` after `save_snapshots`, wrapped by `_run_stage` exactly like the others.
- New file `data/cache/anomaly_state.json` with the structured verdict.
- New endpoint `GET /api/health/pipeline` returning the cached verdict.
- LLM verdict-writer using the existing `get_llm_client()` factory + a new prompt-version constant in `llm/prompts.py` (per the standing rule that all LLM tasks bump prompt versions).
- Email path reuses `send_alert()`.
- Audit trail: one entry under `docs/audit-trail/analysis/`, one under `docs/audit-trail/api/` for the new endpoint.

Definition of done:
- Three real anomalies caught in dogfood (e.g., manually expire X bearer token; manually empty `narrative_docs`; manually drop `loader_batch_size` to 0). Each fires the expected alert.
- Operator allow-list (`anomaly_acks.json`) covers at least one ack path.

Out of scope for v1:
- UI surface beyond the JSON endpoint. (A small "Pipeline health" tile on the Review tab is a fast follow.)
- Cross-window trend memory beyond "today vs. trailing 7d/30d". 
- Auto-creating GitHub issues. (Add only if the email cadence proves insufficient.)

### Phase 2 — Decide based on Phase 1 (Week 3+)

After two weeks of the anomaly agent running, answer:
- Is it firing useful alerts? → keep it; consider a simple UI tile.
- Is it noisy? → tune thresholds; no scope expansion.
- Is the email path enough? → leave it. Build a GitHub-issue path only if the operator ends up acking 3+ alerts a week and wants a queue.

**Do not** start the bug-ticket agent without first adopting Sentry (or accepting that journald is the source). **Do not** start a code-audit agent without a clear failure story for the existing PR review process.

## 6. Open questions for human decision

1. **Sentry adoption.** Does the project want to take the dependency? It's the cleanest source for a future bug-ticket agent. Free tier is enough for this volume. Decision unblocks §2.3 entirely.
2. **PR review automation policy.** Is a Claude-on-PR review agent (§2.1, GitHub Actions) wanted? It costs ~$1–5/mo and produces useful suggestions, but it also produces *suggestions a single-maintainer project must read*. Worth it depends on whether the operator wants more eyes on diffs or fewer interruptions.
3. **Where does the anomaly agent's UI surface live?** Review tab? A new "Pipeline health" page? Out-of-band Grafana-style dashboard? V1 ships with a JSON endpoint and email; the UI question can wait, but it'll come up.
4. **Alert routing.** Today everything goes to the operator's Gmail. Anomaly alerts will too. Is there appetite for a separate channel (Discord webhook, Slack) so anomaly alerts don't pile in with deploy/crash mail? Cheap to add, but non-zero design.
5. **Anomaly-detector thresholds — process question.** The thresholds in §2.5 (>75% volume drop, >2σ confidence drift, etc.) are first guesses. After two weeks of dogfood data, do we want to (a) hand-tune them, (b) auto-calibrate from trailing distributions, (c) leave them static and accept whatever rate they fire at? Decision affects how much complexity belongs in v1.
6. **Fact-check coordination.** If `docs/proposals/fact-check-agent.md` ships, does the anomaly agent need to be aware of fact-check coverage as one of its checks? Best treated as a follow-up after both exist, but worth flagging now.
7. **Long-term shape.** Is there a "platform" ambition here — multi-tenant Civic Lens, agents per-tenant, etc. — that should pull us toward Option B/C infra earlier? If yes, the anomaly agent should be designed against that future. If no (single deployment, single operator), Option D is the right ceiling. Today's signals point to "no", but this is a product decision, not an engineering one.
