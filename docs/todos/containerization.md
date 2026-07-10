# Containerization + swappable LLM interface

Move the live CPX21 from systemd-native builds to a Docker Compose stack
(images built in CI, pulled from GHCR), add continuous SQLite replication
via Litestream, and land an OpenAI-compatible LLM client so a self-hosted
backend or custom token router can be adopted later by env change only.

Decision record: self-hosted inference does NOT pencil out today (worst-case
pipeline volume is ~$14/mo on gemini-2.5-flash-lite vs $50+/mo for any
always-on box). Revisit when sustained Gemini spend exceeds ~$50/mo.

## Code (ships with this branch)

- [x] Dockerfiles: `ingest/Dockerfile` (+ entrypoint that materializes
      migrations beside the DB), `analysis/Dockerfile` (shared api/analyze
      image), `deploy/docker/Dockerfile.web` (UI baked into caddy:2.10);
      root `.dockerignore`.
- [x] `docker-compose.yml` (caddy host-net, api loopback publish, litestream
      sidecar, `jobs`-profile ingest/analyze one-shots) +
      `docker-compose.override.example.yml` for local dev.
- [x] `deploy/litestream.yml` + `LITESTREAM_*` vars in `.env.example`.
- [x] systemd units rewritten to `docker compose run --rm` (crawl/analyze/x),
      `civic-lens-api.service` deleted, `civic-lens-stack.service` added.
- [x] `deploy/deploy.sh` pull-based (no on-box compiles); `deploy/install.sh`
      installs docker, drops caddy/go/node host packages, pins UID 10001.
- [x] Caddyfile: `trust_pool file` (Caddy 2.8+ spelling), `root * /srv/ui`.
- [x] CI: go 1.24 everywhere; build-only image job in `ci.yml`; GHCR
      build+push `images` job in `deploy.yml` gating `deploy`.
- [x] `OpenAICompatClient` + `CIVIC_LLM_BACKEND=openai_compat` factory branch
      + settings + `analysis/tests/test_openai_compat.py`.

## Cutover (on the box, after merge — runbook)

- [ ] Make the three GHCR packages public (or install a read-only pull token).
- [ ] Install docker-ce/docker.io + compose plugin on the box; `git pull` in
      /opt/civic-lens; `docker compose pull`.
- [ ] Verify `id -u civic-lens` is 10001; if not: `usermod -u 10001 civic-lens
      && groupmod -g 10001 civic-lens && chown -R civic-lens:civic-lens
      /var/lib/civic-lens`. (10001 avoids the systemd-resolve collision at 990.)
- [ ] Create the Litestream R2 bucket + scoped API token; add `LITESTREAM_*`
      to /etc/civic-lens.env.
- [ ] Dry-run `docker compose run --rm ingest migrate` against a scratch
      `CIVIC_DB_PATH` (proves entrypoint migration copy + UID writes).
- [ ] Window: disable crawl/analyze/x timers; wait for in-flight jobs; remove
      the `/var/lib/civic-lens/data/migrations` symlink; real migrate run;
      stop host `civic-lens-api` + `caddy`; `docker compose up -d`; install
      new units + `daemon-reload`; re-enable timers +
      `systemctl enable --now civic-lens-stack.service`.
- [ ] Smoke: `/health` 200 via Cloudflare; direct-IP TLS refused; SPA loads
      with immutable cache headers; manual `systemctl start` of crawl and
      analyze both exit 0 and write data; kill a one-shot mid-run and confirm
      the OnFailure alert email; litestream syncing + restore passes
      `PRAGMA integrity_check`; nightly backup.sh still green; reboot brings
      the stack and timers back.
- [ ] Disable host `caddy.service` permanently; after one clean week of timer
      cycles, remove the old venv/binary/ui-dist from /opt/civic-lens.

## Flash-Lite switch (gated on golden-set eval)

- [ ] Hand-verify golden-set labels and commit the
      `analysis/evals/baseline_claims.json` baseline (gate is warn-and-pass
      until this lands).
- [ ] Re-record golden-set recordings against `gemini-2.5-flash-lite`; run
      `python -m analysis.evals.run_eval --gate` (0.02 F1 tolerance).
- [ ] On pass: set `CIVIC_GEMINI_MODEL=gemini-2.5-flash-lite` in
      /etc/civic-lens.env. On fail: stay on flash and record the result in an
      audit-trail entry either way.
