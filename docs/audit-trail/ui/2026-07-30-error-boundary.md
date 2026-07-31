# 2026-07-30 — Root ErrorBoundary: render crashes degrade to ErrorState

`ui/src/components/common/ErrorBoundary.tsx` now wraps `<App />` at the
root (`main.tsx`). A render-time throw anywhere in the tree previously
unmounted the entire app to a blank screen with nothing logged; it now
renders the standard `ErrorState` (message + "Try again" that clears the
boundary) and logs the error with its component stack via console.error.

Part of the durable-error-log initiative
(`analysis/2026-07-30-durable-error-log.md`). The UI deliberately does
NOT report client errors to the server: an unauthenticated write endpoint
on a public site is an abuse surface (spam rows on a 2 GB disk) for
marginal value — UI errors reproduce in a browser console. If wanted
later, it belongs behind the CF-Access'd review path.
