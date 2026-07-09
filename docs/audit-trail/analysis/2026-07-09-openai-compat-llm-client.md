# 2026-07-09 — OpenAI-compatible LLM client behind the factory seam

The LLM factory now supports a third backend: `CIVIC_LLM_BACKEND=openai_compat`
selects `OpenAICompatClient` (`analysis/src/llm/openai_compat.py`), which
speaks the OpenAI `/v1` REST surface — `/v1/chat/completions` with
role-separated messages and strict `json_schema` response_format,
`/v1/embeddings` for narrative clustering, `/v1/models` as the availability
probe. Because that surface is the lingua franca of self-hosted inference
(LiteLLM, vLLM, llama.cpp server, serverless GPU endpoints, a future custom
token router), adopting any of them later is an env-var change
(`CIVIC_LLM_BASE_URL`/`_API_KEY`/`_MODEL`) with zero engine edits. Nothing
selects this backend in production today — Gemini remains the prod backend.

## What shipped

- `analysis/src/llm/openai_compat.py` — `OpenAICompatClient(BaseLLMClient)`.
  Retry/backoff mirrors `ollama.py`/`gemini.py` (one shared policy across
  backends); JSON parsing + schema validation stay client-side via the
  inherited `parse_json_response()`, so schema enforcement is
  backend-agnostic even when the server ignores `strict`. `embed()` returns
  `None` on failure per the base contract (narrative clusterer falls back to
  Jaccard). Token accounting from the response `usage` block.
- `analysis/src/common/settings.py` — `llm_base_url`, `llm_api_key`,
  `llm_model`, `llm_embedding_model`, `llm_timeout` (all `CIVIC_`-prefixed);
  `.env.example` documents them.
- `analysis/src/llm/factory.py` — `get_openai_compat_client()` singleton +
  dispatch branch; `get_llm_client()` return union widened.
- `analysis/tests/test_openai_compat.py` — 9 tests pinning the swap
  contract: wire format (roles, strict json_schema payload, bearer auth),
  failures surfacing as the RuntimeError/ValueError the engines already
  catch for heuristic fallback, embed None-on-error, token accounting, and
  factory dispatch.

## Why

- Decision record (2026-07-09): self-hosted inference does not pencil out at
  current volume — worst-case pipeline load (~3,200 calls/day) is ~$14/mo on
  gemini-2.5-flash-lite vs $50+/mo for any always-on box, and the analyzed
  content is public discourse, so the data-locality upside is minor. Rather
  than deploy router infrastructure with nothing to route, this lands the
  interface so the cutover is cheap when sustained Gemini spend crosses
  ~$50/mo (roughly 5-10x current volume).

## Follow-ups

- Gated `gemini-2.5-flash` → `gemini-2.5-flash-lite` switch (golden-set eval
  baseline first): `docs/todos/containerization.md`.
- Companion entry for the compose stack this rides with:
  `../infra/2026-07-09-docker-compose-stack.md`.
