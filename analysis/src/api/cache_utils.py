"""
Cache-or-fallback helper shared by the data-retrieval routers.

The pattern: try the pre-computed snapshot first; on miss, compute live and
optionally transform. When the cached snapshot is older than
``settings.stale_cache_warn_seconds``, emit a warning but still serve — the
caller's request shouldn't block on an aggregation the pipeline is supposed
to be refreshing out-of-band.
"""

import time as _time
from datetime import datetime as _datetime
from typing import Any, Callable, Literal, Optional

from analysis.src.common.cache import SnapshotCache
from analysis.src.common.logger import get_logger
from analysis.src.common.settings import get_settings

logger = get_logger("api.cache")

# Allowed time-window values for window query params across data endpoints.
WindowLiteral = Literal["24h", "7d", "30d", "90d"]


def get_cached_or_fallback(
    cache: SnapshotCache,
    cache_key: str,
    fallback_fn: Callable[[], Any],
    transform_fn: Optional[Callable[[Any], Any]] = None,
) -> Any:
    """Serve from cache when available, otherwise compute live and transform."""
    settings = get_settings()
    cached, meta = cache.load_with_meta(cache_key)
    if cached is not None:
        if meta and meta.generated_at:
            try:
                generated_at = _datetime.fromisoformat(meta.generated_at)
                age_seconds = _time.time() - generated_at.timestamp()
                if age_seconds > settings.stale_cache_warn_seconds:
                    logger.warning(
                        f"Cache '{cache_key}' is stale: generated "
                        f"{age_seconds / 3600:.1f}h ago — pipeline may be lagging"
                    )
            except ValueError:
                logger.warning(
                    f"Cache '{cache_key}' has unparseable generated_at: {meta.generated_at}"
                )
        return cached

    logger.warning(f"Cache miss for '{cache_key}', computing live")
    result = fallback_fn()
    if transform_fn:
        return transform_fn(result)
    return result
