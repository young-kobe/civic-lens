"""
Per-entity bot-rollup assembly (the three-way Bot Detector grid).

Pure assembly helpers the entity-rollup scan in ``repository`` calls:
catch-all profile selection (the singular bot display copy, deliberately
different from sentiment's plural form), the per-entity ``BotEntityItem``
build (bot-rate + confidence-ranked samples), and the grid sort key.
"""

from __future__ import annotations

from typing import Any, Dict

from analysis.src.reporting.entity_registry import (
    catch_all_profile,
    CATCH_ALL_SUBREDDITS,
    CATCH_ALL_X_USERS,
    EntityRoute,
)
from analysis.src.reporting.models import BotEntityItem


# How many bot-flagged posts, confidence-ranked, to surface per entity so
# its card can open an evidence modal instead of dead-ending at a link.
_SAMPLES_PER_ENTITY = 5


def _entity_sort_key(item: BotEntityItem):
    """Sort bot-rollup entities: registry-matched first (catch-alls last),
    highest bot_rate_pct next, then highest bot_docs as tie-break."""
    is_catch_all = 1 if item.kind == "catch_all" else 0
    return (is_catch_all, -item.bot_rate_pct, -item.bot_docs)


def _resolve_entity_profile(route: EntityRoute) -> Dict[str, Any]:
    """Profile card for a routed doc. Matched entities / curated accounts /
    verified-officials carry ``route.entity_profile``; the plain X / subreddit
    catch-alls have None, so bot supplies its own display copy here (the
    wording deliberately differs from sentiment's plural form)."""
    profile = route.entity_profile
    if profile is not None:
        return profile
    if route.key == CATCH_ALL_X_USERS:
        return catch_all_profile(
            CATCH_ALL_X_USERS, "Other X users",
            "X post whose author is not in the tracked officials registry.",
        )
    return catch_all_profile(
        CATCH_ALL_SUBREDDITS, "Other subreddits",
        "Reddit post whose subreddit is not in the tracked subreddit registry.",
    )


def _build_bot_entity_item(key: str, slot: Dict[str, Any]) -> BotEntityItem:
    """Finalize one accumulator slot into a ``BotEntityItem``: bot rate and
    the top ``_SAMPLES_PER_ENTITY`` confidence-ranked flagged examples."""
    rate = (slot["bot"] / slot["total"] * 100) if slot["total"] else 0.0
    samples = [
        ex for _conf, ex in
        sorted(slot["flagged"], key=lambda pair: -pair[0])[:_SAMPLES_PER_ENTITY]
        if ex is not None
    ]
    return BotEntityItem(
        key=key, kind=slot["kind"],
        total_docs=slot["total"], bot_docs=slot["bot"],
        bot_rate_pct=round(rate, 1),
        entity_profile=slot["profile"],
        samples=samples,
    )
