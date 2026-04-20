"""
Tests for the SnapshotCache module.
"""

import shutil
import tempfile
import unittest
from pathlib import Path

from analysis.src.common.cache import CacheMetadata, SnapshotCache


class TestSnapshotCache(unittest.TestCase):
    """Tests for SnapshotCache class."""

    def setUp(self):
        self.temp_dir = tempfile.mkdtemp()
        self.cache = SnapshotCache(self.temp_dir)

    def tearDown(self):
        shutil.rmtree(self.temp_dir, ignore_errors=True)

    def test_save_and_load(self):
        data = {"key": "value", "count": 42}
        self.cache.save("test_key", data, doc_count=10)
        self.assertEqual(self.cache.load("test_key"), data)

    def test_load_missing_key(self):
        self.assertIsNone(self.cache.load("nonexistent_key"))

    def test_exists(self):
        self.assertFalse(self.cache.exists("test_key"))
        self.cache.save("test_key", {"data": 1})
        self.assertTrue(self.cache.exists("test_key"))

    def test_delete(self):
        self.cache.save("test_key", {"data": 1})
        self.assertTrue(self.cache.exists("test_key"))

        self.assertTrue(self.cache.delete("test_key"))
        self.assertFalse(self.cache.exists("test_key"))

        # Deleting non-existent key returns False
        self.assertFalse(self.cache.delete("test_key"))

    def test_metadata(self):
        self.cache.save("test_key", {"data": 1}, doc_count=42)

        meta = self.cache.get_metadata("test_key")
        self.assertIsNotNone(meta)
        self.assertIsInstance(meta, CacheMetadata)
        self.assertEqual(meta.key, "test_key")
        self.assertEqual(meta.doc_count, 42)
        self.assertEqual(meta.version, SnapshotCache.VERSION)
        self.assertIn("generated_at", meta.to_dict())

    def test_load_with_meta(self):
        data = {"items": [1, 2, 3]}
        self.cache.save("test_key", data, doc_count=3)

        loaded_data, meta = self.cache.load_with_meta("test_key")
        self.assertEqual(loaded_data, data)
        self.assertEqual(meta.doc_count, 3)

    def test_get_all_metadata(self):
        self.cache.save("key1", {"a": 1}, doc_count=1)
        self.cache.save("key2", {"b": 2}, doc_count=2)
        self.cache.save("key3", {"c": 3}, doc_count=3)

        all_meta = self.cache.get_all_metadata()
        self.assertEqual(len(all_meta), 3)

        keys = {m["key"] for m in all_meta}
        self.assertEqual(keys, {"key1", "key2", "key3"})

    def test_clear_all(self):
        self.cache.save("key1", {"a": 1})
        self.cache.save("key2", {"b": 2})
        self.cache.save("key3", {"c": 3})

        count = self.cache.clear_all()
        self.assertEqual(count, 3)
        self.assertFalse(self.cache.exists("key1"))
        self.assertFalse(self.cache.exists("key2"))
        self.assertFalse(self.cache.exists("key3"))

    def test_sanitizes_key(self):
        """Keys with slashes are underscored for filesystem safety."""
        self.cache.save("path_to_key", {"data": 1})
        self.assertTrue(self.cache.exists("path_to_key"))
        path = self.cache._get_path("path_to_key")
        self.assertIn("_", str(path.name))

    def test_rejects_dot_dot_traversal(self):
        """Traversal payloads raise ValueError (audit §1.4)."""
        with self.assertRaises(ValueError):
            self.cache._get_path("../../../etc/passwd")
        with self.assertRaises(ValueError):
            self.cache.save("../../escape", {"x": 1})

    def test_rejects_null_byte(self):
        """NUL-byte payloads raise ValueError — prevents C-string truncation
        tricks if the key ever flows into a lower-level filesystem call."""
        with self.assertRaises(ValueError):
            self.cache._get_path("sentiment_24h\x00/etc/passwd")

    def test_path_stays_under_cache_dir(self):
        """The resolved path must always be inside ``cache_dir``."""
        path = self.cache._get_path("sentiment_24h")
        self.assertTrue(str(path).startswith(str(Path(self.temp_dir).resolve())))

    def test_complex_data(self):
        data = {
            "stories": [
                {"id": 1, "title": "Story 1", "tags": ["a", "b"]},
                {"id": 2, "title": "Story 2", "tags": ["c"]},
            ],
            "metadata": {
                "count": 2,
                "nested": {"deep": {"value": True}},
            },
        }

        self.cache.save("complex", data, doc_count=2)
        self.assertEqual(self.cache.load("complex"), data)


if __name__ == "__main__":
    unittest.main()
