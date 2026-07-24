"""
Floors and caps for the Phase 9 review service.
"""

from __future__ import annotations

# Public accuracy suppression floor -- ported verbatim from the pre-redesign
# reporting/review.py::ReviewService.MIN_PUBLIC_REVIEW_N so a review-derived
# UI figure doesn't shift only because the write path changed underneath it.
MIN_PUBLIC_REVIEW_N = 20

# Full-text preview length for one review-queue item -- long enough for a
# reviewer to judge the call without shipping the whole doc body over the
# wire. Ported verbatim from the old review.py's _TEXT_PREVIEW_CHARS.
TEXT_PREVIEW_CHARS = 1200
