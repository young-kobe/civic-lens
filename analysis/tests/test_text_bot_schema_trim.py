"""
Tests for the 2026-07-25 schema/prompt trim: the text task drops its
favorability half (entity_stances/overall_gop_stance/
overall_favorability_confidence/favorability_reasoning) and the bot task
drops `is_bot` (a lossy collapse of `label`). See
docs/audit-trail/analysis/2026-07-25-text-sentiment-only.md and
docs/audit-trail/analysis/2026-07-25-bot-schema-trim.md.
"""

from __future__ import annotations

import os
import sys
import unittest

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.engine import text
from analysis.src.llm.client import LLMClient
from analysis.src.llm.prompts import BOT_SYSTEM_PROMPT, TEXT_ANALYSIS_SYSTEM_PROMPT
from analysis.src.llm.schemas import BOT_SCHEMA, TEXT_ANALYSIS_SCHEMA


class _FakeTransport:
    """Minimal stand-in for a backend's complete_once()/is_available/
    get_token_usage() surface -- same shape as test_engine_text.py's fixture,
    kept local here so this module stays independent of that one."""

    def __init__(self, outcomes):
        self.is_available = True
        self._outcomes = list(outcomes)

    def complete_once(self, system_prompt, user_prompt, response_schema=None, temperature=None):
        return self._outcomes.pop(0)

    def get_token_usage(self) -> int:
        return 0


def _doc() -> "text.TextDocInput":
    return text.TextDocInput(
        doc_id=1, source_type="news", title="A headline",
        text="Reporters said this is a bad policy decision overall.",
    )


_REMOVED_TEXT_KEYS = (
    "entity_stances", "overall_gop_stance",
    "overall_favorability_confidence", "favorability_reasoning",
)

# Any of these appearing in the sentiment-only prompt would reintroduce the
# one-party bug (favorability_stances was scoped to GOP entities only by
# this exact prompt text) and violate media-analysis rule 7 (lean is never
# fed into an LLM prompt).
_PARTY_NAMES = ("GOP", "Republican", "Democrat", "Trump", "Biden")


class TextSchemaTrimTests(unittest.TestCase):
    def test_required_list_excludes_removed_favorability_keys(self):
        for key in _REMOVED_TEXT_KEYS:
            self.assertNotIn(key, TEXT_ANALYSIS_SCHEMA["required"])
            self.assertNotIn(key, TEXT_ANALYSIS_SCHEMA["properties"])

    def test_response_without_favorability_keys_yields_valid_text_analysis(self):
        response = {
            "sentiment_label": "NEGATIVE",
            "sentiment_confidence": 0.8,
            "sentiment_evidence_spans": ["this is a bad policy decision"],
            "sarcasm_detected": False,
            "sentiment_reasoning": "test reasoning",
        }
        client = LLMClient(_FakeTransport([response]))

        result = text.analyze(_doc(), client)

        self.assertEqual(result.sentiment.label, "negative")
        self.assertEqual(result.sentiment.confidence, 0.8)

    def test_no_party_name_appears_in_text_system_prompt(self):
        for name in _PARTY_NAMES:
            self.assertNotIn(name, TEXT_ANALYSIS_SYSTEM_PROMPT)


class BotSchemaTrimTests(unittest.TestCase):
    def test_is_bot_removed_from_schema_and_prompt(self):
        self.assertNotIn("is_bot", BOT_SCHEMA["required"])
        self.assertNotIn("is_bot", BOT_SCHEMA["properties"])
        self.assertNotIn("is_bot", BOT_SYSTEM_PROMPT)

    def test_dropped_account_tells_removed_from_prompt(self):
        # These two tells had no real data source (BotDocInput's docstring):
        # posting_frequency/listed_count were always hardcoded "unknown".
        self.assertNotIn("sustained high tweet rate", BOT_SYSTEM_PROMPT.lower())
        self.assertNotIn("zero list memberships", BOT_SYSTEM_PROMPT.lower())


if __name__ == "__main__":
    unittest.main()
