"""
GET /api/v1/sentiment aggregation package. panel.py holds the entry point
and orchestration; sql.py/routing.py are the shared base modules;
received.py/outbound.py hold the received-tone and outbound-target
rollups. See panel.py's own docstring for the response contract.
"""

from __future__ import annotations

from analysis.src.api.queries.sentiment.panel import get_sentiment_panel

__all__ = ["get_sentiment_panel"]
