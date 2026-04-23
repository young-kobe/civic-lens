# Audit trail

Human-readable record of how the system changed, bucketed by layer. This is the authoritative engineering history — git log captures *what* changed, this captures *why* and *what it replaces*.

## Structure

```
audit-trail/
  ingestion/     Go crawler, RSS / Reddit / X fetchers, frontier state, raw storage
  analysis/     Python ETL, engine (bot / sentiment / propaganda / narratives), aggregators, scheduler
  api/          FastAPI routers, rate limits, snapshot cache contract
  ui/           React + Vite frontend, components, pages, theme
  infra/        Deployment, systemd, Caddy, CI/CD, scheduled jobs, secrets handling
```

One file per bucket per change, dated `YYYY-MM-DD-short-slug.md`. When a change spans layers, write one entry per affected layer with cross-links.

## Entry shape

Keep entries dense and forward-looking — a future reader should be able to understand *the system as it is now* from the entries they read, not a diff of what used to be.

```markdown
# YYYY-MM-DD — Short imperative title

One paragraph explaining the change in its own right: what the system now does, why that matters, and what surface it's on.

## What shipped

- Concrete bullets. File paths, function names.

## Why

- The constraint, incident, or design pressure that forced the change.
- Not "we wanted to" — name the cause.

## Follow-ups

- Items that slipped this increment. Linked to `docs/todos/` where relevant.
```

Keep each entry under ~200 lines. Longer = split into multiple entries.

## Workflow

1. **Plan first.** Non-trivial work starts as a file in `docs/todos/` with a checklist.
2. **Execute.** As boxes tick, the work lands in code.
3. **Record on commit.** In the same PR that ships the change, add a dated entry here under the affected layer. Update any cross-references in `docs/INVARIANTS.md` if an invariant was created, changed, or removed.
4. **Retire the todo.** When every box in the todo is checked, delete the todo file — the audit-trail entries are the permanent record.

This workflow is a hard rule. Every merge should leave the tree consistent: if code changed, there's a matching audit-trail entry; if an initiative completed, the todo is gone.

## What does NOT go here

- Per-commit diffs. `git log` owns that.
- Debugging narratives. Fix goes in the code + commit message.
- Speculative future plans. Those live in `docs/todos/`.
- Deprecated designs for historical interest. If the code is gone, the entry describes the current state and names what replaced whatever was there — not the old thing on its own.

## Seed

The pre-existing numbered walkthroughs under `docs/walkthroughs/` are being consolidated into this structure; see `docs/todos/walkthrough-consolidation.md`. Until that lands, treat the walkthroughs as a secondary source.
