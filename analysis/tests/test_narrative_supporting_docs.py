"""
Intent guard for narrative drill-down supporting-doc cards.

WHY this test exists: ``build_supporting_docs`` used to emit a body snippet only
for docs with no title (social posts), nulling it for news. That left news
drill-down cards as a bare headline with no context — the UI leads with the
headline and renders the snippet as the dek beneath it, so a null snippet meant
an empty card body. This test pins the fix: EVERY doc with body text gets a
snippet, so a news row (headline + body) still yields a dek. It fails if someone
reintroduces the ``if not r.title`` gate.
"""

import json
import os
import sys
import unittest

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.reporting.aggregators.narrative.projector import build_supporting_docs
from analysis.src.reporting.aggregators.rows import NarrativeSupportingDocRow


def _row(**overrides) -> NarrativeSupportingDocRow:
    """A supporting-doc row with all-None defaults; override just the fields
    a case cares about."""
    fields = dict(
        doc_id=1, title=None, text=None, source_type=None,
        domain_or_subreddit=None, ident=None, published_at=None, x_handle=None,
        output_json=None, confidence=None, x_retweets=None, x_replies=None,
        x_likes=None, x_quotes=None, u_name=None, u_avatar=None,
        u_verified_type=None, u_followers=None, u_created_at=None, u_bio=None,
        reddit_score=None, reddit_comments=None,
    )
    fields.update(overrides)
    return NarrativeSupportingDocRow(**fields)


class TestSupportingDocDek(unittest.TestCase):
    def test_news_row_with_title_still_gets_body_dek(self):
        """A news doc has BOTH a headline and body text; the card needs the dek
        under the headline, so the snippet must be the body preview (not null)."""
        row = _row(
            doc_id=910011,
            title="Border Crossings Surge to Record Levels",
            text="Federal data shows April crossings exceeded every prior "
                 "monthly record, straining processing centers past capacity.",
            source_type="news",
            domain_or_subreddit="nytimes.com",
        )
        out = build_supporting_docs([row])
        self.assertEqual(len(out), 1)
        doc = out[0]
        self.assertEqual(doc["title"], "Border Crossings Surge to Record Levels")
        self.assertIsNotNone(doc["snippet"], "news card lost its dek")
        # The dek is the body preview, distinct from the headline.
        self.assertIn("Federal data", doc["snippet"])
        self.assertNotEqual(doc["snippet"], doc["title"])

    def test_social_row_without_title_uses_body_as_headline(self):
        """Unchanged behavior: a social post has no title, so its body preview
        is what the UI shows in place of a headline."""
        row = _row(
            doc_id=920011,
            title=None,
            text="Reciprocal tariffs on every country that has tariffed us.",
            source_type="x_post",
            x_handle="POTUS",
        )
        out = build_supporting_docs([row])
        self.assertIsNone(out[0]["title"])
        self.assertIsNotNone(out[0]["snippet"])
        self.assertIn("Reciprocal tariffs", out[0]["snippet"])

    def test_empty_body_yields_null_snippet(self):
        """No body text -> no snippet (the card falls back to the headline or
        '(untitled)'); must not fabricate a dek."""
        row = _row(doc_id=1, title="A headline", text=None, source_type="news")
        self.assertIsNone(build_supporting_docs([row])[0]["snippet"])

    def test_reasoning_is_surfaced_from_sentiment_json(self):
        """The sentiment reasoning rides along on the card so the modal can show
        why the tone was labeled."""
        row = _row(
            doc_id=1,
            title="A headline",
            text="Body text.",
            source_type="news",
            output_json=json.dumps({"label": "NEGATIVE", "reasoning": "Loaded framing."}),
            confidence=0.9,
        )
        doc = build_supporting_docs([row])[0]
        self.assertEqual(doc["sentiment_label"], "negative")
        self.assertEqual(doc["reasoning"], "Loaded framing.")


if __name__ == "__main__":
    unittest.main()
