"""
Engine models package.

Provides dataclasses for analysis engine results.
"""

from analysis.src.engine.models.engine_models import (
    SentimentResult,
    BotResult,
    EntityStance,
    FavorabilityResult,
)

__all__ = [
    "SentimentResult",
    "BotResult",
    "EntityStance",
    "FavorabilityResult",
]
