"""
Claim-extraction eval runner + CI regression gate.

Usage (from repo root, PYTHONPATH=.):

    python -m analysis.evals.run_eval                 # replay, report only
    python -m analysis.evals.run_eval --gate          # replay + baseline gate (CI)
    python -m analysis.evals.run_eval --mode live     # re-run the real LLM (needs keys)
    python -m analysis.evals.run_eval --mode live --record   # refresh recordings
    python -m analysis.evals.run_eval --write-baseline       # commit current score

Modes:
- replay (default): feeds recorded model responses through the live
  claims-engine code path (schema validation, evidence verification,
  claim validation). Deterministic; runs in CI with no API keys. Catches
  regressions in everything downstream of the network call, and detects
  prompt/schema drift via the recording fingerprint.
- live: calls the configured LLM backend per example. Opt-in (costs
  money / needs keys). Required after any prompt, schema, or model
  change: re-record, re-score, re-baseline.

Only examples with review_status=VERIFIED count toward scores and the
gate. PENDING_HUMAN_REVIEW examples are drafts awaiting hand labeling
(--include-pending runs them report-only, never gated).

Exit codes: 0 = pass or gate inactive, 1 = gate failure, 2 = bad config.
"""

import argparse
import json
import sys
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Dict, List, Optional, Tuple

from analysis.evals.harness import (
    RecordingLLMClient,
    ReplayLLMClient,
    claim_extraction_fingerprint,
)
from analysis.evals.scoring import ExampleScore, aggregate_scores, score_example
from analysis.src.engine.claims import ClaimDocInput, analyze
from analysis.src.llm.prompts import CLAIM_EXTRACTION_PROMPT_VERSION

EVALS_DIR = Path(__file__).resolve().parent
GOLDEN_DIR = EVALS_DIR / "golden" / "claims"
RECORDED_DIR = EVALS_DIR / "recorded" / "claims"
BASELINE_PATH = EVALS_DIR / "baseline_claims.json"

REVIEW_STATUSES = ("PENDING_HUMAN_REVIEW", "VERIFIED")


@dataclass
class GoldenExample:
    example_id: str
    input_text: str
    expected_claims: List[Dict[str, str]]
    review_status: str
    input_provenance: str
    notes: str = ""


def load_golden(golden_dir: Path = GOLDEN_DIR) -> List[GoldenExample]:
    """Load and validate every golden example file.

    Validation failures raise ValueError — a malformed golden set must
    break the build, not silently shrink the eval.
    """
    examples: List[GoldenExample] = []
    seen_ids: set = set()
    for path in sorted(golden_dir.glob("*.json")):
        raw = json.loads(path.read_text(encoding="utf-8"))
        example_id = raw.get("id", "")
        if not example_id or example_id != path.stem:
            raise ValueError(f"{path.name}: 'id' must equal the filename stem")
        if example_id in seen_ids:
            raise ValueError(f"{path.name}: duplicate id '{example_id}'")
        seen_ids.add(example_id)
        status = raw.get("review_status", "")
        if status not in REVIEW_STATUSES:
            raise ValueError(
                f"{path.name}: review_status '{status}' not in {REVIEW_STATUSES}"
            )
        text = raw.get("input_text", "")
        if not text.strip():
            raise ValueError(f"{path.name}: empty input_text")
        expected = raw.get("expected_claims", [])
        for i, claim in enumerate(expected):
            span = claim.get("evidence_span", "")
            if not span or span.lower() not in text.lower():
                raise ValueError(
                    f"{path.name}: expected_claims[{i}].evidence_span is not a "
                    f"verbatim substring of input_text (invariant B2)"
                )
            if not claim.get("claim", "").strip():
                raise ValueError(f"{path.name}: expected_claims[{i}].claim is empty")
        examples.append(
            GoldenExample(
                example_id=example_id,
                input_text=text,
                expected_claims=expected,
                review_status=status,
                input_provenance=raw.get("input_provenance", "unknown"),
                notes=raw.get("notes", ""),
            )
        )
    return examples


def load_recording(example_id: str, recorded_dir: Path = RECORDED_DIR) -> Optional[dict]:
    path = recorded_dir / f"{example_id}.json"
    if not path.exists():
        return None
    return json.loads(path.read_text(encoding="utf-8"))


def run_replay(
    examples: List[GoldenExample], recorded_dir: Path = RECORDED_DIR
) -> Tuple[List[ExampleScore], List[str], List[str]]:
    """Score examples against recordings.

    Returns (scores, missing_ids, stale_ids). Stale = the recording's
    prompt fingerprint no longer matches the live prompt/schema.
    """
    current_fp = claim_extraction_fingerprint()
    scores: List[ExampleScore] = []
    missing: List[str] = []
    stale: List[str] = []
    for ex in examples:
        recording = load_recording(ex.example_id, recorded_dir)
        if recording is None:
            missing.append(ex.example_id)
            continue
        if recording.get("prompt_fingerprint") != current_fp:
            stale.append(ex.example_id)
            continue
        # doc_id=0: the engine only uses it for log lines; eval examples are
        # identified by example_id, not corpus doc ids.
        result = analyze(
            ClaimDocInput(doc_id=0, text=ex.input_text),
            ReplayLLMClient(recording["raw_response"]),
        )
        predicted = [
            {"claim": c.claim_text, "confidence": c.confidence, "evidence_span": c.evidence_span}
            for c in result.claims
        ]
        scores.append(
            score_example(ex.example_id, ex.input_text, predicted, ex.expected_claims)
        )
    return scores, missing, stale


def run_live(
    examples: List[GoldenExample],
    record: bool,
    recorded_dir: Path = RECORDED_DIR,
) -> List[ExampleScore]:
    """Run the configured LLM backend per example; optionally save recordings."""
    from analysis.src.llm import get_llm_client

    recorder = RecordingLLMClient(get_llm_client())
    fingerprint = claim_extraction_fingerprint()
    scores: List[ExampleScore] = []
    for ex in examples:
        recorder.last_response = None
        result = analyze(ClaimDocInput(doc_id=0, text=ex.input_text), recorder)
        predicted = [
            {"claim": c.claim_text, "confidence": c.confidence, "evidence_span": c.evidence_span}
            for c in result.claims
        ]
        scores.append(
            score_example(ex.example_id, ex.input_text, predicted, ex.expected_claims)
        )
        if record and recorder.last_response is not None:
            recorded_dir.mkdir(parents=True, exist_ok=True)
            (recorded_dir / f"{ex.example_id}.json").write_text(
                json.dumps(
                    {
                        "example_id": ex.example_id,
                        "raw_response": recorder.last_response,
                        "prompt_version": CLAIM_EXTRACTION_PROMPT_VERSION,
                        "prompt_fingerprint": fingerprint,
                        "recorded_at": datetime.now(timezone.utc).isoformat(),
                    },
                    indent=2,
                    ensure_ascii=False,
                )
                + "\n",
                encoding="utf-8",
            )
    return scores


def load_baseline(path: Path = BASELINE_PATH) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def evaluate_gate(
    metrics: Dict[str, float],
    baseline: dict,
    missing: List[str],
    stale: List[str],
) -> Tuple[bool, str]:
    """Apply the regression gate. Returns (passed, message).

    Gate is INACTIVE (passes with a warning) until a baseline has been
    written from hand-verified examples. Once active, missing or stale
    recordings fail the gate — a prompt change without re-recording must
    not slip through as a silent pass.
    """
    if baseline.get("baseline") is None:
        return True, (
            "GATE INACTIVE: no baseline committed yet. Verify golden labels, "
            "record live outputs, then run --write-baseline."
        )
    base = baseline["baseline"]
    tolerance = float(baseline.get("tolerance", 0.0))
    if stale:
        return False, (
            f"GATE FAIL: {len(stale)} recording(s) have a stale prompt fingerprint "
            f"({', '.join(stale[:5])}{'...' if len(stale) > 5 else ''}). The prompt, "
            "schema, or prompt version changed — re-record with "
            "'--mode live --record', review scores, then '--write-baseline'."
        )
    if missing:
        return False, (
            f"GATE FAIL: {len(missing)} verified example(s) have no recording "
            f"({', '.join(missing[:5])}{'...' if len(missing) > 5 else ''}). "
            "Record them with '--mode live --record'."
        )
    floor = float(base["f1"]) - tolerance
    if metrics["f1"] < floor:
        return False, (
            f"GATE FAIL: F1 {metrics['f1']:.4f} is below baseline "
            f"{base['f1']:.4f} - tolerance {tolerance:.4f} = {floor:.4f}."
        )
    return True, (
        f"GATE PASS: F1 {metrics['f1']:.4f} >= floor {floor:.4f} "
        f"(baseline {base['f1']:.4f}, tolerance {tolerance:.4f})."
    )


def write_baseline(metrics: Dict[str, float], n_examples: int, path: Path = BASELINE_PATH) -> None:
    baseline = load_baseline(path)
    baseline["baseline"] = {
        "precision": metrics["precision"],
        "recall": metrics["recall"],
        "f1": metrics["f1"],
        "n_examples": n_examples,
        "prompt_version": CLAIM_EXTRACTION_PROMPT_VERSION,
        "prompt_fingerprint": claim_extraction_fingerprint(),
        "written_at": datetime.now(timezone.utc).isoformat(),
    }
    path.write_text(json.dumps(baseline, indent=2) + "\n", encoding="utf-8")


def print_example_diffs(scores: List[ExampleScore], verbose: bool) -> None:
    for s in scores:
        imperfect = s.false_positives or s.false_negatives
        if not (verbose or imperfect):
            continue
        print(f"  {s.example_id}: TP={s.true_positives} FP={s.false_positives} FN={s.false_negatives}")
        if verbose:
            for m in s.matches:
                print(f"    match (iou={m.iou:.2f}): '{m.predicted_claim}' ~ '{m.expected_claim}'")
        for c in s.unmatched_predicted:
            print(f"    FP (predicted, not in golden): '{c}'")
        for c in s.unmatched_expected:
            print(f"    FN (golden, not predicted):    '{c}'")


def main(argv: Optional[List[str]] = None) -> int:
    parser = argparse.ArgumentParser(description="Claim-extraction golden-set eval")
    parser.add_argument("--mode", choices=["replay", "live"], default="replay")
    parser.add_argument("--record", action="store_true",
                        help="live mode: save model responses under recorded/")
    parser.add_argument("--include-pending", action="store_true",
                        help="also run PENDING_HUMAN_REVIEW examples (report-only)")
    parser.add_argument("--gate", action="store_true",
                        help="apply the baseline regression gate (CI)")
    parser.add_argument("--write-baseline", action="store_true",
                        help="store the current verified-set score as the new baseline")
    parser.add_argument("--verbose", action="store_true")
    parser.add_argument("--report-json", type=Path, default=None,
                        help="write full per-example results to this path")
    args = parser.parse_args(argv)

    if args.record and args.mode != "live":
        print("error: --record requires --mode live", file=sys.stderr)
        return 2

    all_examples = load_golden()
    verified = [e for e in all_examples if e.review_status == "VERIFIED"]
    pending = [e for e in all_examples if e.review_status == "PENDING_HUMAN_REVIEW"]
    print(f"Golden set: {len(all_examples)} examples "
          f"({len(verified)} VERIFIED, {len(pending)} PENDING_HUMAN_REVIEW)")

    selected = verified + (pending if args.include_pending else [])
    missing: List[str] = []
    stale: List[str] = []
    if args.mode == "replay":
        scores, missing, stale = run_replay(selected)
    else:
        scores = run_live(selected, record=args.record)
        if args.record:
            print(f"Recorded {len(scores)} response(s) to {RECORDED_DIR}")

    # Scores and gate use VERIFIED examples only; pending are report-only.
    verified_ids = {e.example_id for e in verified}
    verified_scores = [s for s in scores if s.example_id in verified_ids]
    pending_scores = [s for s in scores if s.example_id not in verified_ids]
    verified_missing = [i for i in missing if i in verified_ids]
    verified_stale = [i for i in stale if i in verified_ids]

    metrics = aggregate_scores(verified_scores)
    print(f"\nVerified-set metrics ({len(verified_scores)} scored, "
          f"{len(verified_missing)} missing recording, {len(verified_stale)} stale):")
    print(f"  precision={metrics['precision']:.4f} recall={metrics['recall']:.4f} "
          f"f1={metrics['f1']:.4f}")
    print_example_diffs(verified_scores, args.verbose)

    if pending_scores:
        pending_metrics = aggregate_scores(pending_scores)
        print(f"\nPending-set metrics (report-only, NOT ground truth, "
              f"{len(pending_scores)} scored):")
        print(f"  precision={pending_metrics['precision']:.4f} "
              f"recall={pending_metrics['recall']:.4f} f1={pending_metrics['f1']:.4f}")
        print_example_diffs(pending_scores, args.verbose)

    if args.report_json:
        args.report_json.write_text(
            json.dumps(
                {
                    "mode": args.mode,
                    "metrics_verified": metrics,
                    "examples": [s.to_dict() for s in scores],
                    "missing_recordings": missing,
                    "stale_recordings": stale,
                },
                indent=2,
            )
            + "\n",
            encoding="utf-8",
        )
        print(f"\nFull report written to {args.report_json}")

    if args.write_baseline:
        if not verified_scores:
            print("error: refusing to write a baseline from zero verified examples",
                  file=sys.stderr)
            return 2
        if verified_missing or verified_stale:
            print("error: refusing to write a baseline with missing/stale recordings",
                  file=sys.stderr)
            return 2
        write_baseline(metrics, n_examples=len(verified_scores))
        print(f"Baseline written to {BASELINE_PATH}")

    if args.gate:
        passed, message = evaluate_gate(metrics, load_baseline(), verified_missing, verified_stale)
        print(f"\n{message}")
        return 0 if passed else 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
