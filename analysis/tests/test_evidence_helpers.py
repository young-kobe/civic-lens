"""
Unit tests for the shared evidence / document-presentation helpers
(``analysis.src.reporting.aggregators.evidence``).

These helpers were consolidated out of narrative.py / sentiment.py into a
neutral shared module. The tests lock the URL / label / snippet / evidence
contract so the collapse of sentiment's inline URL duplication — and the
repointing of bot / propaganda / review imports — is provably behavior
identical. WHY it matters: every public sample must carry an auditable
permalink (invariant C1 / audit A-7), and the "degrade to bare platform"
behavior for handle-less X rows is relied on by the UI.
"""

import sys
import unittest
from pathlib import Path

_repo_root = Path(__file__).resolve().parents[2]
if str(_repo_root) not in sys.path:
    sys.path.insert(0, str(_repo_root))

from analysis.src.reporting.aggregators.evidence import (
    build_doc_url,
    build_source_label,
    sanitize_evidence,
    text_snippet,
)


class BuildDocUrlTests(unittest.TestCase):
    def test_news_ident_passthrough(self):
        self.assertEqual(
            build_doc_url("news", "nyt.com", "https://nyt.com/a"),
            "https://nyt.com/a",
        )

    def test_absolute_url_wins_regardless_of_source(self):
        self.assertEqual(
            build_doc_url("x_post", None, "http://example.com/x", "handle"),
            "http://example.com/x",
        )

    def test_reddit_post_synthesizes_permalink(self):
        self.assertEqual(
            build_doc_url("reddit_post", "politics", "t3_abc123"),
            "https://reddit.com/r/politics/comments/abc123",
        )

    def test_reddit_comment_strips_prefix_and_defaults_subreddit(self):
        self.assertEqual(
            build_doc_url("reddit_comment", None, "t1_xyz"),
            "https://reddit.com/r/all/comments/xyz",
        )

    def test_x_post_synthesizes_status_url(self):
        self.assertEqual(
            build_doc_url("x_post", "x.com", "123", "jack"),
            "https://x.com/jack/status/123",
        )

    def test_x_post_without_handle_has_no_url(self):
        self.assertIsNone(build_doc_url("x_post", "x.com", "123", None))

    def test_null_ident_returns_none(self):
        self.assertIsNone(build_doc_url("news", "nyt.com", None))


class BuildSourceLabelTests(unittest.TestCase):
    def test_news_with_and_without_domain(self):
        self.assertEqual(build_source_label("news", "nyt.com", None), "News · nyt.com")
        self.assertEqual(build_source_label("news", None, None), "News")

    def test_x_with_and_without_handle(self):
        self.assertEqual(build_source_label("x_post", "x.com", "jack"), "X · @jack")
        self.assertEqual(build_source_label("x_post", "x.com", None), "X")

    def test_reddit_with_and_without_subreddit(self):
        self.assertEqual(
            build_source_label("reddit_post", "politics", None), "Reddit · r/politics"
        )
        self.assertEqual(build_source_label("reddit_comment", None, None), "Reddit")

    def test_unknown_source_degrades_to_source_type(self):
        self.assertEqual(build_source_label("weird", None, None), "weird")
        self.assertEqual(build_source_label(None, None, None), "unknown")


class TextSnippetTests(unittest.TestCase):
    def test_none_and_empty(self):
        self.assertIsNone(text_snippet(None))
        self.assertIsNone(text_snippet("   "))

    def test_whitespace_collapsed(self):
        self.assertEqual(text_snippet("a\n  b\tc"), "a b c")

    def test_truncation_appends_ellipsis(self):
        out = text_snippet("x" * 200, limit=10)
        self.assertEqual(out, "xxxxxxx...")
        self.assertEqual(len(out), 10)


class SanitizeEvidenceTests(unittest.TestCase):
    def test_dedupe_case_insensitive(self):
        self.assertEqual(sanitize_evidence(["Satan", "satan"]), ["Satan"])

    def test_strips_placeholder_and_short_spans(self):
        self.assertEqual(
            sanitize_evidence(["exact quote here", "ok", "valid span"]),
            ["valid span"],
        )

    def test_preserves_single_loaded_word(self):
        self.assertEqual(sanitize_evidence(["Satanists"]), ["Satanists"])

    def test_caps_at_max_count(self):
        spans = [f"span number {i}" for i in range(10)]
        self.assertEqual(len(sanitize_evidence(spans, max_count=3)), 3)

    def test_non_string_spans_skipped(self):
        self.assertEqual(sanitize_evidence([None, 42, "valid span"]), ["valid span"])


if __name__ == "__main__":
    unittest.main()
