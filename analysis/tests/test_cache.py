"""
Tests for the SnapshotCache module.
"""

import json
import pytest
from pathlib import Path
import tempfile
import shutil

from analysis.src.common.cache import SnapshotCache, CacheMetadata


@pytest.fixture
def temp_cache_dir():
    """Create a temporary directory for cache tests."""
    temp_dir = tempfile.mkdtemp()
    yield temp_dir
    shutil.rmtree(temp_dir, ignore_errors=True)


@pytest.fixture
def cache(temp_cache_dir):
    """Create a SnapshotCache instance with a temp directory."""
    return SnapshotCache(temp_cache_dir)


class TestSnapshotCache:
    """Tests for SnapshotCache class."""
    
    def test_save_and_load(self, cache):
        """Test basic save and load functionality."""
        data = {"key": "value", "count": 42}
        cache.save("test_key", data, doc_count=10)
        
        loaded = cache.load("test_key")
        assert loaded == data
    
    def test_load_missing_key(self, cache):
        """Test loading a key that doesn't exist."""
        result = cache.load("nonexistent_key")
        assert result is None
    
    def test_exists(self, cache):
        """Test the exists method."""
        assert not cache.exists("test_key")
        cache.save("test_key", {"data": 1})
        assert cache.exists("test_key")
    
    def test_delete(self, cache):
        """Test deleting a cached key."""
        cache.save("test_key", {"data": 1})
        assert cache.exists("test_key")
        
        result = cache.delete("test_key")
        assert result is True
        assert not cache.exists("test_key")
        
        # Deleting non-existent key returns False
        result = cache.delete("test_key")
        assert result is False
    
    def test_metadata(self, cache):
        """Test that metadata is saved correctly."""
        cache.save("test_key", {"data": 1}, doc_count=42)
        
        meta = cache.get_metadata("test_key")
        assert meta is not None
        assert isinstance(meta, CacheMetadata)
        assert meta.key == "test_key"
        assert meta.doc_count == 42
        assert meta.version == SnapshotCache.VERSION
        assert "generated_at" in meta.to_dict()
    
    def test_load_with_meta(self, cache):
        """Test loading data with metadata."""
        data = {"items": [1, 2, 3]}
        cache.save("test_key", data, doc_count=3)
        
        loaded_data, meta = cache.load_with_meta("test_key")
        assert loaded_data == data
        assert meta.doc_count == 3
    
    def test_get_all_metadata(self, cache):
        """Test getting metadata for all cached items."""
        cache.save("key1", {"a": 1}, doc_count=1)
        cache.save("key2", {"b": 2}, doc_count=2)
        cache.save("key3", {"c": 3}, doc_count=3)
        
        all_meta = cache.get_all_metadata()
        assert len(all_meta) == 3
        
        keys = {m["key"] for m in all_meta}
        assert keys == {"key1", "key2", "key3"}
    
    def test_clear_all(self, cache):
        """Test clearing all cached items."""
        cache.save("key1", {"a": 1})
        cache.save("key2", {"b": 2})
        cache.save("key3", {"c": 3})
        
        count = cache.clear_all()
        assert count == 3
        assert not cache.exists("key1")
        assert not cache.exists("key2")
        assert not cache.exists("key3")
    
    def test_sanitizes_key(self, cache):
        """Test that keys with slashes are sanitized."""
        cache.save("path/to/key", {"data": 1})
        assert cache.exists("path/to/key")
        
        # The file should be named with underscores
        path = cache._get_path("path/to/key")
        assert "_" in str(path.name)
    
    def test_complex_data(self, cache):
        """Test caching complex nested data structures."""
        data = {
            "stories": [
                {"id": 1, "title": "Story 1", "tags": ["a", "b"]},
                {"id": 2, "title": "Story 2", "tags": ["c"]},
            ],
            "metadata": {
                "count": 2,
                "nested": {"deep": {"value": True}}
            }
        }
        
        cache.save("complex", data, doc_count=2)
        loaded = cache.load("complex")
        assert loaded == data


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
