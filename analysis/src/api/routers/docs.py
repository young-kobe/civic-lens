"""
Universal document drill-down router (Phase 9 strictly-live): GET
/docs/{doc_id} resolves any ingested document by id with NO time
predicate -- age never gates existence, only aggregate windows do.
"""

from __future__ import annotations

from fastapi import APIRouter, HTTPException

from analysis.src.api.models.docs import DocumentDetailResponse
from analysis.src.api.queries.docs import get_document

router = APIRouter(tags=["docs"])


@router.get("/docs/{doc_id}", response_model=DocumentDetailResponse)
def get_doc(doc_id: int) -> DocumentDetailResponse:
    """Document core fields + admission_class + source_url, subtype extras,
    every CURRENT analysis result, and citations in/out. 404 on an unknown
    doc_id -- otherwise resolves regardless of how old the document is."""
    payload = get_document(doc_id)
    if payload is None:
        raise HTTPException(status_code=404, detail=f"doc_id {doc_id} not found")
    return DocumentDetailResponse(**payload)
