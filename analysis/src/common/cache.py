"""
Snapshot Cache for pre-computed analysis results.

Stores aggregation snapshots as JSON files for static API serving.
Each snapshot includes metadata: generated_at, doc_count, version.
"""

import json
import os
from dataclasses import dataclass, asdict
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from analysis.src.common.logger import get_logger

logger = get_logger("cache")


@dataclass
class CacheMetadata:
    """Metadata for a cached snapshot."""
    key: str
    generated_at: str  # ISO format
    doc_count: int
    version: str
    file_path: str
    
    def to_dict(self) -> dict:
        return asdict(self)


class SnapshotCache:
    """
    File-based cache for pre-computed aggregation snapshots.
    
    Writes JSON files to cache_dir with structure:
    {
        "_meta": { "generated_at": "...", "doc_count": N, "version": "..." },
        "data": { ... actual payload ... }
    }
    """
    
    VERSION = "1.0"
    
    def __init__(self, cache_dir: str = "data/cache"):
        self.cache_dir = Path(cache_dir)
        self.cache_dir.mkdir(parents=True, exist_ok=True)
        logger.info(f"SnapshotCache initialized at {self.cache_dir.absolute()}")
    
    def _get_path(self, key: str) -> Path:
        """Get file path for a cache key.

        Callers pass keys assembled from request query params (e.g.
        ``f"sentiment_{window}"``). Reject traversal payloads even though the
        Pydantic ``Literal`` validators on the routers already constrain the
        values — defense-in-depth so a future endpoint that forgets to
        validate can't escape ``cache_dir`` (audit §1.4).
        """
        if "\x00" in key or ".." in key:
            raise ValueError(f"Invalid cache key: {key!r}")
        safe_key = key.replace("/", "_").replace("\\", "_")
        candidate = (self.cache_dir / f"{safe_key}.json").resolve()
        cache_root = self.cache_dir.resolve()
        # Path.is_relative_to lands in 3.9; we target 3.12 so this is safe.
        if not candidate.is_relative_to(cache_root):
            raise ValueError(f"Cache key escapes cache_dir: {key!r}")
        return candidate
    
    def save(self, key: str, data: Any, doc_count: int = 0) -> None:
        """
        Save data to cache with metadata.
        
        Args:
            key: Cache key (e.g., "stories", "sentiment")
            data: Data to cache (must be JSON-serializable)
            doc_count: Number of documents used to generate this data
        """
        path = self._get_path(key)
        
        payload = {
            "_meta": {
                "generated_at": datetime.now(timezone.utc).isoformat(),
                "doc_count": doc_count,
                "version": self.VERSION,
            },
            "data": data
        }
        
        # Write atomically by writing to temp file first
        temp_path = path.with_suffix(".tmp")
        try:
            with open(temp_path, "w", encoding="utf-8") as f:
                json.dump(payload, f, indent=2, default=str)
            temp_path.replace(path)
            logger.info(f"Cached '{key}' ({doc_count} docs) -> {path}")
        except Exception as e:
            logger.error(f"Failed to save cache '{key}': {e}")
            if temp_path.exists():
                temp_path.unlink()
            raise
    
    def load(self, key: str) -> dict | None:
        """
        Load cached data.
        
        Returns:
            The cached data payload, or None if not found.
        """
        path = self._get_path(key)
        
        if not path.exists():
            logger.warning(f"Cache miss: '{key}' not found")
            return None
        
        try:
            with open(path, "r", encoding="utf-8") as f:
                payload = json.load(f)
            return payload.get("data")
        except Exception as e:
            logger.error(f"Failed to load cache '{key}': {e}")
            return None
    
    def load_with_meta(self, key: str) -> tuple[dict | None, CacheMetadata | None]:
        """
        Load cached data with metadata.
        
        Returns:
            Tuple of (data, metadata), or (None, None) if not found.
        """
        path = self._get_path(key)
        
        if not path.exists():
            return None, None
        
        try:
            with open(path, "r", encoding="utf-8") as f:
                payload = json.load(f)
            
            meta_raw = payload.get("_meta", {})
            meta = CacheMetadata(
                key=key,
                generated_at=meta_raw.get("generated_at", ""),
                doc_count=meta_raw.get("doc_count", 0),
                version=meta_raw.get("version", "unknown"),
                file_path=str(path)
            )
            return payload.get("data"), meta
        except Exception as e:
            logger.error(f"Failed to load cache '{key}': {e}")
            return None, None
    
    def get_metadata(self, key: str) -> CacheMetadata | None:
        """Get metadata for a cached key without loading full data."""
        _, meta = self.load_with_meta(key)
        return meta
    
    def get_all_metadata(self) -> list[dict]:
        """Get metadata for all cached snapshots."""
        metadata = []
        for path in self.cache_dir.glob("*.json"):
            key = path.stem
            meta = self.get_metadata(key)
            if meta:
                metadata.append(meta.to_dict())
        return metadata
    
    def exists(self, key: str) -> bool:
        """Check if a cache key exists."""
        return self._get_path(key).exists()
    
    def delete(self, key: str) -> bool:
        """Delete a cached snapshot."""
        path = self._get_path(key)
        if path.exists():
            path.unlink()
            logger.info(f"Deleted cache '{key}'")
            return True
        return False
    
    def clear_all(self) -> int:
        """Clear all cached snapshots. Returns count deleted."""
        count = 0
        for path in self.cache_dir.glob("*.json"):
            path.unlink()
            count += 1
        logger.info(f"Cleared {count} cached snapshots")
        return count
