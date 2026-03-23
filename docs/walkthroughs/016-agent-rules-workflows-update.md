# Agent Rules and Workflows Update

## Summary

Updated agent configuration to accurately reflect the Civic Lens architecture (Go ingestion, Python analysis, React UI) and added DRY/SOLID design principles.

## Changes Made

### Rules Created/Updated

| File | Change |
|------|--------|
| [code-style.md](file:///c:/Users/kobey/civic-lens/.agent/rules/code-style.md) | **[NEW]** DRY/SOLID principles + language-specific style guides (Python, Go, TypeScript) |
| [invariants.md](file:///c:/Users/kobey/civic-lens/.agent/rules/invariants.md) | Added layer boundaries rule + reference to code-style.md |

render_diffs(file:///c:/Users/kobey/civic-lens/.agent/rules/invariants.md)

### Workflows Created/Updated

| File | Change |
|------|--------|
| [global.md](file:///c:/Users/kobey/civic-lens/.agent/workflows/global.md) | **[NEW]** Architecture overview, data flow, common commands |
| [go-ingestion.md](file:///c:/Users/kobey/civic-lens/.agent/workflows/go-ingestion.md) | **[NEW]** Replaces `cpp-scraping-injestion.md` with Go-based instructions |
| [python-ai-reporting.md](file:///c:/Users/kobey/civic-lens/.agent/workflows/python-ai-reporting.md) | Updated for FastAPI + React (was Streamlit) |

### Files Deleted

- `cpp-scraping-injestion.md` - Obsolete C++ workflow

## New Slash Commands

- `/global` - Architecture and common commands
- `/go-ingestion` - Go crawler and data storage
- `/python-ai-reporting` - Python analysis + FastAPI + React dashboard
