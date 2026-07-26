"""
Scoring for claim-extraction evals: span-anchored matching + micro P/R/F1.

Every claim — golden or predicted — carries a verbatim evidence span
(pipeline invariant B2), so matching is deterministic: a predicted claim
matches a golden claim when their evidence spans overlap in the source
text (char-level IoU over the first case-insensitive occurrence). No LLM
judge, no fuzzy claim-text similarity in the score itself; the claim
texts are surfaced in per-example diffs for the human reading the report.

Known limitation (documented in docs/EVALS.md): two different assertions
citing the same span would count as a match. In practice claims are
anchored 1:1 to their spans by the extractor's validation rules, and the
per-example diff makes any such case visible to a reviewer.
"""

from dataclasses import dataclass, field
from typing import Dict, List, Optional, Tuple

# Minimum char-level IoU between two evidence spans for the claims they
# anchor to count as the same claim. Deliberately modest: annotator and
# model may quote different extents of the same sentence. Raising this
# makes the eval stricter; changing it invalidates the committed baseline.
SPAN_IOU_THRESHOLD = 0.3


def span_char_range(span: str, text: str) -> Optional[Tuple[int, int]]:
    """(start, end) of the first case-insensitive occurrence, or None.

    Mirrors the pipeline's substring check (engine/claims.py's
    validate_evidence_span): case-insensitive, first occurrence wins.
    """
    if not span:
        return None
    pos = text.lower().find(span.lower())
    if pos == -1:
        return None
    return (pos, pos + len(span))


def span_iou(a: Tuple[int, int], b: Tuple[int, int]) -> float:
    """Char-level intersection-over-union of two [start, end) ranges."""
    inter = max(0, min(a[1], b[1]) - max(a[0], b[0]))
    if inter == 0:
        return 0.0
    union = (a[1] - a[0]) + (b[1] - b[0]) - inter
    return inter / union


@dataclass
class ClaimMatch:
    predicted_claim: str
    expected_claim: str
    iou: float


@dataclass
class ExampleScore:
    example_id: str
    true_positives: int
    false_positives: int
    false_negatives: int
    matches: List[ClaimMatch] = field(default_factory=list)
    unmatched_predicted: List[str] = field(default_factory=list)
    unmatched_expected: List[str] = field(default_factory=list)

    def to_dict(self) -> dict:
        return {
            "example_id": self.example_id,
            "true_positives": self.true_positives,
            "false_positives": self.false_positives,
            "false_negatives": self.false_negatives,
            "matches": [
                {
                    "predicted_claim": m.predicted_claim,
                    "expected_claim": m.expected_claim,
                    "span_iou": round(m.iou, 3),
                }
                for m in self.matches
            ],
            "unmatched_predicted": self.unmatched_predicted,
            "unmatched_expected": self.unmatched_expected,
        }


def score_example(
    example_id: str,
    input_text: str,
    predicted: List[Dict[str, str]],
    expected: List[Dict[str, str]],
) -> ExampleScore:
    """Match predicted claims to expected claims by evidence-span overlap.

    ``predicted`` / ``expected`` items need ``claim`` and ``evidence_span``
    keys. Matching is greedy on descending IoU with a floor of
    SPAN_IOU_THRESHOLD; each claim participates in at most one match.
    """
    pred_ranges = [span_char_range(p.get("evidence_span", ""), input_text) for p in predicted]
    exp_ranges = [span_char_range(e.get("evidence_span", ""), input_text) for e in expected]

    candidates: List[Tuple[float, int, int]] = []
    for pi, pr in enumerate(pred_ranges):
        if pr is None:
            continue
        for ei, er in enumerate(exp_ranges):
            if er is None:
                continue
            iou = span_iou(pr, er)
            if iou >= SPAN_IOU_THRESHOLD:
                candidates.append((iou, pi, ei))

    candidates.sort(key=lambda c: (-c[0], c[1], c[2]))
    matched_pred: set = set()
    matched_exp: set = set()
    matches: List[ClaimMatch] = []
    for iou, pi, ei in candidates:
        if pi in matched_pred or ei in matched_exp:
            continue
        matched_pred.add(pi)
        matched_exp.add(ei)
        matches.append(
            ClaimMatch(
                predicted_claim=predicted[pi].get("claim", ""),
                expected_claim=expected[ei].get("claim", ""),
                iou=iou,
            )
        )

    unmatched_predicted = [
        p.get("claim", "") for i, p in enumerate(predicted) if i not in matched_pred
    ]
    unmatched_expected = [
        e.get("claim", "") for i, e in enumerate(expected) if i not in matched_exp
    ]
    return ExampleScore(
        example_id=example_id,
        true_positives=len(matches),
        false_positives=len(unmatched_predicted),
        false_negatives=len(unmatched_expected),
        matches=matches,
        unmatched_predicted=unmatched_predicted,
        unmatched_expected=unmatched_expected,
    )


def aggregate_scores(scores: List[ExampleScore]) -> Dict[str, float]:
    """Micro-averaged precision / recall / F1 across all examples."""
    tp = sum(s.true_positives for s in scores)
    fp = sum(s.false_positives for s in scores)
    fn = sum(s.false_negatives for s in scores)
    precision = tp / (tp + fp) if (tp + fp) else 0.0
    recall = tp / (tp + fn) if (tp + fn) else 0.0
    f1 = (2 * precision * recall / (precision + recall)) if (precision + recall) else 0.0
    return {
        "true_positives": tp,
        "false_positives": fp,
        "false_negatives": fn,
        "precision": round(precision, 4),
        "recall": round(recall, 4),
        "f1": round(f1, 4),
    }
