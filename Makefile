# Local development commands. Secrets stay in the ignored .runtime/ env files.
SHELL := /bin/bash
.SHELLFLAGS := -Eeuo pipefail -c
.ONESHELL:

DOTNET ?= $(shell command -v dotnet || echo /tmp/civic-dotnet/dotnet)
CADDY ?= .runtime/tools/caddy/caddy
LIMIT ?= 20

HOST := "$(DOTNET)" src/CivicLens.Host/bin/Release/net10.0/CivicLens.Host.dll
COLLECTOR := src/CivicLens.Collector/bin/Release/net10.0/CivicLens.Collector.dll
WITH_ENV := source .runtime/database/connection.env && source .runtime/review/connection.env &&

export CIVIC_LENS_COLLECTION_CONFIG := $(CURDIR)/config/federal-pilot.json
export CIVIC_LENS_CAPTURE_DIRECTORY := $(CURDIR)/.runtime/captures
export CIVIC_LENS_RELEASE_DIRECTORY := $(CURDIR)/.runtime/releases

.PHONY: help build test check db migrate review releases publish activate serve

help: ## List targets
	@awk -F':.*## ' '/^[a-z]+:.*## /{printf "  %-9s %s\n", $$1, $$2}' $(MAKEFILE_LIST)

build: ## Locked restore and Release build
	"$(DOTNET)" restore CivicLens.slnx --locked-mode
	"$(DOTNET)" build CivicLens.slnx --configuration Release --no-restore

test: build ## Full test suite (needs Docker)
	"$(DOTNET)" test CivicLens.slnx --configuration Release --no-build

check: test ## Tests, formatting, and diagram checks
	"$(DOTNET)" format CivicLens.slnx --verify-no-changes --no-restore
	python3 tools/render-architecture.py --check

db: ## Start local Postgres on 127.0.0.1:5433
	@docker start civic-lens-postgres >/dev/null 2>&1 || docker run -d --name civic-lens-postgres \
	    --restart unless-stopped --env-file .runtime/database/postgres.env -p 127.0.0.1:5433:5432 \
	    -v civic-lens-postgres:/var/lib/postgresql postgres:18.3-alpine >/dev/null
	until docker exec civic-lens-postgres pg_isready -q; do sleep 1; done

migrate: build db ## Apply checked-in migrations
	@$(WITH_ENV) $(HOST) db migrate

review: migrate ## Run the HTTPS proxy, review workspace, and worker
	@$(WITH_ENV) export XDG_DATA_HOME="$(CURDIR)/.runtime/review/data" XDG_CONFIG_HOME="$(CURDIR)/.runtime/review/config"
	trap 'kill $$(jobs -p) 2>/dev/null; wait' EXIT
	$(CADDY) run --config .runtime/review/Caddyfile --adapter caddyfile > .runtime/review/proxy.log 2>&1 &
	$(HOST) review 5081 &
	$(HOST) worker $(COLLECTOR) .runtime/captures > .runtime/review/worker.log 2>&1 &
	echo "Review workspace: https://localhost:7443 (Ctrl+C stops all three)."
	wait -n

releases: ## List releases (LIMIT=20)
	@$(WITH_ENV) $(HOST) releases list $(LIMIT)

publish: ## make publish KEY=<key> DRAFTS="<id> ..."
	@$(WITH_ENV) $(HOST) releases publish "$(KEY)" $(DRAFTS)

activate: ## make activate RELEASE=<number>
	@$(WITH_ENV) $(HOST) releases activate "$(RELEASE)"

serve: ## Serve the active release on 127.0.0.1:8090
	python3 -m http.server 8090 --bind 127.0.0.1 --directory .runtime/releases/current
