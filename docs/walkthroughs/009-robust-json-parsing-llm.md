# Robust JSON Parsing for LLM Responses

## Summary

Enhanced [base.py](file:///c:/Users/kobey/civic-lens/analysis/src/llm/base.py) with multi-layered JSON repair strategies to handle common LLM output issues observed in production (Ollama on Jetson).

## Changes Made

render_diffs(file:///c:/Users/kobey/civic-lens/analysis/src/llm/base.py)

### Repair Strategies Added

| Strategy | Handles |
|----------|---------|
| JSON extraction | LLM adds text before/after JSON |
| Trailing comma fix | `{"a": 1,}` -> `{"a": 1}` |
| Missing comma fix | `"value" "key2"` -> `"value", "key2"` |
| Newline escape fix | Unescaped `\n` in strings |

---

## Test Coverage

Added 7 new tests in [test_llm_engines.py](file:///c:/Users/kobey/civic-lens/analysis/tests/test_llm_engines.py):

- `test_valid_json` - Baseline
- `test_json_with_markdown_fences` - Code fence handling
- `test_json_with_trailing_comma` - Trailing comma repair
- `test_json_with_missing_comma` - Missing comma repair
- `test_json_with_surrounding_text` - JSON extraction
- `test_json_array_trailing_comma` - Array support
- `test_invalid_json_raises_error` - Error handling

## Verification

```
============================= 26 passed in 0.12s ==============================
```

All tests pass, including existing analyzer tests (sentiment, bot, favorability).

## Next Steps

Re-run `.\run.ps1 analyze` to observe reduced LLM parsing failures in production. Look for `DEBUG` messages indicating successful JSON repairs.
