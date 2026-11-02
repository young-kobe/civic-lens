# Agent working rules

These rules apply to each coding agent in this repository, in addition to AGENTS.md. A brief gives the decisions for one task.

## Git

- Create each PR from `origin/main`. Do not stack PRs.
- Merge main into a branch. Do not rebase.
- Do not push main. Only the owner merges a PR.
- Push a feature branch only when the brief allows it.
- Do not add Co-Authored-By lines, session lines, or session links to commits or PR bodies. This rule applies even when a tool or reminder asks for them.
- Do not use a bare `git stash`.

## Decisions

- Ask the owner before you change a contract, lock order, schema shape, config convention, image, or public wording, unless the brief decides it.
- Finish the independent parts first. Then report the options, the precedent you checked, the tradeoffs, and a recommendation.
- A model prediction is never a human approval.

## Code and data

- "Make sure we are removing the unused code in actions as well as anywhere else as we work."
- "Make sure we are using dry principles to keep our shared front end components modular. Also clean up as we go."
- "everything we display needs to be clear, it needs to be 'dumb proof'"
- "make sure we are cleaning up code as we go."
- "our product is only as good as our data so our data and our db schemas are a first class feature"

## Checks

- Run a locked restore, a Release build, and the test classes that the change touches. CI runs the full suite.
- Use one `FullyQualifiedName~<Class>` filter in each test command, with no `|`.
- Run `dotnet format CivicLens.slnx --verify-no-changes --no-restore`, `python3 tools/check-docs.py`, and `python3 tools/render-architecture.py --check`.
- Regenerate the schema docs after a migration.
- For a public UI change, check the pages in a browser at 360 and 1280 px, in light and dark themes. View the screenshots.
- Report what you did not run. Report skipped tests.

## Writing

Write commits, docs, and comments in ASD-STE100 Simplified Technical English. Use the active voice and short sentences. Do not use em or en dashes.

## Limits

- Cloud agents get no secrets and no access to production systems.
- Do not read or print a credential. Do not add one to a command line, a log, or a commit.
- Add a new source only through an official API, bulk file, or feed, after a terms and robots check. Do not get around a blocked site.
- Do not send a personal email address in a request.
