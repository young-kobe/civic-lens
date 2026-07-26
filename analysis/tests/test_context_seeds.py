"""
Tests for the reference-context seeding layer.

Covers the three observable behaviours the rest of the pipeline depends on:

  * Seeds parse correctly from markdown frontmatter, including filtering of
    malformed files and empty-keyword entries.
  * Keyword matching is case-insensitive and word-bounded — so e.g. the
    "vac" in "vacuum" must NOT trigger the vaccines seed.
  * The claims engine prepends a REFERENCE CONTEXT block above the user
    prompt only when at least one seed matches; for unrelated docs the
    prompt is unchanged.
"""

from __future__ import annotations

import os
import sys
import textwrap
import unittest
from pathlib import Path
from tempfile import TemporaryDirectory

current_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.dirname(os.path.dirname(current_dir))
if project_root not in sys.path:
    sys.path.insert(0, project_root)

from analysis.src.llm import context_seeds
from analysis.src.engine.claims import ClaimDocInput, analyze


def _write_seed(dir_path: Path, slug: str, body: str) -> Path:
    p = dir_path / f"{slug}.md"
    p.write_text(textwrap.dedent(body), encoding="utf-8")
    return p


class TestSeedLoader(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.dir = Path(self._tmp.name)

    def test_parses_frontmatter_and_collects_keywords(self) -> None:
        _write_seed(self.dir, "vaccines", """\
            ---
            topic: Vaccines
            source: CDC
            source_url: https://cdc.gov/vaccines
            last_verified: 2026-04-25
            applies_to_keywords:
              - vaccine
              - mRNA
            ---

            Body text about vaccines.
            """)
        seeds = context_seeds.load_seeds(self.dir)
        self.assertEqual(len(seeds), 1)
        s = seeds[0]
        self.assertEqual(s.slug, "vaccines")
        self.assertEqual(s.topic, "Vaccines")
        self.assertEqual(s.source_url, "https://cdc.gov/vaccines")
        self.assertEqual(s.last_verified, "2026-04-25")
        self.assertEqual(s.applies_to_keywords, ("vaccine", "mrna"))
        self.assertIn("Body text", s.content)

    def test_files_without_frontmatter_or_keywords_are_skipped(self) -> None:
        # No frontmatter at all → skipped silently.
        _write_seed(self.dir, "no_frontmatter", "just a markdown body")
        # Frontmatter but empty keyword list → still skipped (matching one
        # of these would always be false, so they'd never inject anyway).
        _write_seed(self.dir, "empty_kw", """\
            ---
            topic: Nothing
            applies_to_keywords: []
            ---

            body
            """)
        seeds = context_seeds.load_seeds(self.dir)
        self.assertEqual(seeds, [])

    def test_match_seeds_word_boundary_case_insensitive(self) -> None:
        _write_seed(self.dir, "vaccines", """\
            ---
            topic: Vaccines
            source: CDC
            source_url: https://cdc.gov
            last_verified: 2026-04-25
            applies_to_keywords:
              - vaccine
            ---
            body
            """)
        seeds = context_seeds.load_seeds(self.dir)

        # Case-insensitive whole-word match.
        self.assertEqual(
            len(context_seeds.match_seeds("The Vaccine works.", seeds)), 1
        )
        # Substring "vac" in "vacuum" must NOT match — that's the exact
        # collision the word-boundary rule exists to prevent.
        self.assertEqual(
            context_seeds.match_seeds("Bought a new vacuum cleaner.", seeds), []
        )
        # Unrelated text matches nothing.
        self.assertEqual(
            context_seeds.match_seeds("Stock market hit a record.", seeds), []
        )

    def test_match_seeds_caps_at_max(self) -> None:
        for i, kw in enumerate(["vaccine", "climate", "inflation", "evolution"]):
            _write_seed(self.dir, f"s{i}", f"""\
                ---
                topic: T{i}
                source: S
                source_url: u
                last_verified: 2026-04-25
                applies_to_keywords:
                  - {kw}
                ---
                body
                """)
        seeds = context_seeds.load_seeds(self.dir)
        text = "vaccine climate inflation evolution"
        # All four keywords are present; cap should hold at 3.
        self.assertEqual(len(context_seeds.match_seeds(text, seeds)), 3)

    def test_format_seeds_block_includes_citation_metadata(self) -> None:
        _write_seed(self.dir, "vaccines", """\
            ---
            topic: Vaccines
            source: CDC
            source_url: https://cdc.gov
            last_verified: 2026-04-25
            applies_to_keywords:
              - vaccine
            ---

            Authoritative body content.
            """)
        seeds = context_seeds.load_seeds(self.dir)
        block = context_seeds.format_seeds_block(seeds)
        # The block exists for grounding; it must carry the citation
        # metadata so a reviewer reading the prompt later can re-derive
        # which source the model saw.
        self.assertIn("REFERENCE CONTEXT", block)
        self.assertIn("Source: CDC", block)
        self.assertIn("Source URL: https://cdc.gov", block)
        self.assertIn("Last verified: 2026-04-25", block)
        self.assertIn("Authoritative body content.", block)
        # Empty input produces an empty string so callers can do a plain
        # concat without an if-guard.
        self.assertEqual(context_seeds.format_seeds_block([]), "")


# --------------------------------------------------------------------------- #
# Engine integration: the claims engine injects the block when seeds match,
# and skips it cleanly when nothing matches.
# --------------------------------------------------------------------------- #


class _RecordingLLMClient:
    """Captures the last user_prompt so tests can assert on prompt content
    without mocking the whole LLM stack."""
    is_available = True

    def __init__(self, response: dict) -> None:
        self._response = response
        self.last_user_prompt: str = ""
        self.last_system_prompt: str = ""

    def complete(self, system_prompt: str, user_prompt: str, response_schema=None) -> dict:
        self.last_system_prompt = system_prompt
        self.last_user_prompt = user_prompt
        return self._response


def _stub_default_seeds(test_case: unittest.TestCase, seeds: list) -> None:
    """Force the module-level seed cache to a hermetic list and unwind on
    teardown so other tests in the same run aren't affected."""
    context_seeds.reset_cache()
    original = context_seeds.get_seeds
    test_case.addCleanup(setattr, context_seeds, "get_seeds", original)
    context_seeds.get_seeds = lambda: tuple(seeds)


class TestClaimsEngineSeedInjection(unittest.TestCase):
    def setUp(self) -> None:
        self._tmp = TemporaryDirectory()
        self.addCleanup(self._tmp.cleanup)
        self.dir = Path(self._tmp.name)
        _write_seed(self.dir, "climate-change", """\
            ---
            topic: Climate change
            source: IPCC
            source_url: https://ipcc.ch
            last_verified: 2026-04-25
            applies_to_keywords:
              - climate change
            ---
            IPCC AR6 finds human activities have warmed the climate.
            """)
        seeds = context_seeds.load_seeds(self.dir)
        _stub_default_seeds(self, seeds)

    def test_injects_block_when_seeds_match(self) -> None:
        client = _RecordingLLMClient({
            "claims": [
                {
                    "claim": "The bill funds climate adaptation projects",
                    "confidence": 0.9,
                    "evidence_span": "funds climate adaptation projects",
                },
            ],
        })
        text = "The bill funds climate adaptation projects to fight climate change."
        result = analyze(ClaimDocInput(doc_id=1, text=text), client)
        self.assertIn("REFERENCE CONTEXT", client.last_user_prompt)
        self.assertIn("Source: IPCC", client.last_user_prompt)
        self.assertEqual(len(result.claims), 1)

    def test_prompt_unchanged_when_nothing_matches(self) -> None:
        client = _RecordingLLMClient({"claims": []})
        analyze(
            ClaimDocInput(doc_id=1, text="The Knicks won in overtime last night."),
            client,
        )
        self.assertNotIn("REFERENCE CONTEXT", client.last_user_prompt)


if __name__ == "__main__":
    unittest.main()
