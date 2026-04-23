"""Unit tests for the bot detector's indicator sanitization helpers.

Covers the TODO item "Sanitize whyFlagged at source" from
``docs/todos/bot-propaganda-entity-signals.md``. Previously the UI filtered
``=None`` / ``=0`` / ``=null`` / ``=undefined`` / ``=`` entries at display
time; those noise strings originated in the LLM prompt (empty signal
fields rendering as the literal word ``None`` in the prompt text, then
echoed back by the LLM in its ``indicators`` list). The sanitizer catches
both the root cause (prompt value mapping) and the late-binding defense
(filter on the returned indicators array).
"""

import unittest

from analysis.src.engine.bot import _safe_prompt_value, _sanitize_llm_indicators


class TestSafePromptValue(unittest.TestCase):
    """`_safe_prompt_value` is what the LLM prompt sees for each signal
    field. Its job is to never surface an ambiguous placeholder ("None",
    "null", empty, missing) as if it were a real observation."""

    def test_none_becomes_unknown(self):
        self.assertEqual(_safe_prompt_value(None), "unknown")

    def test_empty_string_becomes_unknown(self):
        self.assertEqual(_safe_prompt_value(""), "unknown")
        self.assertEqual(_safe_prompt_value("   "), "unknown")

    def test_string_forms_of_missing_become_unknown(self):
        for v in ("None", "none", "NULL", "undefined", "N/A", "n/a"):
            with self.subTest(value=v):
                self.assertEqual(_safe_prompt_value(v), "unknown")

    def test_real_values_pass_through(self):
        self.assertEqual(_safe_prompt_value(0), "0")
        self.assertEqual(_safe_prompt_value(42), "42")
        self.assertEqual(_safe_prompt_value("government"), "government")
        self.assertEqual(_safe_prompt_value("  spaced  "), "spaced")

    def test_integer_zero_is_valid_signal(self):
        # Counts of 0 are genuine signal ("follower count: 0 is suspicious"),
        # NOT missing data. They must survive the mapping as "0".
        self.assertEqual(_safe_prompt_value(0), "0")


class TestSanitizeLlmIndicators(unittest.TestCase):
    """`_sanitize_llm_indicators` filters the LLM's output indicators,
    dropping entries that match the historical noise patterns."""

    def test_drops_none_suffix(self):
        result = _sanitize_llm_indicators([
            "account_age=None days",
            "followers=0",
            "Non-US geo-tagged origin",
        ])
        self.assertEqual(result, ["Non-US geo-tagged origin"])

    def test_drops_null_undefined_empty_assignments(self):
        result = _sanitize_llm_indicators([
            "field=null",
            "field=undefined",
            "trailing=",
            "kept signal",
        ])
        self.assertEqual(result, ["kept signal"])

    def test_preserves_real_key_value_with_nonzero(self):
        # `followers=50` is a legitimate per-account signal and must pass.
        result = _sanitize_llm_indicators(["followers=50", "tweet_count=123"])
        self.assertEqual(result, ["followers=50", "tweet_count=123"])

    def test_preserves_human_phrases(self):
        # The human-phrase indicators from the heuristic classifier must
        # pass — they do not contain "=None"-style fragments.
        inputs = [
            "Spam keywords: lorem, ipsum",
            "High text repetition (75%)",
            "New account (3 days)",
            "verified_type=government (de-biased)",
        ]
        self.assertEqual(_sanitize_llm_indicators(inputs), inputs)

    def test_ignores_non_list_input(self):
        self.assertEqual(_sanitize_llm_indicators(None), [])
        self.assertEqual(_sanitize_llm_indicators("not a list"), [])
        self.assertEqual(_sanitize_llm_indicators({"k": "v"}), [])

    def test_coerces_non_string_entries_and_drops_empty(self):
        result = _sanitize_llm_indicators([123, None, "", "real"])
        self.assertEqual(result, ["123", "real"])

    def test_case_insensitive_none_match(self):
        # The pattern is IGNORECASE — "NONE" / "None" / "none" all drop.
        result = _sanitize_llm_indicators([
            "field=None",
            "field=NONE",
            "field=none",
            "ok",
        ])
        self.assertEqual(result, ["ok"])


if __name__ == "__main__":
    unittest.main()
