# 2026-07-25 — `/health` stops advertising a switch this stack ignores

`GET /health` no longer returns `llm_enabled`
(`analysis/src/api/routers/health.py`). It still reports `status`, `app_name`,
`api_version`, and `db_reachable`, and still exercises the Postgres pool so a
misconfigured deploy fails loudly.

## Why

A 2026-07-25 audit traced every reader of `settings.llm_enabled`. In the
Postgres stack there are none: the engines under `analysis/src/engine/` that
`scheduler/pipeline.py` imports always call the LLM and record a failed run
when the call fails. The only consumers are `scheduler/job_runner.py` and the
four pre-redesign engines it drives — the retired SQLite stack.

So the field reported a control that could not affect anything the new API
serves. A reader seeing `llm_enabled: false` would reasonably conclude analysis
was running without the model, which is not what that value means here.

## Related change

`.env.example` shipped `CIVIC_LLM_ENABLED=false`, which compounded the same
confusion. It is now `true`, with a comment naming the retired stack as its
only reader and Phase 11 as its removal point.

**Check the production `.env` separately.** The repo has no `.env`, so the
deployed value is unknown from here. It matters more than it looks: the
scheduled analysis timer still runs `job_runner` (`docker-compose.yml:133`,
`deploy/systemd/civic-lens-analyze.service`, `setup-cron.sh`), and that stack
*does* honour the switch. If production carries `false`, the nightly run has
been producing heuristic-only output.

## Follow-ups

- Delete `settings.llm_enabled` outright when `job_runner.py` and the old
  engines go at Phase 11. It cannot be removed sooner without breaking the
  stack currently serving production.
- Cross-linked: `docs/audit-trail/analysis/2026-07-25-llm-only-judgments.md`
  covers the engine-side heuristic removals from the same audit.
