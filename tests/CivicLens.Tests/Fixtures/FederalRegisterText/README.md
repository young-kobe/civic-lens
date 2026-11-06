# Federal Register body text fixtures

Fetched 2026-11-06 from the Federal Register API with the User-Agent `CivicLens/1.0 (+https://civic-lens.info)`. robots.txt allows these paths.

- `<number>.fields.json`: the answer of `https://www.federalregister.gov/api/v1/documents/<number>.json` with the fields `document_number`, `raw_text_url`, `body_html_url`, `executive_order_notes` and `disposition_notes`.
- `<number>.txt`: the body of `raw_text_url`, unchanged. The server sends it as `text/plain`, but it is a small HTML page with a `<pre>` block.

Documents: 2025-01900 (Executive Order 14147, has executive order notes), 2024-26714 (Proclamation 10857), 2024-25777 (Notice). The date in the `raw_text_url` path of 2024-26714 is 2024/11/14, one day after its publication date, so code must take the URL from the API field and never build it.
