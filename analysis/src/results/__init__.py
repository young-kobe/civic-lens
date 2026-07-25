"""
Postgres redesign Phase 5: analysis/src/results is the ONLY package that
writes analysis.* result tables. See store.py for the API.
"""

from analysis.src.results.store import (
    BotSignalsRow,
    CitationRow,
    ClaimRow,
    PropagandaResultRow,
    PropagandaTechniqueRow,
    RunHandle,
    SentimentRow,
    TargetMentionRow,
    open_run,
    register_prompt_version,
)

__all__ = [
    "BotSignalsRow",
    "CitationRow",
    "ClaimRow",
    "PropagandaResultRow",
    "PropagandaTechniqueRow",
    "RunHandle",
    "SentimentRow",
    "TargetMentionRow",
    "open_run",
    "register_prompt_version",
]
