"""
Tests for the deterministic pre-LLM text gates (engine/text_prep.py).

Why these matter:

1. Sentence-boundary truncation exists so an evidence span can never
   straddle the character-budget cut — a straddling span fails the verbatim
   validator and the LLM call is paid for but its output dropped. The tests
   pin "no dangling fragment" rather than an exact slice length.
2. The triviality gate exists so content the prompts already define as
   NEUTRAL/empty (@-mentions, links, hashtags only) never spends an LLM
   call. The tests assert the call is NOT made — not merely that the
   result looks neutral — because the cost saving is the behavior.
"""

import unittest

from analysis.src.engine.claims import ClaimDocInput, analyze
from analysis.src.engine.text_prep import is_trivial_content, truncate_at_sentence


class _ExplodingLLMClient:
    """Stand-in client that fails the test if the engine calls it."""

    is_available = True

    def complete(self, *args, **kwargs):
        raise AssertionError("LLM was called for content the gate should have handled")


class TestTruncateAtSentence(unittest.TestCase):
    def test_short_text_untouched(self):
        text = "One sentence. Two sentences."
        self.assertEqual(truncate_at_sentence(text, 2000), text)

    def test_cuts_at_sentence_boundary(self):
        # 40-char budget over two sentences: the cut must land after the
        # first sentence, not mid-word inside the second.
        text = "The senator voted yes today. The bill now moves to the House floor."
        out = truncate_at_sentence(text, 40)
        self.assertEqual(out, "The senator voted yes today.")

    def test_falls_back_to_word_boundary(self):
        # No sentence punctuation at all — must still not cut mid-word.
        text = "word " * 100
        out = truncate_at_sentence(text.strip(), 42)
        self.assertLessEqual(len(out), 42)
        self.assertTrue(all(w == "word" for w in out.split()))

    def test_ignores_boundary_too_early_in_window(self):
        # A lone period in the first 10% of the budget must not shrink the
        # window to a fragment — fall through to the word boundary instead.
        text = "Yes. " + "context " * 50
        out = truncate_at_sentence(text, 100)
        self.assertGreater(len(out), 60)


class TestIsTrivialContent(unittest.TestCase):
    def test_mentions_links_hashtags_only_is_trivial(self):
        self.assertTrue(is_trivial_content("@potus @vp https://t.co/abc #maga #vote"))

    def test_substantive_post_is_not_trivial(self):
        self.assertFalse(is_trivial_content("The border bill passed the Senate today"))

    def test_short_but_real_commentary_with_tags_is_not_trivial(self):
        self.assertFalse(is_trivial_content("This ruling changes everything #scotus"))


class TestTrivialContentSkipsLLM(unittest.TestCase):
    def test_claims_engine_short_circuits_without_call(self):
        result = analyze(
            ClaimDocInput(doc_id=1, text="@a @b #c https://t.co/xyz"),
            _ExplodingLLMClient(),
        )
        # Clean deterministic empty, not a failure — the doc gets a done run
        # and never re-queues.
        self.assertEqual(result.claims, [])
        self.assertEqual(result.inference_method, "deterministic")


if __name__ == "__main__":
    unittest.main()
