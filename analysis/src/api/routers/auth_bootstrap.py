"""
Post-CF-Access bounce endpoint (walkthrough 050).

When the SPA's admin fetch hits a missing or expired Cloudflare Access
session, the browser can't follow CF's cross-origin 302 to the login page
from inside a ``fetch()`` call — it surfaces as either an opaque redirect
or a CORS TypeError. The UI detects this and top-level-navigates here.

This endpoint lives under the CF-Access-gated ``/api/v1/review/*`` path, so
the browser hits CF's login flow first, sets a fresh session cookie on its
way back, and then the tiny HTML below sends the user to wherever they
were in the SPA before the prompt (carried in ``sessionStorage``).

The endpoint is deliberately NOT gated by ``require_admin_token``: that
token is a fetch-layer header the browser never sends on a top-level
navigation, and the HTML payload contains no sensitive data — its sole
job is to bounce an already-CF-authenticated user back to ``/``.
"""

from fastapi import APIRouter
from fastapi.responses import HTMLResponse

router = APIRouter(tags=["auth_bootstrap"])


# Inline constants keep this router zero-dep and easy to audit. The script
# validates the stored return path is a same-origin absolute path (starts
# with "/" but not "//") before calling location.replace — a hostile tab
# can't coerce the bounce into an open-redirect.
_BOOTSTRAP_HTML = """<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<title>Signing in - Civic Lens</title>
<meta name="viewport" content="width=device-width,initial-scale=1">
<style>
 body{font-family:system-ui,-apple-system,BlinkMacSystemFont,sans-serif;
      margin:0;min-height:100vh;display:flex;align-items:center;
      justify-content:center;color:#334155;background:#f8fafc}
 main{text-align:center}
 p{margin:0;font-size:14px;letter-spacing:.02em}
</style>
</head>
<body>
<main>
<p>Signing in...</p>
<noscript>Authentication complete. <a href="/">Return to Civic Lens</a>.</noscript>
</main>
<script>
  (function(){
    var target = '/';
    try {
      var saved = sessionStorage.getItem('civic_post_auth_return');
      sessionStorage.removeItem('civic_post_auth_return');
      if (typeof saved === 'string'
          && saved.charAt(0) === '/'
          && saved.charAt(1) !== '/'
          && saved.charAt(1) !== '\\\\') {
        target = saved;
      }
    } catch (e) { /* sessionStorage may be blocked; fall back to / */ }
    window.location.replace(target);
  })();
</script>
</body>
</html>"""


@router.get("/review/bootstrap", response_class=HTMLResponse)
def bootstrap() -> HTMLResponse:
    # no-store so a cached 200 can't short-circuit a future re-auth: each
    # visit must pass CF Access fresh so the session cookie gets set.
    return HTMLResponse(
        content=_BOOTSTRAP_HTML,
        headers={"Cache-Control": "no-store"},
    )
