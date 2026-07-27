# GET /docs/{id} carries author display fields

**Date:** 2026-07-26
**Layer:** api (cross-link: [ui](../ui/2026-07-26-geometry-restoration.md))
**Todo:** docs/todos/ui-feature-restoration.md

`analysis/src/api/queries/docs.py` LEFT JOINs `corpus.authors` in the core
document query; `DocumentDetailResponse` adds `author_handle`,
`author_display_name`, `author_profile_image_url`, `author_verified`
(all nullable -- news docs rarely have author rows). Sole consumer is the
restored PostCard's author line and avatar. Contract snapshot
`docs_basic.json` re-recorded for the additive fields.
