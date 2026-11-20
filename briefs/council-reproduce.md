# Council role 2: Reproducibility auditor

Read briefs/council-common.md on this branch first (git show origin/cloud-briefs:briefs/council-common.md). Follow it exactly. This file names your angle.

Role name: Reproducibility.

Your job is to rerun the plan's experiments and check that the numbers in the docs match the outputs. Use this cloud machine; it has more memory than the owner's machine.

Setup, each step its own Bash call:
1. python3 -m venv /tmp/venv
2. /tmp/venv/bin/pip install numpy scikit-learn networkx threadpoolctl tokenizers onnxruntime
3. Read each script header and its note in docs/post-r68/experiments/ to learn its inputs and command.

Rules:
- Run only scripts whose inputs are in the repo or come from public government sources (congress.gov, govinfo.gov, VA). Pace network requests at least 2 seconds apart. Write outputs to /tmp only.
- Some scripts read a published release database, a model file, or a local corpus that is not in the repo. Do not try to get it. List each such script as NOT REPRODUCIBLE FROM THE REPO with the missing input. That is a finding: the plan says each result has repeatable materials.
- For each script that runs: record the exact command, the exit code, and each headline number. Compare each number with the doc that reports it (give file and line). Mark MATCH, MISMATCH (both values), or NOT IN OUTPUT.
- Check pins: where a note gives a hash, release number, or source URL, confirm the script uses the same one.
- Check the SQL files (published-graph.sql, reuse-issue-facts.sql, versioned-issue-definitions.sql, measure-issue-definition-reuse.sql) by reading them against the column names in the public_api views and migrations on main. You may start a disposable Postgres in Docker if dockerd runs (sudo dockerd > /tmp/dockerd.log 2>&1 &, then docker info; retry a Docker Hub 429 after a wait) and apply only the synthetic parts. Never connect to a real database.
- Check docs/post-r68/experiments/review-graph/: serve it with python3 -m http.server from /tmp copies and load it in a headless browser if you can (npm i playwright@1.49.1 in /tmp; npx playwright install --with-deps chromium). Report console errors and whether the map and list views show the same facts. Skip this if the install fails, and say so.

In your report, put a reproduction table first: script, command, status, numbers checked, result. Then the findings. Then say which experiments need a pinned, shareable input bundle before the pilot starts.
